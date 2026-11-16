// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using NCode.Identity.Json;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Contains the root keys that a team brings, keyed by region: the payload that <see cref="TeamEntity.RootKeysJson"/>
/// holds in its schema-versioned envelope. The team store and the local team directory write and read that column only
/// through <see cref="Write"/> and <see cref="Read"/>.
/// </summary>
// See ADR-1395.
internal sealed class TeamRootKeysPayload
{
    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> that write and read the payload: the web defaults with nullable
    /// annotations and required constructor parameters respected, so a payload that lacks a member the serving code
    /// always writes, or holds <c>null</c> for one, fails to read.
    /// </summary>
    private static JsonSerializerOptions JsonOptions { get; } =
        new(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

    /// <summary>
    /// Gets the root keys that the team brings, keyed by region, in a map that is empty when the team brings none.
    /// </summary>
    public required ImmutableDictionary<string, OwnerRootKeyRequest> RootKeys { get; init; }

    /// <summary>
    /// Writes the root keys that a team brings as the schema-versioned envelope of their payload.
    /// </summary>
    /// <param name="payloadSerializer">The <see cref="IPersistedPayloadSerializer"/> that writes the envelope.</param>
    /// <param name="rootKeys">The root keys that the team brings, keyed by region.</param>
    /// <returns>The <see cref="JsonElement"/> that holds the envelope.</returns>
    public static JsonElement Write(
        IPersistedPayloadSerializer payloadSerializer,
        IReadOnlyDictionary<string, OwnerRootKeyRequest> rootKeys
    ) =>
        payloadSerializer.Serialize(
            new TeamRootKeysPayload
            {
                RootKeys = rootKeys.ToImmutableDictionary(StringComparer.Ordinal),
            },
            JsonOptions
        );

    /// <summary>
    /// Reads the root keys that a team brings from the schema-versioned envelope of their payload.
    /// </summary>
    /// <param name="payloadSerializer">The <see cref="IPersistedPayloadSerializer"/> that reads the envelope.</param>
    /// <param name="rootKeysJson">The <see cref="JsonElement"/> that holds the envelope.</param>
    /// <returns>The root keys that the team brings, keyed by region.</returns>
    /// <exception cref="JsonException">
    /// The value is not the schema-versioned envelope of a team's root keys, or its payload lacks the root keys,
    /// holds a region without a root key, or holds a root key without its key wrapper name or key reference.
    /// </exception>
    public static IReadOnlyDictionary<string, OwnerRootKeyRequest> Read(
        IPersistedPayloadSerializer payloadSerializer,
        JsonElement rootKeysJson
    )
    {
        var rootKeys = payloadSerializer
            .Deserialize<TeamRootKeysPayload>(rootKeysJson, JsonOptions)
            .RootKeys;

        // The serializer leaves the nullability of a map's values unchecked, so a region whose root key is null is
        // refused here.
        foreach (var (region, rootKey) in rootKeys)
        {
            if (rootKey is null)
            {
                throw new JsonException(
                    $"The stored root keys of a team hold no root key for the region '{region}'."
                );
            }
        }

        return rootKeys;
    }
}
