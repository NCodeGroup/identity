// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Provides a default implementation of <see cref="ITenantKeyPlacementGuard"/> that asks the global directory where it
/// places the tenant. A tenant placed in this cell or in no cell may change; a tenant placed in another cell, or whose
/// placement cannot be read, may not, so a change is deferred and never made on a guess. The control-plane cell's
/// control-plane tenant is placed in no cell and never moves, so its keys change without asking the directory.
/// </summary>
// See ADR-1404, ADR-1411 and ADR-1514.
internal sealed class DefaultTenantKeyPlacementGuard(
    ITenantDirectory tenantDirectory,
    ITenantResolver tenantResolver,
    IOptions<CellOptions> cellOptions,
    ILogger<DefaultTenantKeyPlacementGuard> logger
) : ITenantKeyPlacementGuard
{
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IOptions<CellOptions> CellOptions { get; } = cellOptions;
    private ILogger<DefaultTenantKeyPlacementGuard> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<bool> MayChangeKeysAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        // The control-plane cell's control-plane tenant is placed in no cell and never moves, and the directory
        // accepts only the tokens it issues, so its keys change without asking the directory (ADR-1411).
        if (RootTenantRules.IsDirectoryTokenIssuer(CellOptions.Value, TenantResolver, tenantId))
        {
            return true;
        }

        TenantPlacement? placement;
        try
        {
            placement = await TenantDirectory.GetPlacementOrDefaultAsync(
                tenantId,
                cancellationToken
            );
        }
        catch (DirectoryUnavailableException exception)
        {
            Logger.TenantKeysPlacementUnknown(tenantId, exception);
            return false;
        }

        if (placement is null)
        {
            return true;
        }

        var cellName = CellOptions.Value.Name;
        if (string.Equals(placement.Cell, cellName, StringComparison.Ordinal))
        {
            return true;
        }

        Logger.TenantKeysPlacedElsewhere(tenantId, placement.Cell);
        return false;
    }
}
