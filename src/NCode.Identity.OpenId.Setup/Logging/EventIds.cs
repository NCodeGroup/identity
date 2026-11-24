// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Setup.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.Setup</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>29000</c>–<c>29999</c> band per the logging conventions. Values are append-only:
/// never renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 29000;

    public const int SetupTokenIssued = Base + 1;
    public const int SetupTokenIssuedByAnotherReplica = Base + 2;
    public const int RootAdministratorCreated = Base + 3;
    public const int InstanceSetupClientRegistered = Base + 4;

    /// <summary>
    /// Identifies the informational message that setup belongs to the control-plane cell, so this cell prepares no
    /// setup token.
    /// </summary>
    public const int SetupBelongsToTheControlPlane = Base + 5;

    /// <summary>
    /// Identifies the informational message that recovery created or restored an administrator.
    /// </summary>
    public const int AdministratorRecovered = Base + 6;

    /// <summary>
    /// Identifies the informational message that sign-in recovery repaired one setting or resource.
    /// </summary>
    public const int SignInRepaired = Base + 7;

    /// <summary>
    /// Identifies the warning that the directory could not be reached to publish the repaired platform settings.
    /// </summary>
    public const int SignInRepairNotPublished = Base + 8;
}
