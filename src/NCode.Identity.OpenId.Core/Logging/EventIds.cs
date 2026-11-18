// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.Core</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>1000</c>–<c>1999</c> band per the logging conventions. Values are append-only:
/// never renumber or reuse a shipped identifier.
/// </remarks>
internal static class EventIds
{
    private const int Base = 1000;

    public const int UnhandledException = Base + 1;

    public const int ClientError = Base + 2;

    public const int PlatformSettingsReloadRejected = Base + 3;

    public const int TenantKeyReseedFailed = Base + 5;

    public const int TenantChangeBroadcastFailed = Base + 6;

    public const int PlatformSettingsRefreshFailed = Base + 7;

    public const int PlatformSettingsChangeRefreshFailed = Base + 8;

    public const int PlatformSettingsChangeBroadcastFailed = Base + 9;

    public const int TeamSettingsReconciliationStarted = Base + 10;

    public const int TeamSettingsReconciled = Base + 11;

    public const int TeamSettingsReconciliationFailed = Base + 12;

    public const int ReconciledTeamSettingsUnreadable = Base + 13;

    public const int TenantCacheClearFailed = Base + 14;

    public const int PlatformMaterializationAtStartFailed = Base + 15;

    public const int TeamChangeBroadcastFailed = Base + 16;

    public const int TeamCacheClearFailed = Base + 17;

    public const int TenantKeyRolloverFailed = Base + 19;

    public const int TenantKeyRolloverPassFailed = Base + 20;

    public const int TenantKeyRolloverLostRace = Base + 21;

    public const int TenantSeedPostCommitStepFailed = Base + 23;

    public const int TenantKeyAuditPublishFailed = Base + 24;

    public const int TenantKeyExtended = Base + 22;

    public const int ProtectedValuePageConflict = Base + 25;

    public const int MasterKeyMaintenancePassFailed = Base + 26;

    public const int MasterKeyMaintenanceOwnerFailed = Base + 27;

    public const int MasterKeyRotationLostRace = Base + 28;

    public const int MasterKeyAuditPublishFailed = Base + 29;

    public const int ProtectedValueRewrapFailed = Base + 30;

    public const int ProtectedValuePageAbandoned = Base + 31;

    public const int ProtectedValuePageUnreadable = Base + 32;

    public const int TenantKeyRolloverTeamFailed = Base + 33;

    public const int OwnerRootKeyValidationPassFailed = Base + 34;

    public const int OwnerRootKeyValidationOwnerFailed = Base + 35;

    public const int OwnerRootKeyAuditPublishFailed = Base + 36;

    public const int TeamCacheRevalidationFailed = Base + 37;

    public const int TeamCacheEvictionFailed = Base + 38;

    public const int TemporarilyUnavailable = Base + 39;

    public const int TenantCacheRevalidationFailed = Base + 40;

    public const int TenantCacheEvictionFailed = Base + 41;

    public const int RemoteKeySweepSkipped = Base + 42;

    public const int RemoteKeyAuditPublishFailed = Base + 43;

    public const int RemoteKeySweepPassFailed = Base + 44;

    public const int RemoteKeySweepDispatchFailed = Base + 45;

    /// <summary>
    /// Identifies the warning that the directory retired a team that tenants or teams still name, so the cell disables
    /// the team instead of removing it.
    /// </summary>
    public const int DirectoryRetiredTeamHasDependents = Base + 46;

    /// <summary>
    /// Identifies the warning that a change to a team's root key for the cell's region was not applied; the next
    /// directory document for the team or the next reconciliation of root keys retries it.
    /// </summary>
    public const int DirectoryTeamRootKeyNotApplied = Base + 47;

    /// <summary>
    /// Identifies the warning that a mirrored team change was committed, but the command that announces it to the
    /// cell's replicas failed.
    /// </summary>
    public const int DirectoryTeamChangeNotSent = Base + 48;

    /// <summary>
    /// Identifies the warning that a mirrored platform change was committed, but the command that announces it to the
    /// cell's replicas failed.
    /// </summary>
    public const int DirectoryPlatformChangeNotSent = Base + 49;

    /// <summary>
    /// Identifies the informational message that a team document names a parent team the cell does not mirror, so the
    /// cell applies the team when the directory lists it again after its parent.
    /// </summary>
    public const int DirectoryTeamParentNotMirrored = Base + 50;
}
