// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Json;
using NCode.Identity.Jwt.Validation;
using NCode.Identity.OpenId.GlobalDirectory.Server.Logging;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;

/// <summary>
/// Validates the <c>private_key_jwt</c> client assertion (RFC 7523) with which a cell authenticates at the directory's
/// token endpoint. The assertion's <c>iss</c> and <c>sub</c> are the cell's client identifier, the cell is registered
/// and not retired, its <c>aud</c> is the token endpoint URL or the directory's issuer, its <c>exp</c> lies at most
/// <see cref="MaxLifetime"/> ahead, it carries <c>nbf</c> and <c>iat</c>, a key of the cell's registered key set signs
/// it, and its <c>jti</c> is accepted once until it expires.
/// </summary>
// See ADR-1536.
internal sealed class DirectoryClientAssertionValidator(
    TimeProvider timeProvider,
    DefaultDirectoryStore store,
    IPublicJwksReader jwksReader,
    IAlgorithmCollectionProvider algorithmCollectionProvider,
    IJwtValidator jwtValidator,
    IExpiringKeySet replayKeys,
    IOptions<DirectoryServerOptions> options,
    ILogger<DirectoryClientAssertionValidator> logger
)
{
    /// <summary>
    /// Gets the furthest ahead of the current time that an assertion's <c>exp</c> may lie.
    /// </summary>
    internal static TimeSpan MaxLifetime => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the clock skew that the assertion's <c>nbf</c>, <c>exp</c> and replay window tolerate.
    /// </summary>
    internal static TimeSpan ClockSkew => TimeSpan.FromSeconds(30);

    private const string InvalidDescription = "The assertion is invalid.";
    private const string UnregisteredDescription = "The cell is not registered.";
    private const string ReplayKeyPrefix = "directory-assertion:";

    /// <summary>
    /// Reads the <c>iss</c> claim of a compact JWT without verifying it, to find the keys that verify it.
    /// </summary>
    /// <param name="assertion">The compact JWT.</param>
    /// <param name="issuer">The <c>iss</c> claim when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the payload is a JSON object with a non-empty string <c>iss</c>.</returns>
    private static bool TryReadIssuer(string assertion, out string issuer)
    {
        issuer = string.Empty;
        var parts = assertion.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var payloadBytes = StrictBase64Url.Decode(parts[1]);
            var payload = JsonElement.Parse(payloadBytes);
            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(JoseClaimNames.Payload.Iss, out var element)
                && element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: > 0 } value
            )
            {
                issuer = value;
                return true;
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Reads a numeric date claim of a validated payload.
    /// </summary>
    private static bool TryGetInstant(JsonElement payload, string name, out DateTimeOffset instant)
    {
        instant = default;
        if (!payload.TryGetProperty(name, out var element))
            return false;

        return element.TryGetNumericDate(out instant);
    }

    /// <summary>
    /// Reads a string claim of a validated payload, which is <c>null</c> when the claim is absent or not a string.
    /// </summary>
    private static string? GetString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    /// <summary>
    /// Logs why an assertion is refused and creates the result that gives the caller only a fixed description.
    /// </summary>
    private DirectoryClientAssertionResult Refuse(
        string cellName,
        string reason,
        string description = InvalidDescription,
        Exception? exception = null
    )
    {
        Logger.ClientAssertionRefused(cellName, reason, exception);
        return DirectoryClientAssertionResult.Fail(description);
    }

    private TimeProvider TimeProvider { get; } = timeProvider;
    private DefaultDirectoryStore Store { get; } = store;
    private IPublicJwksReader JwksReader { get; } = jwksReader;
    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;
    private IJwtValidator JwtValidator { get; } = jwtValidator;
    private IExpiringKeySet ReplayKeys { get; } = replayKeys;
    private DirectoryServerOptions Options { get; } = options.Value;
    private ILogger<DirectoryClientAssertionValidator> Logger { get; } = logger;

    /// <summary>
    /// Validates a client assertion.
    /// </summary>
    /// <param name="assertion">The compact JWT client assertion.</param>
    /// <param name="tokenEndpointUrl">The absolute URL of the token endpoint that received the assertion.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The result, which names the authenticated cell or the reason the assertion is refused.</returns>
    public async ValueTask<DirectoryClientAssertionResult> ValidateAsync(
        string assertion,
        string tokenEndpointUrl,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenEndpointUrl);
        if (string.IsNullOrEmpty(assertion) || !TryReadIssuer(assertion, out var issuer))
            return Refuse(string.Empty, "The assertion is not a well-formed JWT.");

        if (
            !issuer.StartsWith(DirectoryTokenIssuer.ClientIdPrefix, StringComparison.Ordinal)
            || issuer.Length == DirectoryTokenIssuer.ClientIdPrefix.Length
        )
            return Refuse(
                string.Empty,
                "The assertion's issuer is not a cell's client identifier."
            );

        var cellName = issuer[DirectoryTokenIssuer.ClientIdPrefix.Length..];
        var jwks = await Store.GetCellClientKeysAsync(cellName, cancellationToken);
        if (jwks is not { } jwksElement)
            return Refuse(
                cellName,
                "The cell is not registered or is retired.",
                UnregisteredDescription
            );

        if (!JwksReader.TryRead(jwksElement, out var keys, out var keyError))
            return Refuse(cellName, $"The cell's registered keys are unusable: {keyError}");

        var algorithms = DirectoryTokenIssuer.GetAsymmetricSignatureAlgorithms(
            AlgorithmCollectionProvider.Collection
        );
        var parameters = new JwtValidationParameters
        {
            ClockSkew = ClockSkew,
            ValidSignatureAlgorithms = algorithms,
            ValidKeyManagementAlgorithms = [],
            ValidContentEncryptionAlgorithms = [],
            ValidCompressionAlgorithms = [],
        }
            .UseValidationKeys(keys)
            .ValidateIssuer(issuer)
            .ValidateAudience(tokenEndpointUrl, Options.Issuer)
            .ValidateTokenLifeTime(lifetime =>
            {
                lifetime.RequireNotBefore = true;
                lifetime.RequireIssuedAt = true;
            });

        var result = await JwtValidator.ValidateAsync(assertion, parameters, cancellationToken);
        if (!result.IsValid)
            return Refuse(
                cellName,
                "The assertion failed validation.",
                exception: result.Exception
            );

        var payload = result.DecodedJwt.Payload;
        var subject = GetString(payload, JoseClaimNames.Payload.Sub);
        if (!string.Equals(subject, issuer, StringComparison.Ordinal))
            return Refuse(cellName, "The assertion's subject is not its issuer.");

        if (!TryGetInstant(payload, JoseClaimNames.Payload.Exp, out var expires))
            return Refuse(cellName, "The assertion has no expiry.");

        var now = TimeProvider.GetUtcNow();
        if (expires - now > MaxLifetime + ClockSkew)
            return Refuse(cellName, "The assertion's expiry is too far ahead.");

        var jti = GetString(payload, JoseClaimNames.Payload.Jti);
        if (string.IsNullOrEmpty(jti))
            return Refuse(cellName, "The assertion has no identifier.");

        var replayKey = $"{ReplayKeyPrefix}{cellName}:{jti}";
        var replayExpires = expires + ClockSkew;
        var accepted = await ReplayKeys.TryAddAsync(replayKey, replayExpires, cancellationToken);
        return accepted
            ? DirectoryClientAssertionResult.Success(cellName)
            : Refuse(cellName, "The assertion was used before.");
    }
}
