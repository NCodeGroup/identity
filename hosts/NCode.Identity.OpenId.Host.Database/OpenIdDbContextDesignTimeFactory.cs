// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Lets the EF Core tools (<c>dotnet ef migrations …</c>) construct <see cref="OpenIdDbContext"/> without booting the
/// full host. Migrations are generated into this (the host database) assembly and target Npgsql; the published
/// persistence library ships no migrations. The connection string is a design-time placeholder — migrations are
/// generated from the model and never open a connection.
/// </summary>
internal sealed class OpenIdDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<OpenIdDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=openiddb;Username=postgres;Password=postgres";

    /// <summary>
    /// Builds the service provider that an <see cref="OpenIdDbContext"/> constructed outside a request resolves its
    /// id-generation convention and model contributors from: every slice of the OpenID persistence model, so a
    /// generated migration and a test build the same model.
    /// </summary>
    /// <returns>The service provider, which lives as long as the contexts built over it.</returns>
    // The context resolves its id-generation convention and model contributors from the application service
    // provider; the same contributors must be registered so the generated migration includes every slice.
    internal static ServiceProvider CreateServiceProvider() =>
        new ServiceCollection()
            .AddSingleton<IIdGenerator<long>>(new IdGenerator(0))
            .AddEntityFrameworkCorePersistence<OpenIdDbContext>()
            .AddLocalAccountEntityFramework()
            .ServiceCollection.BuildServiceProvider();

    public OpenIdDbContext CreateDbContext(string[] args) =>
        HostDatabase.CreateUnscopedOpenIdContext(
            CreateServiceProvider(),
            DesignTimeConnectionString
        );
}
