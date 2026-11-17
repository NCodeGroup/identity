// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyTypes;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.Management.Transfers;

// See ADR-1514.
public sealed class DefaultTenantTransferKeyServiceTests : IAsyncLifetime
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private FakeTimeProvider Time { get; } = new(Start);
    private MockRepository MockRepository { get; }
    private Mock<IExpiringValueSet> MockExpiringValueSet { get; }
    private ServiceProvider ServiceProvider { get; }
    private DefaultTenantTransferKeyService Service { get; }

    public DefaultTenantTransferKeyServiceTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockExpiringValueSet = MockRepository.Create<IExpiringValueSet>();

        // The real key factory and key-type catalog make and export the keys; only the value set is mocked.
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary();
        ServiceProvider = serviceCollection.BuildServiceProvider();

        Service = new DefaultTenantTransferKeyService(
            ServiceProvider.GetRequiredService<ISecretKeyFactory>(),
            ServiceProvider.GetRequiredService<ISecretKeyTypeCatalog>(),
            new EphemeralDataProtectionProvider(),
            MockExpiringValueSet.Object,
            Time
        );
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        MockRepository.Verify();
        await ServiceProvider.DisposeAsync();
    }

    #region Helpers

    private sealed class StoredValue
    {
        public string? Key { get; set; }
        public byte[]? Bytes { get; set; }
        public DateTimeOffset ExpiresWhen { get; set; }
    }

    private StoredValue SetupAdd()
    {
        var stored = new StoredValue();
        MockExpiringValueSet
            .Setup(x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (
                    string key,
                    ReadOnlyMemory<byte> value,
                    DateTimeOffset expiresWhen,
                    CancellationToken _
                ) =>
                {
                    stored.Key = key;
                    stored.Bytes = value.ToArray();
                    stored.ExpiresWhen = expiresWhen;
                }
            )
            .ReturnsAsync(true)
            .Verifiable();
        return stored;
    }

    // The set hands out the stored value under its key.
    private void SetupTake(StoredValue stored)
    {
        MockExpiringValueSet
            .Setup(x => x.TryTakeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (string key, CancellationToken _) =>
                    string.Equals(key, stored.Key, StringComparison.Ordinal)
                        ? new ExpiringValue(stored.Bytes, Version: 1)
                        : (ExpiringValue?)null
            )
            .Verifiable();
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_Always_ReturnsThePublicP256KeyUnderARandomKeyId()
    {
        SetupAdd();

        var transferKey = await Service.CreateAsync(CancellationToken.None);

        Assert.Equal(16, Base64Url.DecodeFromChars(transferKey.KeyId).Length);
        var jwk = transferKey.Jwk;
        Assert.Equal("EC", jwk.GetProperty("kty").GetString());
        Assert.Equal("P-256", jwk.GetProperty("crv").GetString());
        Assert.Equal(transferKey.KeyId, jwk.GetProperty("kid").GetString());
        Assert.Equal("enc", jwk.GetProperty("use").GetString());
        Assert.Equal("ECDH-ES+A256KW", jwk.GetProperty("alg").GetString());
        Assert.False(jwk.TryGetProperty("d", out _));
    }

    [Fact]
    public async Task CreateAsync_Always_StoresTheProtectedKeyForOneHourUnderItsKeyId()
    {
        var stored = SetupAdd();

        var transferKey = await Service.CreateAsync(CancellationToken.None);

        Assert.Equal("tenant-transfer-key:" + transferKey.KeyId, stored.Key);
        Assert.Equal(Start + TimeSpan.FromHours(1), stored.ExpiresWhen);

        // The stored bytes are protected, so they are no PKCS#8 private key.
        Assert.NotNull(stored.Bytes);
        using var ecdh = ECDiffieHellman.Create();
        Assert.ThrowsAny<CryptographicException>(() =>
            ecdh.ImportPkcs8PrivateKey(stored.Bytes, out _)
        );
    }

    [Fact]
    public async Task CreateAsync_Twice_ReturnsDifferentKeyIds()
    {
        SetupAdd();

        var first = await Service.CreateAsync(CancellationToken.None);
        var second = await Service.CreateAsync(CancellationToken.None);

        Assert.NotEqual(first.KeyId, second.KeyId);
    }

    [Fact]
    public async Task CreateAsync_WhenTheKeyIdIsAlreadyStored_Throws()
    {
        MockExpiringValueSet
            .Setup(x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service.CreateAsync(CancellationToken.None)
        );
    }

    #endregion

    #region TakeAsync Tests

    [Fact]
    public async Task TakeAsync_AfterCreate_ReturnsThePrivateKeyOfTheIssuedPublicKey()
    {
        var stored = SetupAdd();
        SetupTake(stored);
        var transferKey = await Service.CreateAsync(CancellationToken.None);

        var secretKey = await Service.TakeAsync(transferKey.KeyId, CancellationToken.None);

        var eccKey = Assert.IsAssignableFrom<EccSecretKey>(secretKey);
        Assert.Equal(transferKey.KeyId, eccKey.KeyId);
        Assert.Equal("enc", eccKey.Metadata.Use);
        Assert.Equal("ECDH-ES+A256KW", eccKey.Metadata.Algorithm);
        using var ecdh = eccKey.ExportECDiffieHellman();
        var parameters = ecdh.ExportParameters(includePrivateParameters: true);
        Assert.NotNull(parameters.D);
        Assert.Equal(
            transferKey.Jwk.GetProperty("x").GetString(),
            Base64Url.EncodeToString(parameters.Q.X!)
        );
        Assert.Equal(
            transferKey.Jwk.GetProperty("y").GetString(),
            Base64Url.EncodeToString(parameters.Q.Y!)
        );
    }

    [Fact]
    public async Task TakeAsync_WhenTheKeyIdIsUnknown_ReturnsNone()
    {
        MockExpiringValueSet
            .Setup(x =>
                x.TryTakeAsync("tenant-transfer-key:unknown", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();

        var secretKey = await Service.TakeAsync("unknown", CancellationToken.None);

        Assert.Null(secretKey);
    }

    [Fact]
    public async Task TakeAsync_WhenTheStoredValueIsNotProtectedByTheService_ReturnsNone()
    {
        MockExpiringValueSet
            .Setup(x => x.TryTakeAsync("tenant-transfer-key:kid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiringValue(new byte[] { 1, 2, 3, 4 }, Version: 1))
            .Verifiable();

        var secretKey = await Service.TakeAsync("kid-1", CancellationToken.None);

        Assert.Null(secretKey);
    }

    [Fact]
    public async Task TakeAsync_WhenTheKeyIdIsEmpty_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Service.TakeAsync(string.Empty, CancellationToken.None)
        );
    }

    #endregion
}
