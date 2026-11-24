// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the audiences, scopes, endpoint paths and client identifier prefix that the global directory API shares
/// between the cells that call it and the directory that serves it, and the wait that a route asks of its caller when
/// the directory or the issuer of its access tokens cannot be reached.
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
