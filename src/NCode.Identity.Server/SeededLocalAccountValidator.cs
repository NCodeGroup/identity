// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Settings;

namespace NCode.Identity.Server;

/// <summary>
/// Checks the password of a local account that a configuration seeder is about to create, so a seeder stores no
/// password that the connection's password policy refuses.
/// </summary>
// See ADR-1236.
internal sealed class SeededLocalAccountValidator(
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IPasswordPolicy passwordPolicy
)
{
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;
    private IPasswordPolicy PasswordPolicy { get; } = passwordPolicy;

    /// <summary>
    /// Ensures that the password of a local account a seeder is about to create meets the password policy of the
    /// connection the account is seeded into, resolved through its tenant's.
    /// </summary>
    /// <param name="seeded">The <see cref="SeededLocalAccount"/> the seeder is about to create.</param>
    /// <param name="password">The password the seeder is about to set.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> for the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the account can be seeded.</returns>
    /// <exception cref="InvalidOperationException">
    /// The account's user name equals another account's email address or its email address equals another account's
    /// user name in the connection, or the password policy refuses the password.
    /// </exception>
    public async ValueTask EnsureCanBeSeededAsync(
        SeededLocalAccount seeded,
        string password,
        CancellationToken cancellationToken
    )
    {
        var accountKind = seeded.AccountKind;
        var tenant = seeded.Tenant;
        var connection = seeded.Connection;
        var account = seeded.Account;

        var proposal = new LocalAccountIdentifierProposal
        {
            UserName = account.UserName,
            Email = account.Email,
        };
        var shadowing = await LocalAccountIdentifierShadowing.FindAsync(
            seeded.Store,
            connection.ConnectionId,
            proposal,
            cancellationToken
        );
        if (shadowing.UserNameIsAnEmail)
        {
            throw new InvalidOperationException(
                $"The {accountKind} '{account.UserName}' cannot be seeded: the user name is another account's email address."
            );
        }

        if (shadowing.EmailIsAUserName)
        {
            throw new InvalidOperationException(
                $"The {accountKind} '{account.UserName}' cannot be seeded: the email address is another account's user name."
            );
        }

        var teamSettings = await EffectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        var tenantSettings = EffectiveSettingsResolver.Resolve(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenant.Settings.Value
        );

        var connectionSettings = EffectiveSettingsResolver.ResolveStored(
            tenantSettings,
            OpenIdSettingScopes.Connection,
            connection.ConnectionId,
            connection.Settings.Value
        );

        var passwordMemory = password.AsMemory();
        var policyContext = new PasswordPolicyContext
        {
            Settings = connectionSettings,
            UserName = account.UserName ?? string.Empty,
            Email = account.Email,
        };
        var result = await PasswordPolicy.ValidateAsync(
            policyContext,
            passwordMemory,
            cancellationToken
        );
        if (result.IsRejected)
        {
            throw new InvalidOperationException(
                $"The {accountKind} '{account.UserName}' cannot be seeded: {result.ErrorDescription}"
            );
        }
    }
}
