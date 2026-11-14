// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using NCode.Buffers;
using NCode.Encoders;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Provides the default implementation of <see cref="IMasterKeyTransfer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The package is a compact JWE to the recipient key, <c>ECDH-ES+A256KW</c> with <c>A256GCM</c>, of a JSON document
/// that names the owner, carries the root key the owner brings when it has one that is not being removed, and lists
/// each master key with its identifier, version, status, dates and key material. A master key is unwrapped through the
/// same path that loads it for use, so a key that does not match its key-check value never leaves the source.
/// </para>
/// <para>
/// An import stores the root key of the owner first and then each master key, wrapped under the root key the package
/// brings or else under the nearest root key of the owner, in one unit of work. The identifier and the version of each
/// master key are kept, so a value that a master key protected stays readable. The material of every key lives in
/// sensitive memory and is zeroed after use; no key material is logged.
/// </para>
/// </remarks>
/// <param name="factory">The factory that unwraps and wraps master keys.</param>
/// <param name="nearestRootKeyResolver">The resolver that finds the root key that wraps the master keys of an owner.</param>
/// <param name="storeManagerFactory">The factory that opens the unit of work for the stores.</param>
/// <param name="joseSerializer">The serializer that encrypts and decrypts the package.</param>
/// <param name="algorithmCollectionProvider">The provider of the key management and encryption algorithms.</param>
// See ADR-1514.
internal sealed class DefaultMasterKeyTransfer(
    MasterKeyFactory factory,
    INearestOwnerRootKeyResolver nearestRootKeyResolver,
    IStoreManagerFactory storeManagerFactory,
    IJoseSerializer joseSerializer,
    IAlgorithmCollectionProvider algorithmCollectionProvider
) : IMasterKeyTransfer
{
    private const int PackageVersion = 1;

    private const string ActiveStatus = "active";

    private const string RetiredStatus = "retired";

    private MasterKeyFactory Factory { get; } = factory;

    private INearestOwnerRootKeyResolver NearestRootKeyResolver { get; } = nearestRootKeyResolver;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IJoseSerializer JoseSerializer { get; } = joseSerializer;

    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;

    /// <inheritdoc />
    public async ValueTask<string> ExportAsync(
        SecretOwner owner,
        SecretKey recipient,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var key = owner.Normalize();
        var encoder = JoseSerializer.CreateEncoder(CreateEncryptionOptions(recipient));

        IReadOnlyList<PersistedMasterKey> rows;
        PersistedOwnerRootKey? rootKey;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            rows = await storeManager.GetStore<IMasterKeyStore>().ListAsync(key, cancellationToken);
            rootKey = await storeManager
                .GetStore<IOwnerRootKeyStore>()
                .FindAsync(key, cancellationToken);
        }

        using var payload = BufferFactory.CreatePooledBufferWriter(isSensitive: true);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", PackageVersion);
            writer.WriteString("ownerType", key.OwnerType);
            writer.WriteString("ownerId", key.OwnerId);

            if (MasterKeyFactory.IsOwnerRooted(rootKey))
                WriteRootKey(writer, rootKey);

            writer.WriteStartArray("keys");
            foreach (var row in rows)
            {
                using var masterKey = await Factory.UnwrapAsync(row, cancellationToken);
                WriteMasterKey(writer, row, masterKey);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
        }

        var payloadLength = checked((int)payload.Length);
        using var lease = BufferFactory.Rent(
            payloadLength,
            isSensitive: true,
            out Memory<byte> contiguous
        );
        payload.AsReadOnlySequence.CopyTo(contiguous.Span);

        ReadOnlyMemory<byte> content = contiguous[..payloadLength];
        return await encoder.EncodeAsync(content, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<MasterKeyTransferResult> ImportAsync(
        SecretOwner owner,
        string package,
        SecretKey recipientPrivate,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(package);
        ArgumentNullException.ThrowIfNull(recipientPrivate);

        var key = owner.Normalize();

        using var decoded = BufferFactory.CreatePooledBufferWriter(isSensitive: true);
        var token = JoseSerializer.Parse(package);
        await JoseSerializer.DecodeAsync(token, recipientPrivate, decoded, cancellationToken);

        using var imported = ReadPackage(decoded.AsReadOnlySequence, key);
        if (!SecretOwnerComparer.Instance.Equals(imported.Owner.Normalize(), key))
        {
            throw new InvalidOperationException(
                $"The master key transfer package is for another owner than '{key}'."
            );
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var rootKeyStore = storeManager.GetStore<IOwnerRootKeyStore>();
        var masterKeyStore = storeManager.GetStore<IMasterKeyStore>();

        PersistedOwnerRootKey? target;
        if (imported.RootKey is { } importedRootKey)
        {
            await rootKeyStore.AddAsync(importedRootKey, cancellationToken);
            target = importedRootKey;
        }
        else
        {
            target = await NearestRootKeyResolver.FindNearestAsync(
                rootKeyStore,
                key,
                cancellationToken
            );
        }

        foreach (var importedKey in imported.Keys)
        {
            var wraps = await Factory.WrapAsync(importedKey.MasterKey, target, cancellationToken);
            await masterKeyStore.AddAsync(CreateRow(importedKey, key, wraps), cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        return new MasterKeyTransferResult(imported.Keys.Count);
    }

    private JoseEncryptionOptions CreateEncryptionOptions(SecretKey recipient)
    {
        var collection = AlgorithmCollectionProvider.Collection;

        if (
            !collection.TryGetKeyManagementAlgorithm(
                AlgorithmCodes.KeyManagement.EcdhEsAes256,
                out var keyManagement
            )
        )
        {
            throw new InvalidOperationException(
                $"The key management algorithm '{AlgorithmCodes.KeyManagement.EcdhEsAes256}' is not registered."
            );
        }

        if (
            !collection.TryGetAuthenticatedEncryptionAlgorithm(
                AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm,
                out var encryption
            )
        )
        {
            throw new InvalidOperationException(
                $"The authenticated encryption algorithm '{AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm}' is not registered."
            );
        }

        return new JoseEncryptionOptions(encryption)
        {
            Recipients =
            [
                new JoseRecipientOptions(
                    new JoseKeyManagementCredentials(recipient, keyManagement)
                ),
            ],
        };
    }

    private static void WriteRootKey(Utf8JsonWriter writer, PersistedOwnerRootKey rootKey)
    {
        writer.WriteStartObject("rootKey");
        writer.WriteString("keyWrapperName", rootKey.KeyWrapperName);
        writer.WriteString("keyReference", rootKey.KeyReference);
        if (rootKey.AccessIdentity is not null)
            writer.WriteString("accessIdentity", rootKey.AccessIdentity);
        writer.WriteString("state", rootKey.State);
        writer.WriteString("createdWhen", rootKey.CreatedWhen);
        writer.WriteString("stateChangedWhen", rootKey.StateChangedWhen);
        if (rootKey.ValidatedWhen is { } validatedWhen)
            writer.WriteString("validatedWhen", validatedWhen);
        writer.WriteEndObject();
    }

    private static void WriteMasterKey(
        Utf8JsonWriter writer,
        PersistedMasterKey row,
        MasterKey masterKey
    )
    {
        writer.WriteStartObject();
        writer.WriteString("keyId", row.KeyId);
        writer.WriteNumber("version", row.Version);
        writer.WriteString("status", row.RetiredWhen is null ? ActiveStatus : RetiredStatus);
        writer.WriteString("createdWhen", row.CreatedWhen);
        if (row.RetiredWhen is { } retiredWhen)
            writer.WriteString("retiredWhen", retiredWhen);
        writer.WriteBase64String("key", masterKey.Material);
        writer.WriteEndObject();
    }

    // Builds the row that persists a master key of the package under the wraps of the root of its owner.
    private static PersistedMasterKey CreateRow(
        ImportedKey importedKey,
        SecretOwner owner,
        MasterKeyWraps wraps
    ) =>
        new()
        {
            KeyId = importedKey.MasterKey.KeyId,
            OwnerType = owner.OwnerType,
            OwnerId = owner.OwnerId,
            Version = importedKey.MasterKey.Version,
            CreatedWhen = importedKey.CreatedWhen,
            RetiredWhen = importedKey.RetiredWhen,
            RootKeyProvider = wraps.Root.WrapperName,
            RootKeyId = wraps.Root.KeyId,
            RootKeyReference = wraps.RootKeyReference,
            RootKeyAccessIdentity = wraps.RootKeyAccessIdentity,
            RootWrappedKey = wraps.Root.Ciphertext,
            RecoveryKeyId = wraps.Recovery?.KeyId,
            RecoveryWrappedKey = wraps.Recovery?.Ciphertext,
            KeyCheck = CreateKeyCheck(importedKey.MasterKey),
        };

    // Computes the key-check value of the master key as the string that the row persists.
    private static string CreateKeyCheck(MasterKey masterKey)
    {
        Span<byte> keyCheck = stackalloc byte[SecretProtectionFormat.KeyCheckSizeBytes];
        SecretProtectionFormat.ComputeKeyCheck(
            masterKey.Material,
            masterKey.KeyId,
            masterKey.Owner,
            masterKey.Version,
            keyCheck
        );

        return Base64Url.Encode(keyCheck);
    }

    // Reads the decrypted package. Every master key goes straight into sensitive memory, and the temporary array that
    // holds its decoded material is zeroed before the next key is read.
    private static ImportedPackage ReadPackage(ReadOnlySequence<byte> payload, SecretOwner owner)
    {
        var package = new ImportedPackage();
        try
        {
            var reader = new Utf8JsonReader(payload);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw Malformed();

            int? version = null;
            string? ownerType = null;
            string? ownerId = null;
            var hasKeys = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("version"u8))
                {
                    reader.Read();
                    version = reader.GetInt32();
                }
                else if (reader.ValueTextEquals("ownerType"u8))
                {
                    reader.Read();
                    ownerType = reader.GetString();
                }
                else if (reader.ValueTextEquals("ownerId"u8))
                {
                    reader.Read();
                    ownerId = reader.GetString();
                }
                else if (reader.ValueTextEquals("rootKey"u8))
                {
                    reader.Read();
                    package.RootKey = ReadRootKey(ref reader, owner);
                }
                else if (reader.ValueTextEquals("keys"u8))
                {
                    reader.Read();
                    ReadKeys(ref reader, owner, package.Keys);
                    hasKeys = true;
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (
                reader.TokenType != JsonTokenType.EndObject
                || version != PackageVersion
                || ownerType is null
                || ownerId is null
                || !hasKeys
            )
            {
                throw Malformed();
            }

            package.Owner = new SecretOwner(ownerType, ownerId);
            return package;
        }
        catch (Exception exception)
            when (exception is JsonException or FormatException or InvalidOperationException)
        {
            package.Dispose();
            throw Malformed();
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private static PersistedOwnerRootKey ReadRootKey(ref Utf8JsonReader reader, SecretOwner owner)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw Malformed();

        string? keyWrapperName = null;
        string? keyReference = null;
        string? accessIdentity = null;
        string? state = null;
        DateTimeOffset? createdWhen = null;
        DateTimeOffset? stateChangedWhen = null;
        DateTimeOffset? validatedWhen = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("keyWrapperName"u8))
            {
                reader.Read();
                keyWrapperName = reader.GetString();
            }
            else if (reader.ValueTextEquals("keyReference"u8))
            {
                reader.Read();
                keyReference = reader.GetString();
            }
            else if (reader.ValueTextEquals("accessIdentity"u8))
            {
                reader.Read();
                accessIdentity = reader.GetString();
            }
            else if (reader.ValueTextEquals("state"u8))
            {
                reader.Read();
                state = reader.GetString();
            }
            else if (reader.ValueTextEquals("createdWhen"u8))
            {
                reader.Read();
                createdWhen = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals("stateChangedWhen"u8))
            {
                reader.Read();
                stateChangedWhen = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals("validatedWhen"u8))
            {
                reader.Read();
                validatedWhen = reader.GetDateTimeOffset();
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        if (
            reader.TokenType != JsonTokenType.EndObject
            || keyWrapperName is null
            || keyReference is null
            || state is null
            || createdWhen is not { } created
            || stateChangedWhen is not { } stateChanged
        )
        {
            throw Malformed();
        }

        return new PersistedOwnerRootKey
        {
            OwnerType = owner.OwnerType,
            OwnerId = owner.OwnerId,
            KeyWrapperName = keyWrapperName,
            KeyReference = keyReference,
            AccessIdentity = accessIdentity,
            State = state,
            CreatedWhen = created,
            StateChangedWhen = stateChanged,
            ValidatedWhen = validatedWhen,
        };
    }

    private static void ReadKeys(
        ref Utf8JsonReader reader,
        SecretOwner owner,
        List<ImportedKey> keys
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw Malformed();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            keys.Add(ReadKey(ref reader, owner));
        }
    }

    private static ImportedKey ReadKey(ref Utf8JsonReader reader, SecretOwner owner)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw Malformed();

        Guid? keyId = null;
        int? version = null;
        string? status = null;
        DateTimeOffset? createdWhen = null;
        DateTimeOffset? retiredWhen = null;
        byte[]? material = null;
        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("keyId"u8))
                {
                    reader.Read();
                    keyId = reader.GetGuid();
                }
                else if (reader.ValueTextEquals("version"u8))
                {
                    reader.Read();
                    version = reader.GetInt32();
                }
                else if (reader.ValueTextEquals("status"u8))
                {
                    reader.Read();
                    status = reader.GetString();
                }
                else if (reader.ValueTextEquals("createdWhen"u8))
                {
                    reader.Read();
                    createdWhen = reader.GetDateTimeOffset();
                }
                else if (reader.ValueTextEquals("retiredWhen"u8))
                {
                    reader.Read();
                    retiredWhen = reader.GetDateTimeOffset();
                }
                else if (reader.ValueTextEquals("key"u8))
                {
                    reader.Read();
                    material = reader.GetBytesFromBase64();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (
                reader.TokenType != JsonTokenType.EndObject
                || keyId is not { } id
                || version is not { } keyVersion
                || createdWhen is not { } created
                || material is null
                || material.Length != SecretProtectionFormat.MasterKeySizeBytes
            )
            {
                throw Malformed();
            }

            DateTimeOffset? retired;
            if (string.Equals(status, ActiveStatus, StringComparison.Ordinal))
            {
                retired = null;
            }
            else if (
                string.Equals(status, RetiredStatus, StringComparison.Ordinal)
                && retiredWhen is not null
            )
            {
                retired = retiredWhen;
            }
            else
            {
                throw Malformed();
            }

            return new ImportedKey(
                created,
                retired,
                MasterKey.Create(id, owner, keyVersion, material)
            );
        }
        finally
        {
            if (material is not null)
                CryptographicOperations.ZeroMemory(material);
        }
    }

    private static InvalidOperationException Malformed() =>
        new("The master key transfer package is malformed.");

    // A master key of the package, held in sensitive memory until it is wrapped under the root of its owner.
    private sealed record ImportedKey(
        DateTimeOffset CreatedWhen,
        DateTimeOffset? RetiredWhen,
        MasterKey MasterKey
    );

    // The content of the package, which owns the master keys that it holds.
    private sealed class ImportedPackage : IDisposable
    {
        public SecretOwner Owner { get; set; }

        public PersistedOwnerRootKey? RootKey { get; set; }

        public List<ImportedKey> Keys { get; } = [];

        public void Dispose()
        {
            foreach (var key in Keys)
            {
                key.MasterKey.Dispose();
            }

            Keys.Clear();
        }
    }
}
