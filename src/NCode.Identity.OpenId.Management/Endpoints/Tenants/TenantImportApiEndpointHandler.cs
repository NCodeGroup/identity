// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Exceptions;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/*

POST api/tenant-imports
POST api/tenant-imports/transfer-keys

*/

/// <summary>
/// Provides the API endpoints that a cell publishes to receive a tenant. One issues a one-time transfer key whose
/// public half seals the tenant's export. The other imports the export: it takes the transfer key, decrypts the
/// package, imports every section and the master keys, retags the remote keys that the imported secrets reference to
/// this cell, and moves the tenant's placement in the global directory to this cell. A package names the cell it is
/// offered to, and only that cell imports it. The tenant stays disabled. Every cell maps the routes, and the caller
/// needs the update operation on the platform node.
/// </summary>
// See ADR-1514.
internal class TenantImportApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    ITenantTransferKeyService tenantTransferKeyService,
    IEnumerable<ITenantTransferContributor> tenantTransferContributors,
    IMasterKeyTransfer masterKeyTransfer,
    IJoseSerializer joseSerializer,
    IRemoteKeyProviderCatalog remoteKeyProviderCatalog,
    ITenantDirectory tenantDirectory,
    IManagementAuditRecorder managementAuditRecorder,
    IOptions<CellOptions> cellOptions
) : BaseApiEndpointHandler, IEndpointProvider
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ITenantTransferKeyService TenantTransferKeyService { get; } = tenantTransferKeyService;
    private IReadOnlyList<ITenantTransferContributor> TenantTransferContributors { get; } =
    [.. tenantTransferContributors.OrderBy(contributor => contributor.Order)];
    private IMasterKeyTransfer MasterKeyTransfer { get; } = masterKeyTransfer;
    private IJoseSerializer JoseSerializer { get; } = joseSerializer;
    private IRemoteKeyProviderCatalog RemoteKeyProviderCatalog { get; } = remoteKeyProviderCatalog;
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints
            .MapGroup("/tenant-imports")
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        imports
            .MapPost("", ImportTenantAsync)
            .WithMetadata(new RequestSizeLimitAttribute(TenantTransferLimits.MaxImportRequestBytes))
            .Produces<TenantImportResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        imports
            .MapPost("/transfer-keys", CreateTransferKeyAsync)
            .Produces<TenantTransferKeyResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Issues a one-time transfer key that this cell takes once, when it imports the tenant that the key seals.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The <c>kid</c> and the public <c>JWK</c> of the transfer key, which expires after one hour.</response>
    [EndpointName("api/tenant-imports/transfer-keys/create")]
    internal virtual async ValueTask<IResult> CreateTransferKeyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetPlatformNode(httpContext),
            Operations.Update,
            cancellationToken
        );

        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var transferKey = await TenantTransferKeyService.CreateAsync(cancellationToken);
        return TypedResults.Json(
            new TenantTransferKeyResource { Kid = transferKey.KeyId, Jwk = transferKey.Jwk }
        );
    }

    /// <summary>
    /// Imports a tenant that another cell exported, and moves the tenant's placement in the global directory to this
    /// cell. A move that the directory does not confirm is read back from the placement before anything is undone. The
    /// tenant stays disabled.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="request">
    /// The <see cref="TenantImportRequest"/> that names the transfer key and carries the package.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The tenant is imported and placed in this cell, also after a move that the directory did not confirm.</response>
    /// <response code="409">The directory places the tenant in this cell or in no cell, or refuses the move while it still places the tenant in the source cell. Nothing the import wrote remains.</response>
    /// <response code="413">The request body is larger than a tenant move admits: 64 MiB plus 64 KiB.</response>
    /// <response code="422">The kid is empty, or the transfer key is unknown, expired or already used; the package is not sealed to a transfer key, does not decrypt with it or is malformed, its format version is not one this cell imports, its region is not this cell's region, it is offered to another cell, or it has a section that this cell does not import; the tenant's root key is refused; or a remote key that the tenant's secrets reference belongs to a provider that this cell does not register or lies outside the location this cell uses.</response>
    /// <response code="503">The global directory is unavailable before the import writes anything, or at a move after which it still places the tenant in the source cell, and nothing the import wrote remains; or the outcome of the move is unknown, this cell keeps what the import wrote, and the problem's <c>moveOutcome</c> is <c>unknown</c>.</response>
    [EndpointName("api/tenant-imports/create")]
    internal virtual async ValueTask<IResult> ImportTenantAsync(
        HttpContext httpContext,
        [FromBody] TenantImportRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetPlatformNode(httpContext),
            Operations.Update,
            cancellationToken
        );

        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        // TakeAsync refuses an empty kid, and no transfer key has one.
        if (string.IsNullOrWhiteSpace(request.Kid))
        {
            return Unprocessable("The transfer key is unknown, expired or already used.");
        }

        var privateKey = await TenantTransferKeyService.TakeAsync(request.Kid, cancellationToken);
        if (privateKey is null)
        {
            return Unprocessable("The transfer key is unknown, expired or already used.");
        }

        var token = ParsePackageOrDefault(request.Package);
        if (token is null)
        {
            return Unprocessable("The package does not decrypt with the transfer key.");
        }

        if (!IsSealedToTransferKey(token))
        {
            return Unprocessable("The tenant package is not sealed to a transfer key.");
        }

        var decodedPackage = await DecodePackageOrDefaultAsync(
            token,
            privateKey,
            cancellationToken
        );
        if (decodedPackage is not { } root)
        {
            return Unprocessable("The package does not decrypt with the transfer key.");
        }

        if (
            !TryReadPackage(
                root,
                out var version,
                out var region,
                out var targetCell,
                out var tenantId,
                out var masterKeys,
                out var sections
            )
        )
        {
            return Unprocessable("The package is malformed.");
        }

        if (version != TenantTransferPackage.FormatVersion)
        {
            return Unprocessable("The package's format version is not one this cell imports.");
        }

        if (region != CellOptions.Region)
        {
            return Unprocessable(
                $"The package is from region '{region}', and this cell is in region '{CellOptions.Region}'."
            );
        }

        if (targetCell != CellOptions.Name)
        {
            return Unprocessable(
                $"The package is offered to the cell '{targetCell}', and this cell is '{CellOptions.Name}'."
            );
        }

        var contributors = TenantTransferContributors
            .Where(contributor => sections.ContainsKey(contributor.Name))
            .ToList();
        if (contributors.Count != sections.Count)
        {
            return Unprocessable("The package has a section that this cell does not import.");
        }

        TenantPlacement? placement;
        try
        {
            placement = await TenantDirectory.GetPlacementOrDefaultAsync(
                tenantId,
                cancellationToken
            );
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }

        if (placement is null)
        {
            return TypedResults.Problem(
                detail: $"The directory does not place the tenant '{tenantId}' in any cell.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var sourceCell = placement.Cell;
        if (sourceCell == CellOptions.Name)
        {
            return TypedResults.Problem(
                detail: $"The directory already places the tenant '{tenantId}' in this cell.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        // A tenant row in this cell while the directory places the tenant in another cell is the residue of an import
        // that stopped before its move: each contributor and the master key transfer commit on their own. The import
        // removes those rows before it imports. The retag is not undone, because the import that follows retags every
        // remote key that the package's secrets reference to this cell, and its own rollback tags them back.
        string teamId;
        bool exists;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            exists =
                await storeManager
                    .GetStore<ITenantStore>()
                    .GetOrDefaultAsync(tenantId, cancellationToken)
                    is not null;
        }

        if (exists)
        {
            await RemoveImportedAsync(tenantId, contributors, CancellationToken.None);
        }

        // Each contributor and the master key transfer commit on their own, so a failure before the move removes what
        // was written and puts the remote keys back under the source cell's tag before the answer.
        var retagged = new List<(IRemoteKeyProvider Provider, string KeyId)>();
        IResult? failure;
        try
        {
            failure = await ImportAndRetagAsync(
                tenantId,
                masterKeys,
                privateKey,
                contributors,
                sections,
                retagged,
                cancellationToken
            );
        }
        catch
        {
            await RollbackAsync(
                tenantId,
                sourceCell,
                contributors,
                retagged,
                CancellationToken.None
            );
            throw;
        }

        if (failure is not null)
        {
            await RollbackAsync(
                tenantId,
                sourceCell,
                contributors,
                retagged,
                CancellationToken.None
            );
            return failure;
        }

        var moveFailure = await MoveToThisCellAsync(
            httpContext,
            tenantId,
            sourceCell,
            contributors,
            retagged,
            cancellationToken
        );
        if (moveFailure is not null)
        {
            return moveFailure;
        }

        // The directory places the tenant in this cell whatever the request's cancellation, so the import is read and
        // audited to the end.
        await using (
            var storeManager = await StoreManagerFactory.CreateAsync(CancellationToken.None)
        )
        {
            var imported =
                await storeManager
                    .GetStore<ITenantStore>()
                    .GetOrDefaultAsync(tenantId, CancellationToken.None)
                ?? throw new InvalidOperationException(
                    $"The imported tenant '{tenantId}' is not stored."
                );
            teamId = imported.TeamId;
        }

        await ManagementAuditRecorder.RecordTenantChangedAsync(
            httpContext,
            teamId,
            tenantId,
            ResourceChangeTypes.Imported,
            JsonSerializer.SerializeToElement(
                new Dictionary<string, string> { ["sourceCell"] = sourceCell }
            )
        );

        return TypedResults.Ok(
            new TenantImportResource { TenantId = tenantId, SourceCell = sourceCell }
        );
    }

    // Imports each section in order, then the master keys, and retags the remote keys that the imported secrets
    // reference to this cell. A failure that the caller should answer is returned; nothing is returned when the tenant
    // is imported and its keys are retagged.
    private async ValueTask<IResult?> ImportAndRetagAsync(
        string tenantId,
        string masterKeys,
        SecretKey privateKey,
        List<ITenantTransferContributor> contributors,
        Dictionary<string, JsonElement> sections,
        List<(IRemoteKeyProvider Provider, string KeyId)> retagged,
        CancellationToken cancellationToken
    )
    {
        var remoteKeys = new HashSet<RemoteKeyReference>();
        foreach (var contributor in contributors)
        {
            var section = new TenantTransferSection
            {
                Name = contributor.Name,
                Json = sections[contributor.Name],
            };

            var result = await contributor.ImportAsync(tenantId, section, cancellationToken);
            remoteKeys.UnionWith(result.RemoteKeys);
        }

        var transfer = await MasterKeyTransfer.ImportAsync(
            SecretOwner.ForTenant(tenantId),
            masterKeys,
            privateKey,
            cancellationToken
        );

        if (!transfer.IsImported)
        {
            return Unprocessable($"The tenant's root key is refused. {transfer.RefusalDetail}");
        }

        // Every provider is found before any key is retagged, so a key that this cell cannot retag refuses the import
        // while no key has changed, and so does a key outside the location this cell's provider uses, which this
        // cell's sweep would never list.
        var providers = new List<(IRemoteKeyProvider Provider, string KeyId)>(remoteKeys.Count);
        foreach (var remoteKey in remoteKeys)
        {
            if (!RemoteKeyProviderCatalog.TryGetProvider(remoteKey.Provider, out var provider))
            {
                return Unprocessable(
                    $"The tenant's secrets reference a remote key of provider '{remoteKey.Provider}', which this cell does not register."
                );
            }

            if (!provider.IsInLocation(remoteKey.KeyId))
            {
                return Unprocessable(
                    $"The remote key '{remoteKey.KeyId}' of provider '{remoteKey.Provider}' is outside the location this cell uses."
                );
            }

            providers.Add((provider, remoteKey.KeyId));
        }

        // This cell's sweep keeps a key that it tags, and the source cell's sweep skips it.
        foreach (var (provider, keyId) in providers)
        {
            await provider.RetagKeyAsync(keyId, CellOptions.Name, cancellationToken);
            retagged.Add((provider, keyId));
        }

        return null;
    }

    // Moves the tenant's placement to this cell. The directory can commit a move that it does not confirm, such as one
    // whose acknowledgement a timeout loses or whose transaction a retry runs again, so a move that answers a conflict,
    // cannot be reached, is cancelled or fails is read back from the placement: a placement in this cell completes the
    // import, a placement in the source cell undoes it and answers the move's own failure, and any other answer keeps
    // what the import wrote and reports that the outcome is unknown. Nothing is returned when the tenant is placed here.
    private async ValueTask<IResult?> MoveToThisCellAsync(
        HttpContext httpContext,
        string tenantId,
        string sourceCell,
        List<ITenantTransferContributor> contributors,
        List<(IRemoteKeyProvider Provider, string KeyId)> retagged,
        CancellationToken cancellationToken
    )
    {
        IResult refusal;
        try
        {
            var move = await TenantDirectory.MoveAsync(
                tenantId,
                sourceCell,
                CellOptions.Name,
                cancellationToken
            );
            if (move.IsMoved)
            {
                return null;
            }

            refusal = TypedResults.Problem(
                detail: move.ConflictDetail,
                statusCode: StatusCodes.Status409Conflict
            );
        }
        catch (DirectoryUnavailableException)
        {
            refusal = DirectoryUnavailableProblem(httpContext);
        }
        catch
        {
            var thrownOutcome = await ReadMoveOutcomeAsync(tenantId, sourceCell);
            if (thrownOutcome is false)
            {
                await RollbackAsync(
                    tenantId,
                    sourceCell,
                    contributors,
                    retagged,
                    CancellationToken.None
                );
                throw;
            }

            return thrownOutcome is true ? null : MoveUnknownProblem(httpContext);
        }

        var outcome = await ReadMoveOutcomeAsync(tenantId, sourceCell);
        if (outcome is false)
        {
            await RollbackAsync(
                tenantId,
                sourceCell,
                contributors,
                retagged,
                CancellationToken.None
            );
            return refusal;
        }

        return outcome is true ? null : MoveUnknownProblem(httpContext);
    }

    // Reads where the directory places the tenant after a move that it did not confirm, on a token of its own so the
    // read runs whatever the request's cancellation: true when it places the tenant in this cell, false when it places
    // it in the source cell, and null when it places it in another cell or in none, or cannot be reached.
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The read must complete whatever the caller does, so it runs on its own token and takes none (ADR-1496)."
    )]
    private async ValueTask<bool?> ReadMoveOutcomeAsync(string tenantId, string sourceCell)
    {
        TenantPlacement? placement;
        try
        {
            placement = await TenantDirectory.GetPlacementOrDefaultAsync(
                tenantId,
                CancellationToken.None
            );
        }
        catch (DirectoryUnavailableException)
        {
            return null;
        }

        return placement?.Cell == CellOptions.Name ? true
            : placement?.Cell == sourceCell ? false
            : (bool?)null;
    }

    // Puts the retagged remote keys back under the source cell's tag, so that this cell's sweep does not destroy keys
    // that the source cell still holds, then removes what the import wrote. Callers pass a token that is not
    // cancelled, so the cleanup runs to completion whatever the request's cancellation.
    private async ValueTask RollbackAsync(
        string tenantId,
        string sourceCell,
        List<ITenantTransferContributor> contributors,
        List<(IRemoteKeyProvider Provider, string KeyId)> retagged,
        CancellationToken cancellationToken
    )
    {
        foreach (var (provider, keyId) in retagged)
        {
            await provider.RetagKeyAsync(keyId, sourceCell, cancellationToken);
        }

        await RemoveImportedAsync(tenantId, contributors, cancellationToken);
    }

    // Removes what an import wrote: each contributor's rows in reverse order, then the tenant with its master keys.
    private async ValueTask RemoveImportedAsync(
        string tenantId,
        List<ITenantTransferContributor> contributors,
        CancellationToken cancellationToken
    )
    {
        for (var index = contributors.Count - 1; index >= 0; index--)
        {
            await contributors[index].RemoveAsync(tenantId, cancellationToken);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        await storeManager
            .GetStore<ITenantStore>()
            .RemoveFromCellAsync(tenantId, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    // Parses the package. A package that is not a JOSE token yields nothing.
    private JoseToken? ParsePackageOrDefault(string package)
    {
        try
        {
            return JoseSerializer.Parse(package);
        }
        catch (Exception exception) when (IsPackageFailure(exception))
        {
            return null;
        }
    }

    // The export seals every package to a transfer key with ECDH-ES+A256KW and A256GCM, so a package whose protected
    // header names anything else is refused before the transfer key decrypts it: a key management algorithm that
    // does not fit the transfer key would only fail.
    private static bool IsSealedToTransferKey(JoseToken token) =>
        token.Header.ValueKind == JsonValueKind.Object
        && HasHeaderValue(
            token.Header,
            JoseClaimNames.Header.Alg,
            AlgorithmCodes.KeyManagement.EcdhEsAes256
        )
        && HasHeaderValue(
            token.Header,
            JoseClaimNames.Header.Enc,
            AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm
        );

    private static bool HasHeaderValue(JsonElement header, string name, string value) =>
        header.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && property.ValueEquals(value);

    // A package that is not a JOSE token, that the transfer key does not decrypt, or whose algorithm cannot use the
    // transfer key, which the serializer reports with an ArgumentException.
    private static bool IsPackageFailure(Exception exception) =>
        exception
            is JoseException
                or CryptographicException
                or FormatException
                or JsonException
                or ArgumentException;

    // Decrypts the parsed package with the transfer key. A package that the key does not decrypt yields nothing.
    private async ValueTask<JsonElement?> DecodePackageOrDefaultAsync(
        JoseToken token,
        SecretKey privateKey,
        CancellationToken cancellationToken
    )
    {
        byte[]? payload = null;
        try
        {
            using var decoded = BufferFactory.CreatePooledBufferWriter(isSensitive: true);
            await JoseSerializer.DecodeAsync(token, privateKey, decoded, cancellationToken);
            payload = decoded.AsReadOnlySequence.ToArray();
            return JsonElement.Parse(payload);
        }
        catch (Exception exception) when (IsPackageFailure(exception))
        {
            return null;
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    // Reads the payload that the export writes. The format version is a required number, the region, the target cell,
    // the tenant identifier and the master keys are required strings, and each section has a distinct name and a JSON
    // value.
    private static bool TryReadPackage(
        JsonElement root,
        out int version,
        [NotNullWhen(true)] out string? region,
        [NotNullWhen(true)] out string? targetCell,
        [NotNullWhen(true)] out string? tenantId,
        [NotNullWhen(true)] out string? masterKeys,
        [NotNullWhen(true)] out Dictionary<string, JsonElement>? sections
    )
    {
        version = 0;
        region = null;
        targetCell = null;
        tenantId = null;
        masterKeys = null;
        sections = null;

        if (
            root.ValueKind != JsonValueKind.Object
            || !TryReadInt32(root, TenantTransferPackage.VersionMember, out var versionValue)
            || !TryReadString(root, TenantTransferPackage.RegionMember, out var regionValue)
            || !TryReadString(root, TenantTransferPackage.TargetCellMember, out var targetCellValue)
            || !TryReadString(root, TenantTransferPackage.TenantIdMember, out var tenantIdValue)
            || !TryReadString(root, TenantTransferPackage.MasterKeysMember, out var masterKeysValue)
            || !root.TryGetProperty(TenantTransferPackage.SectionsMember, out var sectionsElement)
            || sectionsElement.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        var sectionsValue = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var sectionElement in sectionsElement.EnumerateArray())
        {
            if (
                sectionElement.ValueKind != JsonValueKind.Object
                || !TryReadString(
                    sectionElement,
                    TenantTransferPackage.SectionNameMember,
                    out var name
                )
                || !sectionElement.TryGetProperty(
                    TenantTransferPackage.SectionJsonMember,
                    out var json
                )
                || !sectionsValue.TryAdd(name, json)
            )
            {
                return false;
            }
        }

        version = versionValue;
        region = regionValue;
        targetCell = targetCellValue;
        tenantId = tenantIdValue;
        masterKeys = masterKeysValue;
        sections = sectionsValue;
        return true;
    }

    private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value
    )
    {
        value = null;
        if (
            !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static ProblemHttpResult Unprocessable(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static ProblemHttpResult DirectoryUnavailableProblem(HttpContext httpContext) =>
        RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The global directory is unavailable. Retry the request.",
            }
        );

    // The problem's moveOutcome tells a caller, such as the console, that the move may have committed.
    private static ProblemHttpResult MoveUnknownProblem(HttpContext httpContext) =>
        RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail =
                    "The outcome of the move is unknown. Read the tenant's placement in the global directory before releasing it at the source; a repeated import clears this cell's copy.",
                Extensions = { [ManagementProblemMembers.MoveOutcome] = "unknown" },
            }
        );
}
