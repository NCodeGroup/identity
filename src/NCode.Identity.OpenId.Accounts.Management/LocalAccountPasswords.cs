// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Judges the passwords set on local accounts against the owning connection's password policy and encodes them for the
/// store without leaving the plaintext on the GC heap.
/// </summary>
internal sealed class LocalAccountPasswords(
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IPasswordPolicy passwordPolicy
)
{
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;
    private IPasswordPolicy PasswordPolicy { get; } = passwordPolicy;

    // Resolves a connection's effective settings from the stored documents, so the tenant need not be served: the
    // tenant's effective settings (its team's settings under its own document), then the connection's document. A
    // document that cannot be honored is answered 409 by the management route group. See ADR-1236, ADR-1245 and
    // ADR-1535.
    public async ValueTask<(
        IReadOnlySettingCollection? Settings,
        ManagementError? Error
    )> ResolveConnectionSettingsAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        CancellationToken cancellationToken
    )
    {
        var tenantId = connection.TenantId;
        var tenantSettings = await ControlPlaneDefaultConnectionReader.ReadTenantSettingsAsync(
            EffectiveSettingsResolver,
            storeManager,
            tenantId,
            cancellationToken
        );
        if (tenantSettings is null)
        {
            var error = new ManagementError
            {
                StatusCode = StatusCodes.Status404NotFound,
                Detail = "The tenant was not found.",
            };
            return (null, error);
        }

        var connectionSettings = EffectiveSettingsResolver.ResolveStored(
            tenantSettings,
            OpenIdSettingScopes.Connection,
            connection.ConnectionId,
            connection.Settings.Value
        );
        return (connectionSettings, null);
    }

    // Judges a password an administrator sets by the connection's effective settings, resolved from its stored
    // documents so the tenant need not be served.
    public async ValueTask<ManagementError?> ValidateAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        PersistedLocalAccount account,
        string password,
        CancellationToken cancellationToken
    )
    {
        var (settings, error) = await ResolveConnectionSettingsAsync(
            storeManager,
            connection,
            cancellationToken
        );
        if (settings is null)
        {
            return error;
        }

        var passwordMemory = password.AsMemory();
        var policyContext = new PasswordPolicyContext
        {
            Settings = settings,
            UserName = account.UserName ?? string.Empty,
            Email = account.Email,
        };
        var result = await PasswordPolicy.ValidateAsync(
            policyContext,
            passwordMemory,
            cancellationToken
        );
        if (!result.IsRejected)
        {
            return null;
        }

        return new ManagementError
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Detail = result.ErrorDescription,
        };
    }

    // Encodes the password into a pinned, zeroed-on-return buffer so the plaintext never lingers on the GC heap.
    public static async ValueTask WithPasswordBytesAsync(
        string password,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> useAsync,
        CancellationToken cancellationToken
    )
    {
        using var owner = SecureEncoding.UTF8.RentBytes(
            password,
            isSensitive: true,
            out Memory<byte> buffer
        );
        await useAsync(buffer, cancellationToken);
    }
}
