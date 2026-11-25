// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyTypes;
using NCode.Identity.Secrets.Persistence.Verifiers;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.Secrets.Persistence;

public class DefaultPublicJwksReaderTests
{
    private DefaultPublicJwksReader Reader { get; }

    public DefaultPublicJwksReaderTests()
    {
        var provider = new EphemeralDataProtectionProvider();
        var secretKeyFactory = new DefaultSecretKeyFactory(
            new TestDataProtectorFactory<SecretKey>(provider.CreateProtector("factory")),
            new DefaultEccCurveCatalog([])
        );
        var catalog = new DefaultSecretKeyTypeCatalog([
            new SymmetricSecretKeyTypeDescriptor(secretKeyFactory),
            new RsaSecretKeyTypeDescriptor(secretKeyFactory),
            new EccSecretKeyTypeDescriptor(secretKeyFactory, new DefaultEccCurveCatalog([])),
        ]);
        var serializer = new DefaultSecretSerializer(
            catalog,
            TestSecretVerifierSchemes.CreateCatalog(),
            new DefaultRemoteKeyResolver(
                catalog,
                new DefaultRemoteKeyProviderCatalog([], new DefaultRemoteKeyDescriptorCatalog([])),
                new DefaultRemoteKeyDescriptorCatalog([])
            )
        );

        Reader = new DefaultPublicJwksReader(
            catalog,
            new DefaultSecretImporter(catalog),
            serializer,
            TimeProvider.System
        );
    }

    private static string EcJwk(string kid, bool includePrivate = false)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivate);
        var d = includePrivate ? $",\"d\":\"{Base64Url(parameters.D!)}\"" : string.Empty;
        return $"{{\"kty\":\"EC\",\"crv\":\"P-256\",\"kid\":\"{kid}\",\"use\":\"sig\",\"alg\":\"ES256\","
            + $"\"x\":\"{Base64Url(parameters.Q.X!)}\",\"y\":\"{Base64Url(parameters.Q.Y!)}\"{d}}}";
    }

    private static string RsaJwk(string kid)
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        return $"{{\"kty\":\"RSA\",\"kid\":\"{kid}\",\"use\":\"sig\",\"alg\":\"RS256\","
            + $"\"n\":\"{Base64Url(parameters.Modulus!)}\",\"e\":\"{Base64Url(parameters.Exponent!)}\"}}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonElement Set(params string[] keys) =>
        JsonElement.Parse($"{{\"keys\":[{string.Join(",", keys)}]}}");

    [Fact]
    public void TryRead_WhenEcAndRsaKeys_ReturnsKeysInOrder()
    {
        var result = Reader.TryRead(
            Set(EcJwk("ec-1"), RsaJwk("rsa-1")),
            out var keys,
            out var error
        );

        Assert.True(result, error);
        Assert.NotNull(keys);
        Assert.Collection(
            keys,
            key => Assert.Equal("ec-1", key.KeyId),
            key => Assert.Equal("rsa-1", key.KeyId)
        );
        Assert.IsAssignableFrom<EccSecretKey>(keys[0]);
        Assert.IsAssignableFrom<RsaSecretKey>(keys[1]);
    }

    [Fact]
    public void TryRead_WhenKeyHasPrivateMember_ReturnsFalse()
    {
        var result = Reader.TryRead(
            Set(EcJwk("ec-1", includePrivate: true)),
            out var keys,
            out var error
        );

        Assert.False(result);
        Assert.Null(keys);
        Assert.Contains("'d'", error);
    }

    [Fact]
    public void TryRead_WhenKidIsDuplicated_ReturnsFalse()
    {
        var result = Reader.TryRead(
            Set(EcJwk("same"), RsaJwk("same")),
            out var keys,
            out var error
        );

        Assert.False(result);
        Assert.Null(keys);
        Assert.Contains("not unique", error);
    }

    [Fact]
    public void TryRead_WhenKeyHasNoKid_ReturnsFalse()
    {
        var withoutKid = EcJwk("x")
            .Replace("\"kid\":\"x\",", string.Empty, StringComparison.Ordinal);

        var result = Reader.TryRead(Set(withoutKid), out _, out var error);

        Assert.False(result);
        Assert.Contains("'kid'", error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"keys\":[]}")]
    [InlineData("{\"keys\":{}}")]
    [InlineData("[]")]
    public void TryRead_WhenSetHasNoKeys_ReturnsFalse(string json)
    {
        var result = Reader.TryRead(JsonElement.Parse(json), out var keys, out var error);

        Assert.False(result);
        Assert.Null(keys);
        Assert.NotNull(error);
    }
}
