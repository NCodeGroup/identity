// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;

public class SurrogateKeyConventionTests
{
    private sealed class NonGuidRow
    {
        [Key]
        [SurrogateKey]
        public long Id { get; set; }
    }

    private sealed class NonGuidDbContext(DbContextOptions<NonGuidDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Conventions.Add(_ => new SurrogateKeyConvention());
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NonGuidRow>();
        }
    }

    private static ServiceProvider CreateServiceProvider(
        Action<IServiceCollection>? configure = null
    )
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        configure?.Invoke(serviceCollection);
        return serviceCollection.BuildServiceProvider();
    }

    #region ProcessPropertyAdded Tests

    [Fact]
    public void Add_TeamWithoutKey_MintsTheKeyFromTheRegisteredGenerator()
    {
        var fixedKey = Guid.CreateVersion7();
        var mockGenerator = new Mock<ISurrogateKeyGenerator>(MockBehavior.Strict);
        mockGenerator.Setup(x => x.CreateKey()).Returns(fixedKey).Verifiable();

        using var serviceProvider = CreateServiceProvider(serviceCollection =>
            serviceCollection.Replace(ServiceDescriptor.Singleton(mockGenerator.Object))
        );
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var team = new TeamEntity
        {
            TeamId = "platform",
            NormalizedTeamId = "platform",
            Path = "/platform/",
            ConcurrencyToken = string.Empty,
            SettingsConcurrencyToken = string.Empty,
            IsDisabled = false,
            DisplayName = "Platform",
            SettingsJson = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            RootKeysJson = JsonElement.Parse("""{"$ver":1,"$payload":{"rootKeys":{}}}"""),
            Tenants = new List<TenantEntity>(),
        };

        dbContext.Set<TeamEntity>().Add(team);

        Assert.Equal(fixedKey, team.Id);
        mockGenerator.Verify();
    }

    [Fact]
    public void Model_SurrogateKey_IsNeverGeneratedByTheStore()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var property = dbContext
            .Model.FindEntityType(typeof(TeamEntity))!
            .FindProperty(nameof(TeamEntity.Id))!;

        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    [Fact]
    public void Model_SurrogateKeyOnANonGuidProperty_Throws()
    {
        var options = new DbContextOptionsBuilder<NonGuidDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new NonGuidDbContext(options);

        Assert.Throws<InvalidOperationException>(() => context.Model);
    }

    #endregion
}
