// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NCode.Buffers;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public sealed class DirectoryHttpClientTrustTests : IDisposable
{
    private DirectoryInfo Folder { get; } =
        Directory.CreateTempSubdirectory("directory-http-client-trust");

    public void Dispose()
    {
        Folder.Delete(recursive: true);
        Cancellation.Dispose();
    }

    private CancellationTokenSource Cancellation { get; } = new(TimeSpan.FromSeconds(20));

    private CancellationToken Timeout() => Cancellation.Token;

    [Fact]
    public async Task CreateHandler_CertificateFromConfiguredAuthority_IsAccepted()
    {
        using var authority = CreateAuthority("CN=directory-ca");
        using var server = CreateServerCertificate(authority);
        var options = new DirectoryClientOptions
        {
            CaCertificatesPath = WriteAuthorities(authority),
        };

        var status = await GetStatusAsync(server, options, Timeout());

        Assert.Equal(HttpStatusCode.NoContent, status);
    }

    [Fact]
    public async Task CreateHandler_CertificateFromAnotherAuthority_IsRefused()
    {
        using var authority = CreateAuthority("CN=directory-ca");
        using var other = CreateAuthority("CN=other-ca");
        using var server = CreateServerCertificate(authority);
        var options = new DirectoryClientOptions { CaCertificatesPath = WriteAuthorities(other) };

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await GetStatusAsync(server, options, Timeout())
        );
    }

    [Fact]
    public async Task CreateHandler_WithoutPath_UsesTheSystemStore()
    {
        using var authority = CreateAuthority("CN=directory-ca");
        using var server = CreateServerCertificate(authority);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await GetStatusAsync(server, new DirectoryClientOptions(), Timeout())
        );
    }

    [Fact]
    public void CreateHandler_WithPath_DoesNotReplaceTheSystemStoreOfTheProcess()
    {
        using var authority = CreateAuthority("CN=directory-ca");
        var options = new DirectoryClientOptions
        {
            CaCertificatesPath = WriteAuthorities(authority),
        };

        using var handler = DirectoryClientDefaults.CreateHandler(options);

        var policy = handler.SslOptions.CertificateChainPolicy;
        Assert.NotNull(policy);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
        Assert.Single(policy.CustomTrustStore);
    }

    [Fact]
    public void CreateHandler_WithoutPath_HasNoChainPolicy()
    {
        using var handler = DirectoryClientDefaults.CreateHandler(new DirectoryClientOptions());

        Assert.Null(handler.SslOptions.CertificateChainPolicy);
    }

    [Fact]
    public void Validate_MissingFile_Fails()
    {
        var options = new DirectoryClientOptions
        {
            CaCertificatesPath = Path.Combine(Folder.FullName, "missing.pem"),
        };

        var result = ValidateCaCertificates(options);

        Assert.True(result.Failed);
        Assert.Contains("CaCertificatesPath", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FileWithoutCertificates_Fails()
    {
        var path = Path.Combine(Folder.FullName, "empty.pem");
        File.WriteAllText(path, "no certificates here\n", SecureEncoding.UTF8);

        var result = ValidateCaCertificates(
            new DirectoryClientOptions { CaCertificatesPath = path }
        );

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_FileWithCertificate_Succeeds()
    {
        using var authority = CreateAuthority("CN=directory-ca");
        var options = new DirectoryClientOptions
        {
            CaCertificatesPath = WriteAuthorities(authority),
        };

        var result = ValidateCaCertificates(options);

        Assert.True(result.Succeeded);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult ValidateCaCertificates(
        DirectoryClientOptions options
    )
    {
        options.Url = "https://directory.example";
        options.ClientId = "cell-a";
        options.KeyRing = "ring";
        var validator = new DirectoryClientOptionsValidator();
        return validator.Validate(null, options);
    }

    private string WriteAuthorities(X509Certificate2 authority)
    {
        var path = Path.Combine(Folder.FullName, Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, authority.ExportCertificatePem(), SecureEncoding.UTF8);
        return path;
    }

    private static X509Certificate2 CreateAuthority(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true)
        );
        var now = DateTimeOffset.UtcNow;
        var created = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));
        return created;
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 authority)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)
        );
        var now = DateTimeOffset.UtcNow;
        var serial = RandomNumberGenerator.GetBytes(8);
        using var issued = request.Create(
            authority,
            now.AddMinutes(-1),
            now.AddMinutes(30),
            serial
        );
        var withKey = issued.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
    }

    private static async ValueTask<HttpStatusCode> GetStatusAsync(
        X509Certificate2 serverCertificate,
        DirectoryClientOptions options,
        CancellationToken cancellationToken
    )
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serving = ServeOnceAsync(listener, serverCertificate, cancellationToken);
        using var handler = DirectoryClientDefaults.CreateHandler(options);
        using var client = new HttpClient(handler);
        try
        {
            using var response = await client.GetAsync(
                new Uri($"https://localhost:{port}/"),
                cancellationToken
            );
            return response.StatusCode;
        }
        finally
        {
            listener.Stop();
            try
            {
                await serving;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or SocketException
                            or ObjectDisposedException
                            or System.Security.Authentication.AuthenticationException
                            or OperationCanceledException
                )
            {
                // the client refused the certificate or the listener stopped
            }
        }
    }

    private static async ValueTask ServeOnceAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        CancellationToken cancellationToken
    )
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var ssl = new SslStream(tcp.GetStream());
        await ssl.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate },
            cancellationToken
        );

        var buffer = new byte[4096];
        _ = await ssl.ReadAsync(buffer, cancellationToken);
        var response = "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray();
        await ssl.WriteAsync(response, cancellationToken);
        await ssl.FlushAsync(cancellationToken);
    }
}
