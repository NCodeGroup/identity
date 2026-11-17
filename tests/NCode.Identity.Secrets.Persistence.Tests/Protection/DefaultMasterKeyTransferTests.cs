// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Collections.Frozen;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyWrapping;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Options;

namespace NCode.Identity.Secrets.Persistence.Protection;

public class DefaultMasterKeyTransferTests : BaseTests
{
    private const string PackageText = "transfer-package";

    private static SecretOwner Owner { get; } = SecretOwner.ForTenant("tenant-a").Normalize();

    private static SecretOwner OtherOwner { get; } = SecretOwner.ForTenant("tenant-b").Normalize();

    private static SecretProtectionContext Context { get; } =
        new(SecretProtectionPurposes.PersistedSecret, "Item-One");

    private FakeTimeProvider Time { get; } = new();
    private RecoveryKeyWrapper Recovery { get; } =
        new(Microsoft.Extensions.Options.Options.Create(new SecretProtectionOptions()));
    private List<IDisposable> Disposables { get; } = [];
    private List<string> Calls { get; } = [];

    private Mock<IJoseSerializer> Jose { get; }
    private Mock<IAlgorithmCollectionProvider> Algorithms { get; }
    private Mock<IAlgorithmCollection> Collection { get; }
    private Mock<KeyManagementAlgorithm> KeyManagement { get; }
    private Mock<AuthenticatedEncryptionAlgorithm> Encryption { get; }
    private Mock<SecretKey> PublicKey { get; }
    private Mock<SecretKey> PrivateKey { get; }
    private Mock<SecretKey> WrongKey { get; }
    private Mock<JoseToken> Token { get; }
    private Mock<ISecretOwnerParentResolver> Parents { get; }
    private Mock<IOwnerRootKeyManager> OwnerRootKeyManager { get; }
    private FakeJoseEncoder Encoder { get; } = new();

    public DefaultMasterKeyTransferTests()
    {
        Jose = CreateStrictMock<IJoseSerializer>();
        Algorithms = CreateStrictMock<IAlgorithmCollectionProvider>();
        Collection = CreateStrictMock<IAlgorithmCollection>();
        KeyManagement = CreateStrictMock<KeyManagementAlgorithm>();
        Encryption = CreateStrictMock<AuthenticatedEncryptionAlgorithm>();
        PublicKey = CreateStrictMock<SecretKey>();
        PrivateKey = CreateStrictMock<SecretKey>();
        WrongKey = CreateStrictMock<SecretKey>();
        Token = CreateStrictMock<JoseToken>();
        Parents = CreateStrictMock<ISecretOwnerParentResolver>();
        OwnerRootKeyManager = CreateStrictMock<IOwnerRootKeyManager>();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }

            Recovery.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Export and Import Tests

    [Fact]
    public async Task Import_ExportedDeploymentRootedKeys_StoresKeysThatReadEarlierValues()
    {
        var source = Track(new EphemeralKeyWrapper());
        var target = Track(new EphemeralKeyWrapper());

        var createCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        createCatalog.Setup(x => x.GetConfigured()).Returns(source).Verifiable();
        var active = await CreateKeyAsync(createCatalog, 2, null, CancellationToken.None);
        var retired = await CreateKeyAsync(createCatalog, 1, null, CancellationToken.None);
        retired.Row.RetiredWhen = Time.GetUtcNow();

        var activeValue = Protect(active.Key, [active.Key, retired.Key], ActivePlaintext);
        var retiredValue = Protect(retired.Key, [active.Key, retired.Key], RetiredPlaintext);

        var exportCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        exportCatalog.Setup(x => x.Get(KeyWrapperNames.Ephemeral)).Returns(source).Verifiable();
        var exportCell = CreateCell();
        SetupList(exportCell, active.Row, retired.Row);
        SetupFind(exportCell, rootKey: null);

        var package = await ExportAsync(exportCatalog, exportCell, CancellationToken.None);

        Assert.Equal(PackageText, package);
        var payload = SecureEncoding.UTF8.GetString(Encoder.Payload);
        Assert.DoesNotContain(active.Row.RootWrappedKey, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(retired.Row.RootWrappedKey, payload, StringComparison.Ordinal);

        var importCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        importCatalog.Setup(x => x.GetConfigured()).Returns(target).Verifiable();
        var importCell = CreateCell();
        SetupFind(importCell, rootKey: null);
        SetupNoAncestors();
        var stored = SetupAdd(importCell);
        SetupSave(importCell);
        SetupImport();

        var result = await CreateTransfer(importCatalog, importCell.Factory)
            .ImportAsync(Owner, package, PrivateKey.Object, CancellationToken.None);

        Assert.Equal(new MasterKeyTransferResult(2), result);
        Assert.Equal(2, stored.Count);
        foreach (var original in new[] { active.Row, retired.Row })
        {
            var row = Assert.Single(stored, x => x.KeyId == original.KeyId);
            Assert.Equal(Owner, row.Owner);
            Assert.Equal(original.Version, row.Version);
            Assert.Equal(original.CreatedWhen, row.CreatedWhen);
            Assert.Equal(original.RetiredWhen, row.RetiredWhen);
            Assert.Equal(original.KeyCheck, row.KeyCheck);
            Assert.NotEqual(original.RootWrappedKey, row.RootWrappedKey);
            Assert.Null(row.RootKeyReference);
            Assert.Null(row.RootKeyAccessIdentity);
        }

        var readCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        readCatalog.Setup(x => x.Get(KeyWrapperNames.Ephemeral)).Returns(target).Verifiable();
        var reader = await CreateReaderAsync(
            readCatalog,
            stored,
            active.Row.KeyId,
            CancellationToken.None
        );

        Assert.Equal(ActivePlaintext, Unprotect(reader, activeValue));
        Assert.Equal(RetiredPlaintext, Unprotect(reader, retiredValue));
    }

    [Fact]
    public async Task Import_ExportedKeysOfAnOwnerWithItsOwnRootKey_StoresTheRootKeyFirstAndWrapsUnderIt()
    {
        var sourceReferenced = new FakeReferencedKeyWrapper();
        var targetReferenced = new FakeReferencedKeyWrapper();
        var rootKey = CreateRootKey(sourceReferenced.Name);

        var createCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        createCatalog
            .Setup(x => x.GetReferenced(sourceReferenced.Name))
            .Returns(sourceReferenced)
            .Verifiable();
        var key = await CreateKeyAsync(createCatalog, 1, rootKey, CancellationToken.None);
        var protectedValue = Protect(key.Key, [key.Key], ActivePlaintext);

        var exportCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        exportCatalog
            .Setup(x => x.GetReferenced(sourceReferenced.Name))
            .Returns(sourceReferenced)
            .Verifiable();
        var exportCell = CreateCell();
        SetupList(exportCell, key.Row);
        SetupFind(exportCell, rootKey);

        var package = await ExportAsync(exportCatalog, exportCell, CancellationToken.None);

        var importCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        importCatalog
            .Setup(x => x.GetReferenced(sourceReferenced.Name))
            .Returns(targetReferenced)
            .Verifiable();
        var importCell = CreateCell();
        var storedRootKeys = new List<PersistedOwnerRootKey>();
        importCell
            .RootKeys.Setup(x =>
                x.AddAsync(It.IsAny<PersistedOwnerRootKey>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (PersistedOwnerRootKey item, CancellationToken _) =>
                {
                    Calls.Add("root");
                    storedRootKeys.Add(item);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var stored = SetupAdd(importCell);
        SetupSave(importCell);
        SetupImport();
        SetupRootKeyAccepted(rootKey);

        var result = await CreateTransfer(importCatalog, importCell.Factory)
            .ImportAsync(Owner, package, PrivateKey.Object, CancellationToken.None);

        Assert.Equal(new MasterKeyTransferResult(1), result);
        Assert.Equal(["root", "key"], Calls);

        var importedRootKey = Assert.Single(storedRootKeys);
        Assert.Equal(Owner, importedRootKey.Owner);
        Assert.Equal(rootKey.KeyWrapperName, importedRootKey.KeyWrapperName);
        Assert.Equal(rootKey.KeyReference, importedRootKey.KeyReference);
        Assert.Equal(rootKey.AccessIdentity, importedRootKey.AccessIdentity);
        Assert.Equal(rootKey.State, importedRootKey.State);
        Assert.Equal(rootKey.CreatedWhen, importedRootKey.CreatedWhen);
        Assert.Equal(rootKey.StateChangedWhen, importedRootKey.StateChangedWhen);
        Assert.Equal(rootKey.ValidatedWhen, importedRootKey.ValidatedWhen);
        Assert.Null(importedRootKey.FailedWhen);
        Assert.Null(importedRootKey.Failure);

        var row = Assert.Single(stored);
        Assert.Equal(key.Row.KeyId, row.KeyId);
        Assert.Equal(rootKey.KeyWrapperName, row.RootKeyProvider);
        Assert.Equal(rootKey.KeyReference, row.RootKeyReference);
        Assert.Equal(rootKey.AccessIdentity, row.RootKeyAccessIdentity);
        Assert.Null(row.RecoveryKeyId);
        Assert.Null(row.RecoveryWrappedKey);
        Assert.Equal(1, sourceReferenced.WrapCount);
        Assert.Equal(1, targetReferenced.WrapCount);

        var readCatalog = CreateStrictMock<IKeyWrapperCatalog>();
        readCatalog
            .Setup(x => x.GetReferenced(sourceReferenced.Name))
            .Returns(targetReferenced)
            .Verifiable();
        var reader = await CreateReaderAsync(
            readCatalog,
            stored,
            key.Row.KeyId,
            CancellationToken.None
        );

        Assert.Equal(ActivePlaintext, Unprotect(reader, protectedValue));
    }

    [Fact]
    public async Task Import_ExportedOwnerWithoutKeys_StoresNothingAndReturnsZero()
    {
        var package = await ExportEmptyAsync(CancellationToken.None);

        var importCell = CreateCell();
        SetupFind(importCell, rootKey: null);
        SetupNoAncestors();
        SetupSave(importCell);
        SetupImport();

        var result = await CreateTransfer(
                CreateStrictMock<IKeyWrapperCatalog>(),
                importCell.Factory
            )
            .ImportAsync(Owner, package, PrivateKey.Object, CancellationToken.None);

        Assert.Equal(new MasterKeyTransferResult(0), result);
    }

    [Fact]
    public async Task Import_ARootKeyWhoseReferenceTheCellRefuses_ReturnsTheRefusalBeforeOpeningAUnitOfWork()
    {
        const string refusal =
            "The key reference must be the ARN of a KMS key or alias in one of the allowed AWS regions: us-west-2.";
        var (package, rootKey) = await ExportRootedAsync(CancellationToken.None);
        SetupImport();

        string? reason = null;
        string? failure = refusal;
        OwnerRootKeyManager
            .Setup(x => x.CanBringRootKey(Owner, out reason))
            .Returns(true)
            .Verifiable();
        OwnerRootKeyManager
            .Setup(x =>
                x.TryValidateKeyReference(rootKey.KeyWrapperName, rootKey.KeyReference, out failure)
            )
            .Returns(false)
            .Verifiable();

        var transfer = CreateTransfer(
            CreateStrictMock<IKeyWrapperCatalog>(),
            CreateStrictMock<IStoreManagerFactory>()
        );

        var result = await transfer.ImportAsync(
            Owner,
            package,
            PrivateKey.Object,
            CancellationToken.None
        );

        Assert.False(result.IsImported);
        Assert.Equal(0, result.Count);
        Assert.Equal(refusal, result.RefusalDetail);
    }

    [Fact]
    public async Task Import_ARootKeyOfAnOwnerAPolicyRefuses_ReturnsTheRefusalBeforeOpeningAUnitOfWork()
    {
        const string refusal = "The owner cannot bring a root key.";
        var (package, _) = await ExportRootedAsync(CancellationToken.None);
        SetupImport();

        string? reason = refusal;
        OwnerRootKeyManager
            .Setup(x => x.CanBringRootKey(Owner, out reason))
            .Returns(false)
            .Verifiable();

        var transfer = CreateTransfer(
            CreateStrictMock<IKeyWrapperCatalog>(),
            CreateStrictMock<IStoreManagerFactory>()
        );

        var result = await transfer.ImportAsync(
            Owner,
            package,
            PrivateKey.Object,
            CancellationToken.None
        );

        Assert.False(result.IsImported);
        Assert.Equal(0, result.Count);
        Assert.Equal(refusal, result.RefusalDetail);
    }

    [Fact]
    public async Task Import_WithAnotherRecipientKey_ThrowsBeforeOpeningAUnitOfWork()
    {
        var package = await ExportEmptyAsync(CancellationToken.None);
        SetupImport();

        var transfer = CreateTransfer(
            CreateStrictMock<IKeyWrapperCatalog>(),
            CreateStrictMock<IStoreManagerFactory>()
        );

        await Assert.ThrowsAsync<CryptographicException>(() =>
            transfer.ImportAsync(Owner, package, WrongKey.Object, CancellationToken.None).AsTask()
        );
    }

    [Fact]
    public async Task Import_APackageOfAnotherOwner_ThrowsBeforeOpeningAUnitOfWork()
    {
        var package = await ExportEmptyAsync(CancellationToken.None);
        SetupImport();

        var transfer = CreateTransfer(
            CreateStrictMock<IKeyWrapperCatalog>(),
            CreateStrictMock<IStoreManagerFactory>()
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transfer
                .ImportAsync(OtherOwner, package, PrivateKey.Object, CancellationToken.None)
                .AsTask()
        );
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"ownerType\":\"tenant\"")]
    [InlineData("{\"version\":2,\"ownerType\":\"tenant\",\"ownerId\":\"tenant-a\",\"keys\":[]}")]
    [InlineData(
        "{\"version\":1,\"ownerType\":\"tenant\",\"ownerId\":\"tenant-a\",\"keys\":[{\"keyId\":\"not-a-guid\"}]}"
    )]
    public async Task Import_AMalformedPackage_Throws(string payload)
    {
        Encoder.Payload = SecureEncoding.UTF8.GetBytes(payload);
        SetupImport();

        var transfer = CreateTransfer(
            CreateStrictMock<IKeyWrapperCatalog>(),
            CreateStrictMock<IStoreManagerFactory>()
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transfer
                .ImportAsync(Owner, PackageText, PrivateKey.Object, CancellationToken.None)
                .AsTask()
        );
    }

    #endregion

    private static byte[] ActivePlaintext { get; } = RandomNumberGenerator.GetBytes(64);

    private static byte[] RetiredPlaintext { get; } = RandomNumberGenerator.GetBytes(48);

    private sealed record Cell(
        Mock<IStoreManagerFactory> Factory,
        Mock<IStoreManager> Manager,
        Mock<IMasterKeyStore> MasterKeys,
        Mock<IOwnerRootKeyStore> RootKeys
    );

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        Disposables.Add(disposable);
        return disposable;
    }

    private DefaultMasterKeyTransfer CreateTransfer(
        Mock<IKeyWrapperCatalog> catalog,
        Mock<IStoreManagerFactory> storeManagerFactory
    ) =>
        new(
            new MasterKeyFactory(catalog.Object, Recovery, Time),
            new DefaultNearestOwnerRootKeyResolver(Parents.Object),
            OwnerRootKeyManager.Object,
            storeManagerFactory.Object,
            Jose.Object,
            Algorithms.Object
        );

    private Cell CreateCell()
    {
        var masterKeys = CreateStrictMock<IMasterKeyStore>();
        var rootKeys = CreateStrictMock<IOwnerRootKeyStore>();
        var manager = CreateStrictMock<IStoreManager>();
        manager.Setup(x => x.GetStore<IMasterKeyStore>()).Returns(masterKeys.Object).Verifiable();
        manager.Setup(x => x.GetStore<IOwnerRootKeyStore>()).Returns(rootKeys.Object).Verifiable();
        manager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        var factory = CreateStrictMock<IStoreManagerFactory>();
        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IStoreManager>(manager.Object))
            .Verifiable();

        return new Cell(factory, manager, masterKeys, rootKeys);
    }

    private static void SetupList(Cell cell, params PersistedMasterKey[] rows) =>
        cell
            .MasterKeys.Setup(x => x.ListAsync(Owner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<PersistedMasterKey>>(rows))
            .Verifiable();

    private static void SetupFind(Cell cell, PersistedOwnerRootKey? rootKey) =>
        cell
            .RootKeys.Setup(x => x.FindAsync(Owner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PersistedOwnerRootKey?>(rootKey))
            .Verifiable();

    private List<PersistedMasterKey> SetupAdd(Cell cell)
    {
        var stored = new List<PersistedMasterKey>();
        cell.MasterKeys.Setup(x =>
                x.AddAsync(It.IsAny<PersistedMasterKey>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (PersistedMasterKey item, CancellationToken _) =>
                {
                    Calls.Add("key");
                    stored.Add(item);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        return stored;
    }

    private static void SetupSave(Cell cell) =>
        cell
            .Manager.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupNoAncestors() =>
        Parents
            .Setup(x => x.GetAncestorsAsync(Owner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<SecretOwner>>(Array.Empty<SecretOwner>()))
            .Verifiable();

    // The serializer encrypts to the public recipient key with the algorithms the transfer names, and hands the
    // encoder that captures the payload.
    private void SetupExport()
    {
        KeyManagementAlgorithm? keyManagement = KeyManagement.Object;
        AuthenticatedEncryptionAlgorithm? encryption = Encryption.Object;

        Algorithms.Setup(x => x.Collection).Returns(Collection.Object).Verifiable();
        Collection
            .Setup(x =>
                x.TryGetKeyManagementAlgorithm(
                    AlgorithmCodes.KeyManagement.EcdhEsAes256,
                    out keyManagement
                )
            )
            .Returns(true)
            .Verifiable();
        Collection
            .Setup(x =>
                x.TryGetAuthenticatedEncryptionAlgorithm(
                    AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm,
                    out encryption
                )
            )
            .Returns(true)
            .Verifiable();

        Jose.Setup(x =>
                x.CreateEncoder(
                    It.Is<JoseEncryptionOptions>(options =>
                        ReferenceEquals(options.EncryptionAlgorithm, Encryption.Object)
                        && options.Format == JoseSerializationFormat.Compact
                        && options.Recipients.Count == 1
                        && ReferenceEquals(
                            options.Recipients.Single().KeyManagementCredentials.SecretKey,
                            PublicKey.Object
                        )
                        && ReferenceEquals(
                            options
                                .Recipients.Single()
                                .KeyManagementCredentials.KeyManagementAlgorithm,
                            KeyManagement.Object
                        )
                    )
                )
            )
            .Returns(Encoder)
            .Verifiable();
    }

    // The serializer decrypts the package only for the private recipient key.
    private void SetupImport()
    {
        Jose.Setup(x => x.Parse(PackageText)).Returns(Token.Object).Verifiable();
        Jose.Setup(x =>
                x.DecodeAsync(
                    Token.Object,
                    It.IsAny<SecretKey>(),
                    It.IsAny<IBufferWriter<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (
                    JoseToken _,
                    SecretKey recipientKey,
                    IBufferWriter<byte> writer,
                    CancellationToken _
                ) =>
                {
                    if (!ReferenceEquals(recipientKey, PrivateKey.Object))
                        throw new CryptographicException("The package does not decrypt.");

                    writer.Write(Encoder.Payload);
                    return ValueTask.CompletedTask;
                }
            )
            .Verifiable();
    }

    private async ValueTask<string> ExportAsync(
        Mock<IKeyWrapperCatalog> catalog,
        Cell cell,
        CancellationToken cancellationToken
    )
    {
        SetupExport();
        return await CreateTransfer(catalog, cell.Factory)
            .ExportAsync(Owner, PublicKey.Object, cancellationToken);
    }

    private async ValueTask<string> ExportEmptyAsync(CancellationToken cancellationToken)
    {
        var cell = CreateCell();
        SetupList(cell);
        SetupFind(cell, rootKey: null);
        return await ExportAsync(CreateStrictMock<IKeyWrapperCatalog>(), cell, cancellationToken);
    }

    // Exports an owner with its own root key and one master key wrapped under it.
    private async ValueTask<(string Package, PersistedOwnerRootKey RootKey)> ExportRootedAsync(
        CancellationToken cancellationToken
    )
    {
        var wrapper = new FakeReferencedKeyWrapper();
        var rootKey = CreateRootKey(wrapper.Name);

        var catalog = CreateStrictMock<IKeyWrapperCatalog>();
        catalog.Setup(x => x.GetReferenced(wrapper.Name)).Returns(wrapper).Verifiable();
        var key = await CreateKeyAsync(catalog, 1, rootKey, cancellationToken);

        var cell = CreateCell();
        SetupList(cell, key.Row);
        SetupFind(cell, rootKey);

        var package = await ExportAsync(catalog, cell, cancellationToken);
        return (package, rootKey);
    }

    // The manager lets the owner bring the root key and accepts its key reference and its access identity.
    private void SetupRootKeyAccepted(PersistedOwnerRootKey rootKey)
    {
        string? reason = null;
        string? failure = null;
        OwnerRootKeyManager
            .Setup(x => x.CanBringRootKey(Owner, out reason))
            .Returns(true)
            .Verifiable();
        OwnerRootKeyManager
            .Setup(x =>
                x.TryValidateKeyReference(rootKey.KeyWrapperName, rootKey.KeyReference, out failure)
            )
            .Returns(true)
            .Verifiable();
        OwnerRootKeyManager
            .Setup(x =>
                x.TryValidateAccessIdentity(
                    rootKey.KeyWrapperName,
                    rootKey.AccessIdentity!,
                    out failure
                )
            )
            .Returns(true)
            .Verifiable();
    }

    private async ValueTask<CreatedMasterKey> CreateKeyAsync(
        Mock<IKeyWrapperCatalog> catalog,
        int version,
        PersistedOwnerRootKey? rootKey,
        CancellationToken cancellationToken
    )
    {
        var created = await new MasterKeyFactory(catalog.Object, Recovery, Time).CreateAsync(
            Owner,
            version,
            rootKey,
            cancellationToken
        );
        Track(created.Key);
        return created;
    }

    private PersistedOwnerRootKey CreateRootKey(string wrapperName) =>
        new()
        {
            OwnerType = Owner.OwnerType,
            OwnerId = Owner.OwnerId,
            KeyWrapperName = wrapperName,
            KeyReference = "kms://tenant-a/root",
            AccessIdentity = "test-identity-a",
            State = OwnerRootKeyStates.Active,
            CreatedWhen = Time.GetUtcNow(),
            StateChangedWhen = Time.GetUtcNow().AddMinutes(1),
            ValidatedWhen = Time.GetUtcNow().AddMinutes(2),
        };

    // Unwraps the stored rows into a protector whose active key is the one with the given identifier.
    private async ValueTask<DefaultSecretProtector> CreateReaderAsync(
        Mock<IKeyWrapperCatalog> catalog,
        IReadOnlyList<PersistedMasterKey> rows,
        Guid activeKeyId,
        CancellationToken cancellationToken
    )
    {
        var factory = new MasterKeyFactory(catalog.Object, Recovery, Time);
        var keys = new Dictionary<Guid, MasterKey>();
        foreach (var row in rows)
        {
            keys[row.KeyId] = Track(await factory.UnwrapAsync(row, cancellationToken));
        }

        return new DefaultSecretProtector(Owner, keys[activeKeyId], keys.ToFrozenDictionary());
    }

    private static string Protect(
        MasterKey activeKey,
        IReadOnlyList<MasterKey> keys,
        byte[] plaintext
    ) =>
        new DefaultSecretProtector(
            Owner,
            activeKey,
            keys.ToDictionary(x => x.KeyId).ToFrozenDictionary()
        ).ProtectToString(plaintext, Context);

    private static byte[] Unprotect(DefaultSecretProtector protector, string protectedValue)
    {
        var writer = new ArrayBufferWriter<byte>();
        protector.Unprotect(protectedValue, Context, ref writer);
        return writer.WrittenSpan.ToArray();
    }

    // An encoder that returns the package text and keeps the payload that it was asked to encrypt.
    private sealed class FakeJoseEncoder : JoseEncoder
    {
        public byte[] Payload { get; set; } = [];

        public override ValueTask<string> EncodeAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken
        )
        {
            Payload = payload.ToArray();
            return new ValueTask<string>(PackageText);
        }

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync<T>(
            T payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync<T>(
            IBufferWriter<char> tokenWriter,
            T payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync(
            string payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            string payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync(
            ReadOnlyMemory<char> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            ReadOnlyMemory<char> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}
