// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Names the path prefix of the directory service's API, the path of the control-plane authority endpoint and the
/// response size limit.
/// </summary>
internal static class DirectoryClientDefaults
{
    /// <summary>
    /// The path prefix of the directory API, relative to <see cref="DirectoryClientOptions.Url"/>.
    /// </summary>
    internal const string ApiPrefix = "directory/v1/";

    /// <summary>
    /// The path of the control-plane authority endpoint, relative to the origin of the control-plane cell's issuer.
    /// </summary>
    internal const string AuthorityPath = "/control-plane/v1/authority";

    /// <summary>
    /// The path of the control-plane principals endpoint, relative to the origin of the control-plane cell's issuer.
    /// </summary>
    internal const string PrincipalsPath = "/control-plane/v1/principals";

    /// <summary>
    /// The most bytes that a directory response may carry.
    /// </summary>
    internal const int MaxResponseBytes = 1024 * 1024;
}
