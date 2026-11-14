// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Names the audience, the scope and the path prefix of the directory service's API.
/// </summary>
internal static class DirectoryClientDefaults
{
    /// <summary>
    /// The audience of the access token that every directory call presents.
    /// </summary>
    internal const string Audience = "urn:ncode:directory";

    /// <summary>
    /// The scope that the access token carries.
    /// </summary>
    internal const string Scope = "directory";

    /// <summary>
    /// The path prefix of the directory API, relative to <see cref="DirectoryClientOptions.Url"/>.
    /// </summary>
    internal const string ApiPrefix = "directory/v1/";

    /// <summary>
    /// The audience of the access token that every control-plane authority call presents.
    /// </summary>
    internal const string ControlPlaneAudience = "urn:ncode:control-plane";

    /// <summary>
    /// The scope that the control-plane access token carries.
    /// </summary>
    internal const string ControlPlaneScope = "control-plane";

    /// <summary>
    /// The path of the control-plane authority endpoint, relative to the origin of the control-plane cell's issuer.
    /// </summary>
    internal const string AuthorityPath = "/control-plane/v1/authority";

    /// <summary>
    /// The most bytes that a directory response may carry.
    /// </summary>
    internal const int MaxResponseBytes = 1024 * 1024;
}
