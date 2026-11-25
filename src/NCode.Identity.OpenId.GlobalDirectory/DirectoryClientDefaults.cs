// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Names the path prefix of the directory service's API, the response size limit and the named HTTP client that reaches
/// the directory.
/// </summary>
internal static class DirectoryClientDefaults
{
    /// <summary>
    /// The path prefix of the directory API, relative to <see cref="DirectoryClientOptions.Url"/>.
    /// </summary>
    internal const string ApiPrefix = "directory/v1/";

    /// <summary>
    /// The most bytes that a directory response may carry.
    /// </summary>
    internal const int MaxResponseBytes = 1024 * 1024;

    /// <summary>
    /// The most bytes that the directory's token endpoint response may carry.
    /// </summary>
    internal const int MaxTokenResponseBytes = 64 * 1024;

    /// <summary>
    /// The name of the HTTP client that every call to the directory uses.
    /// </summary>
    internal const string HttpClientName = "NCode.Identity.OpenId.GlobalDirectory";

    /// <summary>
    /// Creates the primary handler of the directory's HTTP client. It never follows a redirect or keeps a cookie. When
    /// <see cref="DirectoryClientOptions.CaCertificatesPath"/> is set, it validates the directory's server certificate
    /// against only the certificates in that file; otherwise the system store applies.
    /// </summary>
    /// <param name="options">The <see cref="DirectoryClientOptions"/> of the cell.</param>
    /// <returns>The new <see cref="SocketsHttpHandler"/>.</returns>
    internal static SocketsHttpHandler CreateHandler(DirectoryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        if (string.IsNullOrEmpty(options.CaCertificatesPath))
            return handler;

        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(options.CaCertificatesPath);

        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
        };
        policy.CustomTrustStore.AddRange(authorities);

        handler.SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
        return handler;
    }
}
