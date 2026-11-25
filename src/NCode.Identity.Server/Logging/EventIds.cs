// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Server.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.Server</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>12000</c>–<c>12999</c> band per the logging conventions. Values are append-only:
/// never renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 12000;

    public const int BootstrapAdminClientSeeded = Base + 1;
    public const int BootstrapAdminClientSecretRefreshed = Base + 2;
    public const int ConformanceClientSeeded = Base + 3;
    public const int ConformanceTestUserSeeded = Base + 4;
    public const int LocalRootKeyInDeployment = Base + 5;
    public const int KeyRingKeyUndecryptable = Base + 6;
    public const int KeyRingKeyRevoked = Base + 7;
    public const int KeyRingMasterKeyNotLoaded = Base + 8;
    public const int PlatformSecretProtectorRefreshFailed = Base + 9;
    public const int SecretProtectionCommandConfigurationInvalid = Base + 10;
    public const int SecretProtectionCommandFailed = Base + 11;
    public const int SecretProtectionRecoveryKeyUnreadable = Base + 12;
    public const int SecretProtectionCommandOwnerHasNoMasterKeys = Base + 13;
    public const int SecretProtectionCommandUsageInvalid = Base + 14;
    public const int DeveloperPlatformMasterKeysReplaced = Base + 15;
    public const int DeveloperBffClientReplaced = Base + 16;
    public const int DeveloperBffClientSkipped = Base + 17;
    public const int InstanceSetupCommandUsageInvalid = Base + 18;
    public const int InstanceSetupCommandAlreadyInitialized = Base + 19;
    public const int InstanceSetupCommandRootUserNameMissing = Base + 20;
    public const int InstanceSetupCommandRefused = Base + 21;
    public const int InstanceSetupCommandRootCreated = Base + 22;
    public const int InstanceSetupCommandManagementConsoleRegistered = Base + 23;
    public const int InstanceSetupCommandAutomationClientCreated = Base + 24;
    public const int InstanceSetupCommandRootPasswordMissing = Base + 25;

    /// <summary>
    /// Identifies the error that the setup command ran on a cell that is neither the control plane nor a cell with a
    /// management tenant.
    /// </summary>
    public const int InstanceSetupCommandNotInAnAnchorCell = Base + 26;

    /// <summary>
    /// Identifies the error that the setup command failed with an exception, so the instance is not set up.
    /// </summary>
    public const int InstanceSetupCommandFailed = Base + 27;

    /// <summary>
    /// Identifies the error that the revoke-management-key command line is invalid.
    /// </summary>
    public const int RevokeManagementKeyCommandUsageInvalid = Base + 28;

    /// <summary>
    /// Identifies the error that the revoke-management-key command ran on the control-plane cell.
    /// </summary>
    public const int RevokeManagementKeyCommandOnTheControlPlane = Base + 29;

    /// <summary>
    /// Identifies the information that the revoke-management-key command recorded a key as revoked.
    /// </summary>
    public const int RevokeManagementKeyCommandRevoked = Base + 30;

    /// <summary>
    /// Identifies the error that the revoke-management-key command failed with an exception.
    /// </summary>
    public const int RevokeManagementKeyCommandFailed = Base + 31;

    /// <summary>
    /// Identifies the error that the recover-admin command line is invalid.
    /// </summary>
    public const int RecoverAdminCommandUsageInvalid = Base + 32;

    /// <summary>
    /// Identifies the error that the recover-admin command ran in a cell with no anchor tenant.
    /// </summary>
    public const int RecoverAdminCommandNotInAnAnchorCell = Base + 33;

    /// <summary>
    /// Identifies the error that the recover-admin command has no administrator username.
    /// </summary>
    public const int RecoverAdminCommandUserNameMissing = Base + 34;

    /// <summary>
    /// Identifies the error that the recover-admin command has no administrator password.
    /// </summary>
    public const int RecoverAdminCommandPasswordMissing = Base + 35;

    /// <summary>
    /// Identifies the error that the recover-admin command was refused.
    /// </summary>
    public const int RecoverAdminCommandRefused = Base + 36;

    /// <summary>
    /// Identifies the information that the recover-admin command recovered the administrator.
    /// </summary>
    public const int RecoverAdminCommandRecovered = Base + 37;

    /// <summary>
    /// Identifies the error that the recover-admin command failed with an exception.
    /// </summary>
    public const int RecoverAdminCommandFailed = Base + 38;

    /// <summary>
    /// Identifies the error that the recover-sign-in command line is invalid.
    /// </summary>
    public const int RecoverSignInCommandUsageInvalid = Base + 39;

    /// <summary>
    /// Identifies the error that the recover-sign-in command ran in a workload cell without a management tenant.
    /// </summary>
    public const int RecoverSignInCommandNoManagementTenant = Base + 40;

    /// <summary>
    /// Identifies the information that the recover-sign-in command made one change.
    /// </summary>
    public const int RecoverSignInCommandChanged = Base + 41;

    /// <summary>
    /// Identifies the information that the recover-sign-in command repaired the sign-in.
    /// </summary>
    public const int RecoverSignInCommandRepaired = Base + 42;

    /// <summary>
    /// Identifies the error that the recover-sign-in command failed with an exception.
    /// </summary>
    public const int RecoverSignInCommandFailed = Base + 43;

    /// <summary>
    /// Identifies the error that the directory-client-jwks command line is invalid.
    /// </summary>
    public const int DirectoryClientJwksCommandUsageInvalid = Base + 44;

    /// <summary>
    /// Identifies the error that the directory-client-jwks command was given no key ring.
    /// </summary>
    public const int DirectoryClientJwksCommandNoKeyRing = Base + 45;

    /// <summary>
    /// Identifies the error that the directory-client-jwks command was given an unknown key ring.
    /// </summary>
    public const int DirectoryClientJwksCommandUnknownKeyRing = Base + 46;

    /// <summary>
    /// Identifies the information that the directory-client-jwks command wrote the key set.
    /// </summary>
    public const int DirectoryClientJwksCommandWritten = Base + 47;

    /// <summary>
    /// Identifies the error that the directory-client-jwks command failed with an exception.
    /// </summary>
    public const int DirectoryClientJwksCommandFailed = Base + 48;
}
