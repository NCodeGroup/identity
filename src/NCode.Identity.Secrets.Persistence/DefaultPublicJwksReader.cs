// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NCode.Identity.Jwk;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyTypes;

namespace NCode.Identity.Secrets.Persistence;

/// <summary>
/// Provides the default implementation of <see cref="IPublicJwksReader"/>. Each key's type is matched to its registered
/// <see cref="ISecretKeyTypeDescriptor"/> by its <c>kty</c>, imported as public material and deserialized as a
/// verification-only secret key.
/// </summary>
internal sealed class DefaultPublicJwksReader(
    ISecretKeyTypeCatalog secretKeyTypeCatalog,
    ISecretImporter secretImporter,
    ISecretSerializer secretSerializer,
    TimeProvider timeProvider
) : IPublicJwksReader
{
    private static string[] PrivateMembers { get; } = ["d", "p", "q", "dp", "dq", "qi", "k"];

    private ISecretKeyTypeCatalog SecretKeyTypeCatalog { get; } = secretKeyTypeCatalog;
    private ISecretImporter SecretImporter { get; } = secretImporter;
    private ISecretSerializer SecretSerializer { get; } = secretSerializer;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public bool TryRead(
        JsonElement jwks,
        [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
        [NotNullWhen(false)] out string? error
    )
    {
        keys = null;

        if (
            jwks.ValueKind != JsonValueKind.Object
            || !jwks.TryGetProperty(JsonWebKeySetParameterNames.Keys, out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0
        )
        {
            error = "The JSON Web Key Set must be a JSON object with a non-empty 'keys' array.";
            return false;
        }

        var createdWhen = TimeProvider.GetUtcNow();
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SecretKey>();
        foreach (var element in array.EnumerateArray())
        {
            if (!TryReadKey(element, createdWhen, keyIds, out var key, out error))
                return false;

            result.Add(key);
        }

        keys = result;
        error = null;
        return true;
    }

    private bool TryReadKey(
        JsonElement element,
        DateTimeOffset createdWhen,
        HashSet<string> keyIds,
        [NotNullWhen(true)] out SecretKey? key,
        [NotNullWhen(false)] out string? error
    )
    {
        key = null;

        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(JsonWebKeyParameterNames.KeyType, out var kty)
            || kty.ValueKind != JsonValueKind.String
        )
        {
            error = "Every key in the JSON Web Key Set must be a JSON object with a 'kty' member.";
            return false;
        }

        foreach (var member in PrivateMembers)
        {
            if (element.TryGetProperty(member, out _))
            {
                error = $"A key in the JSON Web Key Set carries the private member '{member}'.";
                return false;
            }
        }

        if (
            !element.TryGetProperty(JsonWebKeyParameterNames.KeyId, out var kidElement)
            || kidElement.ValueKind != JsonValueKind.String
            || kidElement.GetString() is not { Length: > 0 } kid
        )
        {
            error = "Every key in the JSON Web Key Set must have a 'kid'.";
            return false;
        }

        if (!keyIds.Add(kid))
        {
            error = $"The 'kid' value '{kid}' is not unique within the JSON Web Key Set.";
            return false;
        }

        var keyType = kty.GetString() ?? string.Empty;
        if (!SecretKeyTypeCatalog.TryGetByJwkKeyType(keyType, out var descriptor))
        {
            error = $"The key type '{keyType}' is not supported.";
            return false;
        }

        var request = new SecretKeyImportRequest
        {
            SecretId = kid,
            KeyId = kid,
            SecretType = descriptor.KeyType,
            Format = SecretKeyImportFormats.Jwk,
            KeyMaterial = element.GetRawText(),
            Use = GetString(element, JsonWebKeyParameterNames.PublicKeyUse),
            Algorithm = GetString(element, JsonWebKeyParameterNames.Algorithm),
            CreatedWhen = createdWhen,
            ExpiresWhen = DateTimeOffset.MaxValue,
        };

        try
        {
            var persisted = SecretImporter.ImportSecret(request);
            key = SecretSerializer.DeserializePublicSecret(persisted);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or CryptographicException)
        {
            error = $"The key '{kid}' is invalid: {exception.Message}";
            return false;
        }

        error = null;
        return true;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;
}
