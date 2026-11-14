// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NCode.Identity.OpenId.GlobalDirectory.Server;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Lets the EF Core tools (<c>dotnet ef migrations …</c>) construct <see cref="DirectoryDbContext"/> without booting the
/// host. Migrations are generated into this (the directory host) assembly and target Npgsql; the directory server
/// package ships no migrations. The connection string is a design-time placeholder — migrations are generated from the
/// model and never open a connection.
/// </summary>
internal sealed class DirectoryDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<DirectoryDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=directory;Username=postgres;Password=postgres";

    public DirectoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DirectoryDbContext>();
        optionsBuilder.UseNpgsql(
            DesignTimeConnectionString,
            npgsql =>
                npgsql.MigrationsAssembly(
                    typeof(DirectoryDbContextDesignTimeFactory).Assembly.GetName().Name
                )
        );

        return new DirectoryDbContext(optionsBuilder.Options);
    }
}
