// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Management.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.Management</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>3000</c>–<c>3999</c> band per the logging conventions. Values are append-only:
/// never renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 3000;

    public const int SecretGenerationFailed = Base + 1;
    public const int JsonPatchFailed = Base + 2;
    public const int MissingDependency = Base + 4;
    public const int LostConcurrencyRace = Base + 5;
    public const int ClientSettingsUnreadable = Base + 6;
    public const int TenantSettingsUnreadable = Base + 7;
    public const int PlatformSettingsUnreadable = Base + 8;
    public const int StoredSettingsUnreadable = Base + 9;
    public const int TenantEnvironmentTypeUnreadable = Base + 10;
    public const int TenantEnvironmentUnresolved = Base + 11;
    public const int TeamSettingsUnreadable = Base + 12;
    public const int OwnerRootKeyUnavailable = Base + 13;
    public const int AuditPublishFailed = Base + 14;
    public const int RemoteKeyProviderFailed = Base + 15;

    /// <summary>
    /// Identifies the warning that the guard that serializes the audit-stream creates of a tenant could not be
    /// released.
    /// </summary>
    public const int AuditStreamCreateGuardReleaseFailed = Base + 16;

    /// <summary>
    /// Identifies the warning that giving a tenant back its previous issuer in the global directory did not complete
    /// after the update that changed its issuer failed to write the tenant's row, so the directory holds the issuer the
    /// update would have given the tenant.
    /// </summary>
    public const int TenantIssuerChangeTakeBackFailed = Base + 17;

    /// <summary>
    /// Identifies the warning that setting the key custody of an imported tenant to held failed after the directory
    /// committed the move.
    /// </summary>
    public const int TenantKeyCustodyHeldFailed = Base + 18;
}
