// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IPlatformDirectory"/> of a cell that runs on its own. It answers from the cell's own
/// platform row. The platform row is the published document, so publishing writes nothing. Every call opens an
/// <see cref="OpenIdDbContext"/> of its own.
/// </summary>
// See ADR-1411.
internal sealed class LocalPlatformDirectory(
    IDbContextFactory<OpenIdDbContext> dbContextFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IPlatformDirectory
{
    private const string UnscopedReason = "directory";

    private IDbContextFactory<OpenIdDbContext> DbContextFactory { get; } = dbContextFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public ValueTask PublishAsync(PlatformDocument document, CancellationToken cancellationToken)
    {
        // The platform row is the published document.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<PlatformDocument?> GetOrDefaultAsync(CancellationToken cancellationToken)
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => new { entity.SettingsJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return null;
        }

        return new PlatformDocument { Settings = settings.SettingsJson.Clone() };
    }
}
