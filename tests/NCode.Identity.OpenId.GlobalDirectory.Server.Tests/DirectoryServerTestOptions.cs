// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Moq;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Jwt;
using NCode.Identity.Jwt.Encoding;
using NCode.Identity.OpenId.Client;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides a complete <c>DirectoryServer</c> configuration for the tests that host the directory server.
/// </summary>
internal static class DirectoryServerTestOptions
{
    public const string DirectoryIssuer = "https://directory.example";

    public const string ClientJwks = """{"keys":[]}""";

    /// <summary>
    /// Creates the configuration of a valid <c>DirectoryServer</c> section, plus <c>AllowedHosts</c>.
    /// </summary>
    public static Dictionary<string, string?> Create(
        string controlPlaneCell,
        string controlPlaneTenantId,
        string controlPlaneIssuer
    ) =>
        new()
        {
            ["DirectoryServer:Issuer"] = DirectoryIssuer,
            ["DirectoryServer:KeyRing"] = "directory",
            ["DirectoryServer:ControlPlane:Cell"] = controlPlaneCell,
            ["DirectoryServer:ControlPlane:Region"] = "region-1",
            ["DirectoryServer:ControlPlane:TenantId"] = controlPlaneTenantId,
            ["DirectoryServer:ControlPlane:Issuer"] = controlPlaneIssuer,
            ["DirectoryServer:ControlPlane:ClientJwks"] = ClientJwks,
            ["AllowedHosts"] = "localhost",
        };

    /// <summary>
    /// Registers the libraries that the directory's own tokens need, and a key ring that holds one RSA key, which
    /// <see cref="CreateTokenAsync"/> signs with.
    /// </summary>
    public static IServiceBuilder<OpenIdClientLibrary> AddDirectoryTokenServices(
        IServiceCollection services
    )
    {
        var builder = services
            .AddSecretsLibrary()
            .AddJoseLibrary()
            .AddJwtLibrary()
            .AddOpenIdClientLibrary();
        services.AddSharedStateServices().AddInProcessExpiringKeySet();
        services.AddSingleton<DirectoryTestKey>();
        services.RemoveAll<IClientKeyRingProvider>();
        services.AddSingleton(serviceProvider =>
        {
            var key = serviceProvider.GetRequiredService<DirectoryTestKey>().Key;
            var collectionFactory =
                serviceProvider.GetRequiredService<ISecretKeyCollectionFactory>();
            var ring = collectionFactory.Create([key]);
            var provider = new Mock<IClientKeyRingProvider>();
            provider.Setup(x => x.GetKeyRing(It.IsAny<string>())).Returns(ring);
            return provider.Object;
        });
        return builder;
    }

    /// <summary>
    /// Signs an access token with the directory's key, as the directory's token issuer would, with the claims that the
    /// caller chooses.
    /// </summary>
    public static async ValueTask<string> CreateTokenAsync(
        IServiceProvider services,
        string clientId,
        string scope = GlobalDirectoryProtocol.DirectoryScope,
        IReadOnlyDictionary<string, object>? extraClaims = null,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default
    )
    {
        var algorithms = services.GetRequiredService<IAlgorithmCollectionProvider>().Collection;
        Assert.True(algorithms.TryGetSignatureAlgorithm("PS256", out var algorithm));

        var key = services.GetRequiredService<DirectoryTestKey>().Key;
        var claims = new Dictionary<string, object>
        {
            [JoseClaimNames.Payload.Sub] = clientId,
            [JoseClaimNames.Payload.Jti] = Guid.NewGuid().ToString("N"),
            ["client_id"] = clientId,
            ["scope"] = scope,
        };
        if (extraClaims is not null)
        {
            foreach (var (name, value) in extraClaims)
                claims[name] = value;
        }

        var now = DateTimeOffset.UtcNow;
        var parameters = new JwtEncodingParameters
        {
            TokenType = JoseTokenTypes.AccessTokenJwt,
            SigningCredentials = new JoseSigningCredentials(key, algorithm),
            EnsureTokenLifetime = false,
            Issuer = DirectoryIssuer,
            Audience = new StringValues(GlobalDirectoryProtocol.DirectoryAudience),
            IssuedAt = now.AddMinutes(-1),
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(5)),
            ExtraPayloadClaims = claims,
        };
        var encoder = services.GetRequiredService<IJwtEncoder>();
        return await encoder.EncodeAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// Holds the RSA key of the directory's key ring. Every host of a test run holds the same key material, so a token
    /// that one host's key signs is valid at another.
    /// </summary>
    public sealed class DirectoryTestKey : IDisposable
    {
        private static RSAParameters SharedParameters { get; } = CreateParameters();

        private static RSAParameters CreateParameters()
        {
            using var rsa = RSA.Create(2048);
            return rsa.ExportParameters(includePrivateParameters: true);
        }

        private RSA Rsa { get; } = RSA.Create(SharedParameters);

        public DirectoryTestKey(ISecretKeyFactory factory)
        {
            Key = factory.CreateRsa(new KeyMetadata { KeyId = "directory" }, Rsa);
        }

        public SecretKey Key { get; }

        public void Dispose() => Rsa.Dispose();
    }

    /// <summary>
    /// Reads every key set successfully, with no keys.
    /// </summary>
    public sealed class AcceptingPublicJwksReader : IPublicJwksReader
    {
        public bool TryRead(
            JsonElement jwks,
            [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
            [NotNullWhen(false)] out string? error
        )
        {
            keys = [];
            error = null;
            return true;
        }
    }
}
