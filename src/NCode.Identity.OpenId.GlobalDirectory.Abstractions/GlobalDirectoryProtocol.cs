// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the audiences, scopes, endpoint paths and client identifier prefix that the global directory API and the
/// control plane authority API share between the cells that call them and the control-plane cell that serves them, and
/// the wait that a route asks of its caller when the directory, the control plane or the issuer of its access tokens
/// cannot be reached.
/// </summary>
[PublicAPI]
public static class GlobalDirectoryProtocol
{
    /// <summary>
    /// Contains the reserved audience identifier for the global directory API: 'urn:ncode:directory'. The control-plane
    /// cell's root tenant seeds the resource server that carries it.
    /// </summary>
    public const string DirectoryAudience = "urn:ncode:directory";

    /// <summary>
    /// Contains the single scope of the global directory API: 'directory'.
    /// </summary>
    public const string DirectoryScope = "directory";

    /// <summary>
    /// Contains the reserved audience identifier for the control plane API that the other cells call:
    /// 'urn:ncode:control-plane'. The control-plane cell's root tenant seeds the resource server that carries it.
    /// </summary>
    public const string ControlPlaneAudience = "urn:ncode:control-plane";

    /// <summary>
    /// Contains the single scope of the control plane API: 'control-plane'.
    /// </summary>
    public const string ControlPlaneScope = "control-plane";

    /// <summary>
    /// Contains the path of the control plane authority endpoint, relative to the origin of the control-plane cell's
    /// issuer: '/control-plane/v1/authority'.
    /// </summary>
    public const string ControlPlaneAuthorityPath = "/control-plane/v1/authority";

    /// <summary>
    /// Contains the path of the control plane principals endpoint, relative to the origin of the control-plane cell's
    /// issuer: '/control-plane/v1/principals'.
    /// </summary>
    public const string ControlPlanePrincipalsPath = "/control-plane/v1/principals";

    /// <summary>
    /// Contains the prefix of the machine client identifier of a cell: 'cell-{name}'.
    /// </summary>
    public const string CellClientIdPrefix = "cell-";

    /// <summary>
    /// Contains the path of the platform authority document, relative to the directory's origin:
    /// '/directory/v1/platform/authority'.
    /// </summary>
    public const string PlatformAuthorityPath = "/directory/v1/platform/authority";

    /// <summary>
    /// Contains the path of the management keys document, relative to the directory's origin:
    /// '/directory/v1/platform/management-keys'.
    /// </summary>
    public const string ManagementKeysPath = "/directory/v1/platform/management-keys";

    /// <summary>
    /// Contains the format of the path of a team's authority document, relative to the directory's origin, whose
    /// argument is the team identifier: '/directory/v1/teams/{0}/authority'.
    /// </summary>
    public const string TeamAuthorityPathFormat = "/directory/v1/teams/{0}/authority";

    /// <summary>
    /// Contains the path of the revisions of every replicated document, relative to the directory's origin:
    /// '/directory/v1/replicated-revisions'.
    /// </summary>
    public const string ReplicatedRevisionsPath = "/directory/v1/replicated-revisions";

    /// <summary>
    /// The seconds a route asks the caller to wait when the directory, the control plane or the issuer of its access
    /// tokens cannot be reached.
    /// </summary>
    public const int UnavailableRetryAfterSeconds = 5;
}
