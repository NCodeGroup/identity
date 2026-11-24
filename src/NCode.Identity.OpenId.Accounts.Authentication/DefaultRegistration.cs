// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Authentication;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.UserInfo.Commands;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.Registration;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides extension methods to register the local-account authentication capability: the resource-owner password
/// grant handlers that consume the configured <c>ILocalAccountStore</c>.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    // The account-sourced UserInfo enricher is grant-agnostic (any access token issued for a local account), so both
    // the password-grant and interactive registrations wire it; TryAddEnumerable keys on the implementation type, so
    // registering it from both entry points adds it exactly once.
    private static void AddLocalAccountUserInfoClaims(IServiceCollection serviceCollection)
    {
        var descriptor = ServiceDescriptor.Singleton<
            ICommandHandler<GetUserInfoClaimsCommand>,
            DefaultGetLocalAccountUserInfoClaimsHandler
        >();
        serviceCollection.TryAddEnumerable(descriptor);
    }

    // The subject-validation handler is grant-agnostic (it runs wherever an end user's standing is validated), so both
    // the password-grant and interactive registrations wire it; TryAddEnumerable adds it exactly once.
    private static void AddLocalAccountSubjectValidation(IServiceCollection serviceCollection)
    {
        var descriptor = ServiceDescriptor.Singleton<
            ICommandHandler<ValidateSubjectStandingCommand>,
            DefaultValidateLocalAccountSubjectHandler
        >();
        serviceCollection.TryAddEnumerable(descriptor);
    }

    // The subject factory is shared by the password grant and the interactive sign-in, so both registrations wire it;
    // TryAddSingleton adds it exactly once.
    private static void AddLocalAccountSubjectFactory(IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton<LocalAccountSubjectFactory>();
    }

    /// <param name="builder">The <see cref="OpenIdAuthenticationLibrary"/> builder these capabilities extend.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Registers the resource-owner password grant capability over the configured <c>ILocalAccountStore</c>. This
        /// overrides the base <c>NCode.Identity.OpenId.Authentication</c> handler that reports the password grant as
        /// unsupported, so calling it is what enables the grant. It also registers the account-sourced UserInfo
        /// scope-claims enricher, so the UserInfo endpoint returns the subject's profile/email/address/phone claims
        /// from the local account (OpenID Connect Core 1.0 §5.4). It also registers the subject-validation handler that
        /// refuses a subject whose local account is disabled or no longer exists.
        /// </summary>
        /// <param name="localAccountStore">The local-account store builder — the compile-time proof that a
        /// <c>ILocalAccountStore</c> is registered, so this capability cannot be added without one.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddLocalAccountAuthentication(
            IServiceBuilder<LocalAccountLibrary> localAccountStore
        )
        {
            ArgumentNullException.ThrowIfNull(localAccountStore);

            var serviceCollection = builder.ServiceCollection;

            // Replace (not TryAdd) so this capability wins over the base "unsupported password grant" default handler.
            var passwordGrantDescriptor = ServiceDescriptor.Singleton<
                ICommandResponseHandler<
                    AuthenticatePasswordGrantCommand,
                    AuthenticateSubjectDisposition
                >,
                DefaultAuthenticatePasswordGrantHandler
            >();
            serviceCollection.Replace(passwordGrantDescriptor);

            serviceCollection.TryAddSingleton<
                ICommandResponseHandler<CreatePasswordGrantSubjectCommand, ClaimsPrincipal>,
                DefaultCreatePasswordGrantSubjectHandler
            >();

            AddLocalAccountSubjectFactory(serviceCollection);
            AddLocalAccountUserInfoClaims(serviceCollection);
            AddLocalAccountSubjectValidation(serviceCollection);

            return builder;
        }

        /// <summary>
        /// Registers the local-account implementation of the <see cref="IInteractiveCredentialVerifier"/> seam, so the
        /// opt-in interactive login/consent surface (<c>AddInteractiveAuthentication</c>) verifies credentials against
        /// the configured <c>ILocalAccountStore</c> — the same credential path the resource-owner password grant uses.
        /// The host registers the interactive surface separately. A host fronting a different backend (LDAP, an
        /// external identity provider, passwordless) registers its own <see cref="IInteractiveCredentialVerifier"/>
        /// instead. It also registers the subject-validation handler that refuses a subject whose local account is
        /// disabled or no longer exists, the <see cref="IInteractivePasswordChanger"/> that lets an end user change the
        /// password of their local account on the self-service surface, and the
        /// <see cref="IInteractivePasswordResetter"/>, <see cref="IInteractiveEmailVerifier"/> and
        /// <see cref="IInteractiveSignInCodeChallenge"/> that let an end user reset a forgotten password, verify an
        /// email address and complete a sign-in with a code mailed to the account's address.
        /// </summary>
        /// <param name="localAccountStore">The local-account store builder — the compile-time proof that a
        /// <c>ILocalAccountStore</c> is registered, so this capability cannot be added without one.</param>
        /// <param name="mailLibrary">The mail library builder — the compile-time proof that the codes this capability
        /// sends have a sender.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddLocalAccountInteractiveAuthentication(
            IServiceBuilder<LocalAccountLibrary> localAccountStore,
            IServiceBuilder<MailLibrary> mailLibrary
        )
        {
            ArgumentNullException.ThrowIfNull(localAccountStore);
            ArgumentNullException.ThrowIfNull(mailLibrary);

            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton<
                IInteractiveCredentialVerifier,
                LocalAccountInteractiveCredentialVerifier
            >();

            serviceCollection.TryAddSingleton<
                ILocalAccountEmailCodes,
                DefaultLocalAccountEmailCodes
            >();

            // The queued mail of a code is composed at each delivery attempt. See ADR-1533.
            var mailComposerDescriptor = ServiceDescriptor.Singleton<
                IMailComposer,
                LocalAccountMailComposer
            >();
            serviceCollection.TryAddEnumerable(mailComposerDescriptor);

            // Replace (not TryAdd) so this capability wins over the surface's default changer, which reports that no
            // change is supported, whatever the order the two registrations run in.
            // See ADR-1230.
            var passwordChangerDescriptor = ServiceDescriptor.Singleton<
                IInteractivePasswordChanger,
                LocalAccountInteractivePasswordChanger
            >();
            serviceCollection.Replace(passwordChangerDescriptor);

            // The same holds for the three seams of the one-time email codes. See ADR-1533.
            var passwordResetterDescriptor = ServiceDescriptor.Singleton<
                IInteractivePasswordResetter,
                LocalAccountInteractivePasswordResetter
            >();
            serviceCollection.Replace(passwordResetterDescriptor);

            var emailVerifierDescriptor = ServiceDescriptor.Singleton<
                IInteractiveEmailVerifier,
                LocalAccountInteractiveEmailVerifier
            >();
            serviceCollection.Replace(emailVerifierDescriptor);

            var signInCodeChallengeDescriptor = ServiceDescriptor.Singleton<
                IInteractiveSignInCodeChallenge,
                LocalAccountInteractiveSignInCodeChallenge
            >();
            serviceCollection.Replace(signInCodeChallengeDescriptor);

            serviceCollection.TryAddSingleton<LocalAccountSignedInAccountFinder>();

            AddLocalAccountSubjectFactory(serviceCollection);
            AddLocalAccountUserInfoClaims(serviceCollection);
            AddLocalAccountSubjectValidation(serviceCollection);

            return builder;
        }
    }
}
