// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NCode.Buffers;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Authentication.Persistence;

/// <summary>
/// Provides the default implementation of <see cref="IGrantPayloadProtector"/> that protects the payload of a grant
/// under the master key of its tenant.
/// </summary>
/// <remarks>
/// The plaintext is written to and read from sensitive buffers and never becomes a string.
/// </remarks>
/// <param name="secretProtectorProvider">The provider of the protector of a tenant.</param>
/// <param name="payloadSerializer">The schema-versioned serializer of payloads.</param>
/// <param name="logger">The logger.</param>
// See ADR-1235.
internal sealed class DefaultGrantPayloadProtector(
    ISecretProtectorProvider secretProtectorProvider,
    IPersistedPayloadSerializer payloadSerializer,
    ILogger<DefaultGrantPayloadProtector> logger
) : IGrantPayloadProtector
{
    private ISecretProtectorProvider SecretProtectorProvider { get; } = secretProtectorProvider;
    private IPersistedPayloadSerializer PayloadSerializer { get; } = payloadSerializer;
    private ILogger<DefaultGrantPayloadProtector> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<string> ProtectAsync<TPayload>(
        string tenantId,
        string hashedKey,
        TPayload payload,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken
    )
    {
        var element = PayloadSerializer.Serialize(payload, jsonOptions);

        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            element.WriteTo(writer);
        }

        var owner = SecretOwner.ForTenant(tenantId);
        var protector = await SecretProtectorProvider.GetProtectorAsync(
            owner,
            [],
            cancellationToken
        );

        var protectionContext = SecretProtectionContext.ForGrantPayload(hashedKey);
        return protector.ProtectToString(buffer.WrittenSpan, protectionContext);
    }

    /// <inheritdoc />
    public async ValueTask<GrantPayload<TPayload>?> UnprotectOrDefaultAsync<TPayload>(
        string tenantId,
        string grantType,
        string hashedKey,
        string protectedPayload,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var owner = SecretOwner.ForTenant(tenantId);
            var protector = await SecretProtectorProvider.GetProtectorAsync(
                owner,
                protectedValues: [protectedPayload],
                cancellationToken
            );

            using var plaintext = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
            IBufferWriter<byte> writer = plaintext;

            var protectionContext = SecretProtectionContext.ForGrantPayload(hashedKey);
            protector.Unprotect(protectedPayload, protectionContext, ref writer);

            var reader = new Utf8JsonReader(plaintext.WrittenSpan);
            var element = JsonElement.ParseValue(ref reader);

            var deserialized = PayloadSerializer.Deserialize<TPayload>(element, jsonOptions);
            return new GrantPayload<TPayload>(deserialized);
        }
        catch (CryptographicException exception)
        {
            Logger.GrantPayloadUnreadable(exception, tenantId, grantType);
            return null;
        }
    }
}
