// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Declares the Npgsql-specific parts of the <see cref="DirectoryDbContext"/> model, so the context itself stays
/// provider-neutral. The design-time factory and the runtime both configure the database through
/// <see cref="DirectoryDatabase"/>, so the migrations and the running model agree.
/// </summary>
/// <remarks>
/// Teams' normalized paths use PostgreSQL's <c>C</c> collation, so the index on the column serves the subtree prefix
/// match with a range scan.
/// </remarks>
/// <param name="dependencies">The dependencies of the base model customizer.</param>
internal sealed class DirectoryNpgsqlModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    /// <summary>
    /// The name of the collation that orders strings by their bytes, <c>C</c>.
    /// </summary>
    public const string PathCollation = "C";

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder
            .Entity<TeamRecord>()
            .Property(entity => entity.NormalizedPath)
            .UseCollation(PathCollation);
    }
}
