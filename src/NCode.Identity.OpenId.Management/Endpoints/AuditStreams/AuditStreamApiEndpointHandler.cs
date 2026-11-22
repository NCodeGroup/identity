// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.AspNetCore;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.AuditStreams;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints.AuditStreams;

/*

GET    api/tenants/{tenantId}/audit-streams
POST   api/tenants/{tenantId}/audit-streams
GET    api/tenants/{tenantId}/audit-streams/{streamId}
PATCH  api/tenants/{tenantId}/audit-streams/{streamId}
DELETE api/tenants/{tenantId}/audit-streams/{streamId}

POST   api/tenants/{tenantId}/audit-streams/{streamId}/signing-secrets

*/

/// <summary>
/// Provides the API endpoints for managing a tenant's audit streams: the HTTPS endpoints that receive the tenant's
/// audit records as they are stored, each signed with a secret that the tenant verifies the deliveries with. A signing
/// secret's value is returned once, when the stream is created or the secret is rotated, and never again.
/// </summary>
// See ADR-1534.
internal class AuditStreamApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    ISecretProtectorProvider secretProtectorProvider,
    IOpenIdContextFactory openIdContextFactory,
    IManagementAuditRecorder managementAuditRecorder,
    TimeProvider timeProvider
) : BaseApiEndpointHandler, IEndpointProvider
{
    private const string SigningSecretPrefix = "whsec_";
    private const int SigningSecretByteLength = 32;

    private static TimeSpan SigningSecretOverlap { get; } = TimeSpan.FromHours(24);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ISecretProtectorProvider SecretProtectorProvider { get; } = secretProtectorProvider;
    private IOpenIdContextFactory OpenIdContextFactory { get; } = openIdContextFactory;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var auditStreams = endpoints
            .MapGroup("/audit-streams")
            .WithTags(OpenIdConstants.EndpointTags.AuditStreams);

        auditStreams
            .MapGet("", ListAsync)
            .Produces<CollectionResource<AuditStreamResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        // The response reveals a secret once, so no cache may keep it (ADR-1050).
        auditStreams
            .MapPost("", CreateAsync)
            .Produces<CreatedAuditStreamResource>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .WithNoStore();
        auditStreams
            .MapGet("/{streamId}", GetAsync)
            .Produces<AuditStreamResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        auditStreams
            .MapPatch("/{streamId}", UpdateAsync)
            .Produces<AuditStreamResource>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        auditStreams
            .MapDelete("/{streamId}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
        // The response reveals a secret once, so no cache may keep it (ADR-1050).
        auditStreams
            .MapPost("/{streamId}/signing-secrets", RotateSigningSecretAsync)
            .Produces<CreatedAuditStreamSigningSecretResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .WithNoStore();
    }

    /// <summary>
    /// Gets the location of an audit stream, which the create and rotate-signing-secret routes answer with.
    /// </summary>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="streamId">The identifier of the audit stream.</param>
    /// <returns>The location of the audit stream.</returns>
    private static string GetStreamLocation(string tenantId, string streamId) =>
        $"/tenants/{tenantId}/audit-streams/{streamId}";

    /// <summary>
    /// Maps a <see cref="PersistedAuditStream"/> to its <see cref="AuditStreamResource"/> representation, which never
    /// carries the value of a signing secret.
    /// </summary>
    /// <param name="stream">The <see cref="PersistedAuditStream"/> to map.</param>
    /// <returns>The mapped <see cref="AuditStreamResource"/>.</returns>
    internal virtual AuditStreamResource ToAuditStreamResource(PersistedAuditStream stream)
    {
        var delivery = stream.State is { } state
            ? new AuditStreamDeliveryResource
            {
                LastDeliveryTime = state.LastDeliveryTime,
                LastFailureTime = state.LastFailureTime,
                LastError = state.LastError,
                ConsecutiveFailures = state.ConsecutiveFailures,
                NextAttemptTime = state.NextAttemptTime,
            }
            : new AuditStreamDeliveryResource
            {
                ConsecutiveFailures = 0,
                NextAttemptTime = stream.CreatedTime,
            };

        return new AuditStreamResource
        {
            StreamId = stream.StreamId,
            DisplayName = stream.DisplayName,
            Endpoint = stream.Endpoint,
            IsEnabled = stream.IsEnabled,
            CreatedTime = stream.CreatedTime,
            SigningSecrets =
            [
                .. stream.SigningSecrets.Select(secret => new AuditStreamSigningSecretResource
                {
                    SecretId = secret.SecretId,
                    CreatedTime = secret.CreatedTime,
                    ExpiryTime = secret.ExpiryTime,
                }),
            ],
            Delivery = delivery,
        };
    }

    /// <summary>
    /// Lists the audit streams of a tenant.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="cursor">The opaque continuation token from a previous page, or <c>null</c> for the first
    /// page.</param>
    /// <param name="limit">The maximum number of audit streams to return on the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">A page of audit streams.</response>
    [EndpointName("api/audit-streams/list")]
    internal virtual async ValueTask<IResult> ListAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    )
    {
        var effectiveLimit = NormalizeLimit(limit);

        var tenantScope = TenantScopeResource.For(tenantId);
        return await ProcessListAsync(
            httpContext,
            tenantScope,
            async () =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(
                    cancellationToken
                );
                var store = storeManager.GetStore<IAuditStreamStore>();
                var pageRequest = new PageRequest { Cursor = cursor, Limit = effectiveLimit };
                return await store.GetPageAsync(pageRequest, cancellationToken);
            },
            ToAuditStreamResource,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates an audit stream with its first signing secret. The secret's value is returned exactly once.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="request">The request that describes the audit stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="201">The created audit stream, including its first signing secret's one-time value.</response>
    /// <response code="400">The display name is empty, longer than 300 characters or holds a control character, or the
    /// endpoint is not an absolute <c>https</c> URL of at most 1,000 characters without user information or a
    /// fragment.</response>
    /// <response code="409">The tenant holds its maximum number of audit streams.</response>
    [EndpointName("api/audit-streams/create")]
    internal virtual async ValueTask<IResult> CreateAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromBody] CreateAuditStreamRequest request,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Create,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var validationProblem = GetValidationProblemOrDefault(
            request.DisplayName,
            request.Endpoint
        );
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var maxAuditStreams = await GetMaxAuditStreamsAsync(
            httpContext,
            tenantId,
            cancellationToken
        );

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        var count = await store.CountAsync(cancellationToken);
        if (count >= maxAuditStreams)
        {
            return TypedResults.Problem(
                detail: $"The tenant holds its maximum of {maxAuditStreams} audit streams.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var createdTime = TimeProvider.GetUtcNow();
        var streamId = CryptoService.GenerateResourceId();
        var (signingSecret, signingSecretValue) = await CreateSigningSecretAsync(
            tenantId,
            createdTime,
            cancellationToken
        );

        var stream = new PersistedAuditStream
        {
            TenantId = tenantId,
            StreamId = streamId,
            DisplayName = request.DisplayName,
            Endpoint = request.Endpoint,
            IsEnabled = request.IsEnabled,
            CreatedTime = createdTime,
            ConcurrencyToken = string.Empty,
            SigningSecrets = [signingSecret],
        };

        await store.AddAsync(stream, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var streamResource = ToAuditStreamResource(stream);
        var streamValues = ToResourceValues(streamResource);
        await ManagementAuditRecorder.RecordAuditStreamChangedAsync(
            httpContext,
            tenantId,
            streamId,
            ResourceChangeTypes.Created,
            streamValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(stream.ConcurrencyToken);
        var createdResource = new CreatedAuditStreamResource
        {
            AuditStream = streamResource,
            SigningSecret = new CreatedAuditStreamSigningSecretResource
            {
                SecretId = signingSecret.SecretId,
                SigningSecret = signingSecretValue,
                CreatedTime = signingSecret.CreatedTime,
            },
        };
        var location = GetStreamLocation(tenantId, streamId);
        return TypedResults.Created(location, createdResource);
    }

    /// <summary>
    /// Gets an audit stream.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="streamId">The identifier of the audit stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The requested audit stream, without the value of any signing secret.</response>
    /// <response code="404">The audit stream does not exist.</response>
    [EndpointName("api/audit-streams/get")]
    internal virtual async ValueTask<IResult> GetAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string streamId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantScope,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var store = storeManager.GetStore<IAuditStreamStore>();
                return await store.GetOrDefaultAsync(streamId, token);
            },
            stream =>
            {
                // The stored stream carries its concurrency token without the interface that emits the entity tag.
                httpContext.Response.Headers.ETag = ManagementEntityTags.Format(
                    stream.ConcurrencyToken
                );
                return ToAuditStreamResource(stream);
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Updates an audit stream's display name, endpoint and enabled flag.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="streamId">The identifier of the audit stream.</param>
    /// <param name="request">The request that describes the audit stream.</param>
    /// <param name="ifMatch">The <c>If-Match</c> header that names the entity tag the update applies to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The updated audit stream.</response>
    /// <response code="400">The display name is empty, longer than 300 characters or holds a control character, or the
    /// endpoint is not an absolute <c>https</c> URL of at most 1,000 characters without user information or a
    /// fragment.</response>
    /// <response code="404">The audit stream does not exist.</response>
    /// <response code="412">The supplied <c>If-Match</c> header does not match the current audit stream's entity tag,
    /// or is malformed.</response>
    [EndpointName("api/audit-streams/update")]
    internal virtual async ValueTask<IResult> UpdateAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string streamId,
        [FromBody] UpdateAuditStreamRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Update,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var validationProblem = GetValidationProblemOrDefault(
            request.DisplayName,
            request.Endpoint
        );
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        var stream = await store.GetOrDefaultAsync(streamId, cancellationToken);
        if (stream is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var preconditionError = ManagementEntityTags.GetIfMatchErrorOrDefault(
            ifMatch,
            stream.ConcurrencyToken,
            "audit stream"
        );
        if (preconditionError is not null)
        {
            return ToErrorResult(preconditionError);
        }

        stream.DisplayName = request.DisplayName;
        stream.Endpoint = request.Endpoint;
        stream.IsEnabled = request.IsEnabled;

        // A lost race throws StoreConcurrencyException, which the route group answers with 412 (ADR-1012).
        await store.UpdateAsync(stream, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        // The save gives the stored stream a fresh concurrency token, which the stream read before the save lacks.
        var updated = await store.GetOrDefaultAsync(streamId, cancellationToken);
        if (updated is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var updatedResource = ToAuditStreamResource(updated);
        var updatedValues = ToResourceValues(updatedResource);
        await ManagementAuditRecorder.RecordAuditStreamChangedAsync(
            httpContext,
            tenantId,
            streamId,
            ResourceChangeTypes.Updated,
            updatedValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(updated.ConcurrencyToken);
        return TypedResults.Json(updatedResource);
    }

    /// <summary>
    /// Deletes an audit stream with its signing secrets and delivery state.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="streamId">The identifier of the audit stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The audit stream was deleted.</response>
    /// <response code="404">The audit stream does not exist.</response>
    [EndpointName("api/audit-streams/delete")]
    internal virtual async ValueTask<IResult> DeleteAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string streamId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Delete,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        var stream = await store.GetOrDefaultAsync(streamId, cancellationToken);
        if (stream is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var streamResource = ToAuditStreamResource(stream);
        var streamValues = ToResourceValues(streamResource);

        var removed = await store.RemoveAsync(streamId, cancellationToken);
        if (!removed)
        {
            return ManagementProblems.ResourceNotFound();
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        await ManagementAuditRecorder.RecordAuditStreamChangedAsync(
            httpContext,
            tenantId,
            streamId,
            ResourceChangeTypes.Deleted,
            streamValues
        );

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Rotates an audit stream's signing secret: adds a new secret and lets every secret that has no expiry sign for 24
    /// more hours, so a receiver verifies with either while it switches. The new secret's value is returned exactly
    /// once.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="streamId">The identifier of the audit stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="201">The new signing secret, including its one-time value.</response>
    /// <response code="404">The audit stream does not exist.</response>
    [EndpointName("api/audit-streams/rotate-signing-secret")]
    internal virtual async ValueTask<IResult> RotateSigningSecretAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string streamId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Update,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        var stream = await store.GetOrDefaultAsync(streamId, cancellationToken);
        if (stream is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var now = TimeProvider.GetUtcNow();
        var (signingSecret, signingSecretValue) = await CreateSigningSecretAsync(
            tenantId,
            now,
            cancellationToken
        );

        var signingSecrets = new List<PersistedAuditStreamSigningSecret>(
            stream.SigningSecrets.Count + 1
        );
        foreach (var existing in stream.SigningSecrets)
        {
            if (existing.ExpiryTime is { } expiryTime && expiryTime <= now)
            {
                continue;
            }

            existing.ExpiryTime ??= now.Add(SigningSecretOverlap);
            signingSecrets.Add(existing);
        }

        signingSecrets.Add(signingSecret);
        stream.SigningSecrets = signingSecrets;

        // A lost race throws StoreConcurrencyException, which the route group answers with 412 (ADR-1012).
        await store.UpdateAsync(stream, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var streamResource = ToAuditStreamResource(stream);
        var streamValues = ToResourceValues(streamResource);
        await ManagementAuditRecorder.RecordAuditStreamChangedAsync(
            httpContext,
            tenantId,
            streamId,
            ResourceChangeTypes.Updated,
            streamValues
        );

        var createdResource = new CreatedAuditStreamSigningSecretResource
        {
            SecretId = signingSecret.SecretId,
            SigningSecret = signingSecretValue,
            CreatedTime = signingSecret.CreatedTime,
        };
        var location = GetStreamLocation(tenantId, streamId);
        return TypedResults.Created(location, createdResource);
    }

    // Reads the most audit streams the addressed tenant may hold from the settings of a context of that tenant.
    private async ValueTask<int> GetMaxAuditStreamsAsync(
        HttpContext httpContext,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var mediator = httpContext.RequestServices.GetRequiredService<IMediator>();
        await using var openIdContext = await OpenIdContextFactory.CreateForTenantAsync(
            httpContext,
            tenantId,
            mediator,
            cancellationToken
        );

        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        return settings.GetValue(OpenIdSettingKeys.MaxAuditStreams);
    }

    // Generates a signing secret of 32 random bytes, protects it under the tenant owner's master key, and returns the
    // stored secret with the whsec_ text that the caller sees once. The bytes are zeroed once they are protected.
    private async ValueTask<(
        PersistedAuditStreamSigningSecret SigningSecret,
        string SigningSecretValue
    )> CreateSigningSecretAsync(
        string tenantId,
        DateTimeOffset createdTime,
        CancellationToken cancellationToken
    )
    {
        var secretId = CryptoService.GenerateResourceId();

        var owner = SecretOwner.ForTenant(tenantId);
        var protector = await SecretProtectorProvider.GetProtectorAsync(
            owner,
            [],
            cancellationToken
        );

        Span<byte> secretBytes = stackalloc byte[SigningSecretByteLength];
        try
        {
            CryptoService.GenerateBytes(secretBytes);

            var context = SecretProtectionContext.ForAuditStreamSigningSecret(secretId);
            var protectedValue = protector.ProtectToString(secretBytes, context);
            var signingSecretValue = SigningSecretPrefix + Convert.ToBase64String(secretBytes);

            var signingSecret = new PersistedAuditStreamSigningSecret
            {
                SecretId = secretId,
                ProtectedValue = protectedValue,
                CreatedTime = createdTime,
            };
            return (signingSecret, signingSecretValue);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    // Judges the writable members of an audit stream, naming each field that is refused in a validation problem.
    private static ValidationProblem? GetValidationProblemOrDefault(
        string? displayName,
        string? endpoint
    )
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var displayNameError = GetDisplayNameErrorOrDefault(displayName);
        if (displayNameError is not null)
        {
            errors["displayName"] = [displayNameError];
        }

        var endpointError = GetEndpointErrorOrDefault(endpoint);
        if (endpointError is not null)
        {
            errors["endpoint"] = [endpointError];
        }

        if (errors.Count == 0)
        {
            return null;
        }

        var messages = errors.Values.SelectMany(values => values);
        var detail = string.Join(" ", messages);
        return TypedResults.ValidationProblem(
            errors,
            detail: detail,
            title: "The audit stream is invalid."
        );
    }

    private static string? GetDisplayNameErrorOrDefault(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "'displayName' must not be empty.";
        }

        if (displayName.Length > OpenIdMaxLengths.DisplayName)
        {
            return $"'displayName' must be at most {OpenIdMaxLengths.DisplayName} characters.";
        }

        return displayName.Any(char.IsControl)
            ? "'displayName' must not contain control characters."
            : null;
    }

    private static string? GetEndpointErrorOrDefault(string? endpoint)
    {
        if (endpoint is { Length: > OpenIdMaxLengths.UrlValue })
        {
            return $"'endpoint' must be at most {OpenIdMaxLengths.UrlValue} characters.";
        }

        if (!AbsoluteUri.TryParseHttps(endpoint, out var uri))
        {
            return "'endpoint' must be an absolute https URL.";
        }

        if (uri.UserInfo.Length > 0)
        {
            return "'endpoint' must not carry user information.";
        }

        return endpoint.Contains('#') ? "'endpoint' must not carry a fragment." : null;
    }
}
