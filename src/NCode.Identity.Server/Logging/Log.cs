// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.Server.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.Server</c> package.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.BootstrapAdminClientSeeded,
        Level = LogLevel.Information,
        Message = "Seeded bootstrap administrator client '{ClientId}' into tenant '{TenantId}'"
    )]
    internal static partial void BootstrapAdminClientSeeded(
        this ILogger logger,
        string clientId,
        string tenantId
    );

    [LoggerMessage(
        EventId = EventIds.BootstrapAdminClientSecretRefreshed,
        Level = LogLevel.Information,
        Message = "Refreshed the bootstrap administrator client '{ClientId}' secret in tenant '{TenantId}'"
    )]
    internal static partial void BootstrapAdminClientSecretRefreshed(
        this ILogger logger,
        string clientId,
        string tenantId
    );

    [LoggerMessage(
        EventId = EventIds.ConformanceClientSeeded,
        Level = LogLevel.Information,
        Message = "Seeded conformance client '{ClientId}' into tenant '{TenantId}'"
    )]
    internal static partial void ConformanceClientSeeded(
        this ILogger logger,
        string clientId,
        string tenantId
    );

    [LoggerMessage(
        EventId = EventIds.ConformanceTestUserSeeded,
        Level = LogLevel.Information,
        Message = "Seeded conformance test user '{UserName}' into tenant '{TenantId}'"
    )]
    internal static partial void ConformanceTestUserSeeded(
        this ILogger logger,
        string userName,
        string tenantId
    );

    [LoggerMessage(
        EventId = EventIds.LocalRootKeyInDeployment,
        Level = LogLevel.Warning,
        Message = "A deployment uses a local root key; prefer a key management service."
    )]
    internal static partial void LocalRootKeyInDeployment(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.KeyRingKeyUndecryptable,
        Level = LogLevel.Error,
        Message = "The Data Protection key '{KeyId}' cannot be decrypted with the platform's master key."
    )]
    internal static partial void KeyRingKeyUndecryptable(
        this ILogger logger,
        Exception exception,
        Guid keyId
    );

    [LoggerMessage(
        EventId = EventIds.KeyRingKeyRevoked,
        Level = LogLevel.Warning,
        Message = "Revoked the Data Protection key '{KeyId}': {Reason}."
    )]
    internal static partial void KeyRingKeyRevoked(this ILogger logger, Guid keyId, string reason);

    [LoggerMessage(
        EventId = EventIds.KeyRingMasterKeyNotLoaded,
        Level = LogLevel.Error,
        Message = "The master key {MasterKeyId} that a Data Protection key names is missing or cannot be unwrapped; the key ring gate reports the keys it protects."
    )]
    internal static partial void KeyRingMasterKeyNotLoaded(
        this ILogger logger,
        Exception exception,
        Guid masterKeyId
    );

    [LoggerMessage(
        EventId = EventIds.PlatformSecretProtectorRefreshFailed,
        Level = LogLevel.Warning,
        Message = "Loading the platform's secret protector again failed; the protector in use stays, and the next refresh tries again."
    )]
    internal static partial void PlatformSecretProtectorRefreshFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.SecretProtectionCommandConfigurationInvalid,
        Level = LogLevel.Error,
        Message = "The secret-protection command cannot run: the configuration is invalid."
    )]
    internal static partial void SecretProtectionCommandConfigurationInvalid(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.SecretProtectionCommandFailed,
        Level = LogLevel.Error,
        Message = "The secret-protection command failed."
    )]
    internal static partial void SecretProtectionCommandFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.SecretProtectionRecoveryKeyUnreadable,
        Level = LogLevel.Error,
        Message = "The recovery private key file '{Path}' cannot be read, or it is empty or too large."
    )]
    internal static partial void SecretProtectionRecoveryKeyUnreadable(
        this ILogger logger,
        string path,
        Exception? exception
    );

    [LoggerMessage(
        EventId = EventIds.SecretProtectionCommandOwnerHasNoMasterKeys,
        Level = LogLevel.Error,
        Message = "The owner '{Owner}' that --owner names has no master keys; nothing was rewrapped."
    )]
    internal static partial void SecretProtectionCommandOwnerHasNoMasterKeys(
        this ILogger logger,
        SecretOwner owner
    );

    [LoggerMessage(
        EventId = EventIds.SecretProtectionCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The secret-protection command line is invalid: {Error} {Usage}"
    )]
    internal static partial void SecretProtectionCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.DeveloperPlatformMasterKeysReplaced,
        Level = LogLevel.Warning,
        Message = "The platform's master keys cannot be unwrapped, so developer key pruning removed them and revoked every Data Protection key they protected; the platform starts over with a new master key"
    )]
    internal static partial void DeveloperPlatformMasterKeysReplaced(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.DeveloperBffClientReplaced,
        Level = LogLevel.Information,
        Message = "Replaced the stored developer BFF client '{ClientId}' with its configuration, because the stored client differs from it"
    )]
    internal static partial void DeveloperBffClientReplaced(this ILogger logger, string clientId);

    [LoggerMessage(
        EventId = EventIds.DeveloperBffClientSkipped,
        Level = LogLevel.Warning,
        Message = "The developer BFF client '{ClientId}' is not seeded because its configuration is incomplete; set {MissingKeys}"
    )]
    internal static partial void DeveloperBffClientSkipped(
        this ILogger logger,
        string clientId,
        string missingKeys
    );

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The setup command line is invalid: {Error} {Usage}"
    )]
    internal static partial void InstanceSetupCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandAlreadyInitialized,
        Level = LogLevel.Information,
        Message = "The instance is already initialized; setup already ran and creates nothing."
    )]
    internal static partial void InstanceSetupCommandAlreadyInitialized(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandRootUserNameMissing,
        Level = LogLevel.Error,
        Message = "The setup command needs the root administrator's Setup:Root:UserName."
    )]
    internal static partial void InstanceSetupCommandRootUserNameMissing(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandRefused,
        Level = LogLevel.Error,
        Message = "The setup command was refused with status code {StatusCode}; the host's options validation names each configuration rule at start."
    )]
    internal static partial void InstanceSetupCommandRefused(this ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandRootCreated,
        Level = LogLevel.Information,
        Message = "Created the root administrator '{UserName}' in the control-plane tenant '{TenantId}'."
    )]
    internal static partial void InstanceSetupCommandRootCreated(
        this ILogger logger,
        string userName,
        string tenantId
    );

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandManagementConsoleRegistered,
        Level = LogLevel.Information,
        Message = "Registered the management console's client '{ClientId}' in the control-plane tenant."
    )]
    internal static partial void InstanceSetupCommandManagementConsoleRegistered(
        this ILogger logger,
        string clientId
    );

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandAutomationClientCreated,
        Level = LogLevel.Information,
        Message = "Registered the automation client '{ClientId}' in the control-plane tenant."
    )]
    internal static partial void InstanceSetupCommandAutomationClientCreated(
        this ILogger logger,
        string clientId
    );

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandRootPasswordMissing,
        Level = LogLevel.Error,
        Message = "The setup command needs the root administrator's Setup:Root:Password."
    )]
    internal static partial void InstanceSetupCommandRootPasswordMissing(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandNotOnTheControlPlane,
        Level = LogLevel.Error,
        Message = "Setup belongs to the control-plane cell, and this cell is not the control plane; run the setup command there."
    )]
    internal static partial void InstanceSetupCommandNotOnTheControlPlane(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.InstanceSetupCommandFailed,
        Level = LogLevel.Error,
        Message = "The setup command failed; the instance is not set up."
    )]
    internal static partial void InstanceSetupCommandFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.RevokeManagementKeyCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The revoke-management-key command line is invalid: {Error} {Usage}"
    )]
    internal static partial void RevokeManagementKeyCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.RevokeManagementKeyCommandOnTheControlPlane,
        Level = LogLevel.Error,
        Message = "The control-plane cell revokes a management key through the management API; run the revoke-management-key command in a workload cell."
    )]
    internal static partial void RevokeManagementKeyCommandOnTheControlPlane(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.RevokeManagementKeyCommandRevoked,
        Level = LogLevel.Information,
        Message = "Recorded the management key '{KeyId}' as revoked; the cell refuses every token it signed."
    )]
    internal static partial void RevokeManagementKeyCommandRevoked(
        this ILogger logger,
        string keyId
    );

    [LoggerMessage(
        EventId = EventIds.RevokeManagementKeyCommandFailed,
        Level = LogLevel.Error,
        Message = "The revoke-management-key command failed; the key is not revoked."
    )]
    internal static partial void RevokeManagementKeyCommandFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The recover-admin command line is invalid: {Error} {Usage}"
    )]
    internal static partial void RecoverAdminCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandNotOnTheControlPlane,
        Level = LogLevel.Error,
        Message = "The recover-admin command recovers the administrator of the control-plane cell; this cell is a workload cell."
    )]
    internal static partial void RecoverAdminCommandNotOnTheControlPlane(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandUserNameMissing,
        Level = LogLevel.Error,
        Message = "The recover-admin command needs the administrator's username in the Recovery:Admin:UserName configuration."
    )]
    internal static partial void RecoverAdminCommandUserNameMissing(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandPasswordMissing,
        Level = LogLevel.Error,
        Message = "The recover-admin command needs the administrator's password in the Recovery:Admin:Password configuration."
    )]
    internal static partial void RecoverAdminCommandPasswordMissing(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandRefused,
        Level = LogLevel.Error,
        Message = "The recover-admin command was refused with status {StatusCode}; the administrator is not recovered."
    )]
    internal static partial void RecoverAdminCommandRefused(this ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandRecovered,
        Level = LogLevel.Information,
        Message = "Recovered the administrator '{UserName}' in tenant {TenantId}; the account was created: {Created}."
    )]
    internal static partial void RecoverAdminCommandRecovered(
        this ILogger logger,
        string userName,
        string tenantId,
        bool created
    );

    [LoggerMessage(
        EventId = EventIds.RecoverAdminCommandFailed,
        Level = LogLevel.Error,
        Message = "The recover-admin command failed; the administrator is not recovered."
    )]
    internal static partial void RecoverAdminCommandFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.RecoverSignInCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The recover-sign-in command line is invalid: {Error} {Usage}"
    )]
    internal static partial void RecoverSignInCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.RecoverSignInCommandNoManagementTenant,
        Level = LogLevel.Error,
        Message = "The recover-sign-in command repairs the management tenant of a cell; this cell is a workload cell without one."
    )]
    internal static partial void RecoverSignInCommandNoManagementTenant(this ILogger logger);

    [LoggerMessage(
        EventId = EventIds.RecoverSignInCommandChanged,
        Level = LogLevel.Information,
        Message = "Repaired the sign-in: {Scope} '{Name}' changed; the value before was {Before}."
    )]
    internal static partial void RecoverSignInCommandChanged(
        this ILogger logger,
        string scope,
        string name,
        string? before
    );

    [LoggerMessage(
        EventId = EventIds.RecoverSignInCommandRepaired,
        Level = LogLevel.Information,
        Message = "The recover-sign-in command made {Count} change(s)."
    )]
    internal static partial void RecoverSignInCommandRepaired(this ILogger logger, int count);

    [LoggerMessage(
        EventId = EventIds.RecoverSignInCommandFailed,
        Level = LogLevel.Error,
        Message = "The recover-sign-in command failed; the sign-in is not repaired."
    )]
    internal static partial void RecoverSignInCommandFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryClientJwksCommandUsageInvalid,
        Level = LogLevel.Error,
        Message = "The directory-client-jwks command line is invalid: {Error} {Usage}"
    )]
    internal static partial void DirectoryClientJwksCommandUsageInvalid(
        this ILogger logger,
        string error,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryClientJwksCommandNoKeyRing,
        Level = LogLevel.Error,
        Message = "The directory-client-jwks command names no key ring and Directory:KeyRing is not set. {Usage}"
    )]
    internal static partial void DirectoryClientJwksCommandNoKeyRing(
        this ILogger logger,
        string usage
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryClientJwksCommandUnknownKeyRing,
        Level = LogLevel.Error,
        Message = "The directory-client-jwks command found no key ring named '{KeyRing}'."
    )]
    internal static partial void DirectoryClientJwksCommandUnknownKeyRing(
        this ILogger logger,
        string keyRing
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryClientJwksCommandWritten,
        Level = LogLevel.Information,
        Message = "The directory-client-jwks command wrote {Count} public key(s) of the key ring '{KeyRing}'."
    )]
    internal static partial void DirectoryClientJwksCommandWritten(
        this ILogger logger,
        string keyRing,
        int count
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryClientJwksCommandFailed,
        Level = LogLevel.Error,
        Message = "The directory-client-jwks command failed; no key set is written."
    )]
    internal static partial void DirectoryClientJwksCommandFailed(
        this ILogger logger,
        Exception exception
    );
}
