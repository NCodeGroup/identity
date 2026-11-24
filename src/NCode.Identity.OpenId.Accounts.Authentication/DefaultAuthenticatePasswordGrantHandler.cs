// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Commands;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the resource-owner password grant implementation of the
/// <see cref="ICommandResponseHandler{AuthenticatePasswordGrantCommand, AuthenticateSubjectDisposition}"/> seam, backed
/// by the configured <see cref="ILocalAccountStore"/>. This handler is registered only when the host opts into the
/// local-account authentication capability, so its mere presence is what enables the grant; the default
/// <c>NCode.Identity.OpenId.Authentication</c> handler reports the grant as unsupported when this capability is absent.
/// A store that rejects the credentials yields a failed (not unsupported) result, so the two cases stay distinct. A
/// password for an account that reached its limit of failed attempts is verified against the store's dummy hash and
/// refused, as a wrong one is. A connection that requires the email second factor refuses the grant before any password
/// is verified, because the grant carries no second factor.
/// </summary>
internal class DefaultAuthenticatePasswordGrantHandler(
    IStoreManagerFactory storeManagerFactory,
    IConnectionResolver connectionResolver,
    LocalAccountStoreCatalog localAccountStoreCatalog,
    IPrincipalResolver principalResolver,
    IAuditEventRecorder auditEventRecorder,
    IPasswordAttemptLimiter passwordAttemptLimiter
) : ICommandResponseHandler<AuthenticatePasswordGrantCommand, AuthenticateSubjectDisposition>
{
    internal const string InvalidCredentialsErrorCode = "invalid_credentials";
    internal const string AttemptLimitReachedErrorCode = "attempt_limit_reached";

    internal const string SecondFactorRequiredErrorCode = "second_factor_required";

    internal const string InvalidCredentialsReason = "The provided credentials are invalid.";

    internal const string SecondFactorRequiredReason =
        "The connection requires a second factor, which the password grant cannot carry.";

    internal const string ConnectionNotUsableDescription =
        "The requested connection is not available for the password grant.";

    internal const string AttemptLimitReachedReason =
        "The account has reached its limit of failed password attempts.";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;
    private IPrincipalResolver PrincipalResolver { get; } = principalResolver;
    private IAuditEventRecorder AuditEventRecorder { get; } = auditEventRecorder;
    private IPasswordAttemptLimiter PasswordAttemptLimiter { get; } = passwordAttemptLimiter;

    internal virtual AuthenticateSubjectDisposition Failed() => new();

    internal virtual AuthenticateSubjectDisposition Authenticated(SubjectAuthentication ticket) =>
        new(ticket);

    /// <inheritdoc />
    public async ValueTask<AuthenticateSubjectDisposition> HandleAsync(
        AuthenticatePasswordGrantCommand command,
        CancellationToken cancellationToken
    )
    {
        var (openIdContext, client, tokenRequest, authenticationProperties) = command;

        // The grant serves only a local connection, which the request names or the client's default selects.
        var selection = await ConnectionResolver.SelectAsync(
            openIdContext,
            client.Settings,
            tokenRequest.Connection,
            cancellationToken
        );
        var connection = selection.Connection;
        if (
            selection.IsRefused
            || connection is null
            || !ConnectionStrategyNames.IsLocal(connection.Connection.Strategy)
        )
        {
            var description = selection.ErrorDescription ?? ConnectionNotUsableDescription;
            var error = openIdContext
                .ErrorFactory.InvalidRequest(description)
                .WithStatusCode(StatusCodes.Status400BadRequest);
            return new AuthenticateSubjectDisposition(error);
        }

        var username = tokenRequest.Username;
        var password = tokenRequest.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return Failed();
        }

        // The password grant carries no second factor, so a connection that requires one refuses it before any password
        // is verified (ADR-1533).
        if (connection.Settings.GetValue(OpenIdSettingKeys.EmailMfaRequired))
        {
            await AuditEventRecorder.RecordSubjectAuthenticationFailedAsync(
                openIdContext,
                SecondFactorRequiredErrorCode,
                SecondFactorRequiredReason,
                username,
                cancellationToken
            );
            return Failed();
        }

        var result = await VerifyCredentialsAsync(
            openIdContext,
            connection,
            username,
            password,
            cancellationToken
        );

        // A password refused at the limit is verified against the store's dummy hash and counts as a failure, as a
        // wrong one does, under the account key the store holds. See ADR-1237.
        if (result.IsVerified)
        {
            await PasswordAttemptLimiter.ResetAsync(
                openIdContext.Tenant.TenantId,
                connection.ConnectionId,
                result.AccountKey,
                cancellationToken
            );
        }
        else
        {
            // A failure is recorded even when the client disconnects.
            await PasswordAttemptLimiter.RecordFailureAsync(
                openIdContext,
                connection,
                result.AccountKey,
                CancellationToken.None
            );
        }

        if (result.IsRefused)
        {
            await AuditEventRecorder.RecordSubjectAuthenticationFailedAsync(
                openIdContext,
                AttemptLimitReachedErrorCode,
                AttemptLimitReachedReason,
                username,
                cancellationToken
            );
            return Failed();
        }

        var account = result.Account;
        if (account is not { IsEnabled: true })
        {
            await AuditEventRecorder.RecordSubjectAuthenticationFailedAsync(
                openIdContext,
                InvalidCredentialsErrorCode,
                InvalidCredentialsReason,
                username,
                cancellationToken
            );
            return Failed();
        }

        var subjectCommand = new CreatePasswordGrantSubjectCommand(
            openIdContext,
            connection,
            account
        );
        var subject = await openIdContext.Mediator.SendAsync<
            CreatePasswordGrantSubjectCommand,
            ClaimsPrincipal
        >(subjectCommand, cancellationToken);

        var principalId = await ResolvePrincipalIdAsync(
            openIdContext,
            subject,
            account.LocalAccountId,
            cancellationToken
        );

        var ticket = SubjectAuthentication.Create(
            openIdContext,
            OpenIdProtocol.GrantTypes.Password,
            authenticationProperties,
            subject,
            principalId
        );

        await AuditEventRecorder.RecordSubjectAuthenticatedAsync(
            openIdContext,
            principalId,
            cancellationToken
        );

        return Authenticated(ticket);
    }

    private async ValueTask<LocalAccountVerificationResult> VerifyCredentialsAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        // Encode the password into a pinned, zeroed-on-return buffer so the credential never lingers on the GC heap.
        using var owner = SecureEncoding.UTF8.RentBytes(
            password,
            isSensitive: true,
            out Memory<byte> buffer
        );

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection.Connection);
        var identifier = LocalAccountInteractiveCredentialVerifier.CreateIdentifier(
            connection,
            userName
        );

        // The store asks the gate once, with the account key it holds, between its lookup and its verification. The gate
        // refuses a result that a store verified after a refusal and fails a store that never asked, so no store lifts
        // the limit. See ADR-1237.
        var gate = new PasswordAttemptGate(PasswordAttemptLimiter, openIdContext, connection);
        var storedResult = await store.VerifyCredentialAsync(
            identifier,
            buffer,
            gate.IsRefusedAsync,
            cancellationToken
        );
        var result = gate.Enforce(storedResult);

        // Persist a transparent rehash-on-verify, when one occurred, within this unit of work.
        if (result.IsVerified)
        {
            await storeManager.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private async ValueTask<string> ResolvePrincipalIdAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal subject,
        string fallbackSubjectId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var principalId = await PrincipalResolver.ResolvePrincipalIdAsync(
            openIdContext,
            subject,
            storeManager,
            cancellationToken
        );
        await storeManager.SaveChangesAsync(cancellationToken);
        return string.IsNullOrEmpty(principalId) ? fallbackSubjectId : principalId;
    }
}
