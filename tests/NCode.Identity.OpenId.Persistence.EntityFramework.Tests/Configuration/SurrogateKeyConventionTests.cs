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

public class SurrogateKeyConventionTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose()
    {
        Mocks.Verify();
    }

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

    private static TeamEntity CreateTeam() =>
        new()
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

    #region ProcessPropertyAdded Tests

    [Fact]
    public void Add_TeamWithoutKey_MintsTheKeyFromTheRegisteredGenerator()
    {
        var fixedKey = Guid.CreateVersion7();
        var mockGenerator = Mocks.Create<ISurrogateKeyGenerator>();
        mockGenerator.Setup(x => x.CreateKey()).Returns(fixedKey).Verifiable();

        using var serviceProvider = CreateServiceProvider(serviceCollection =>
            serviceCollection.Replace(ServiceDescriptor.Singleton(mockGenerator.Object))
        );
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var team = CreateTeam();

        dbContext.Set<TeamEntity>().Add(team);

        Assert.Equal(fixedKey, team.Id);
    }

    [Fact]
    public void Add_TwoProvidersWithDifferentGenerators_MintsEachKeyFromItsOwnProvider()
    {
        var firstKey = Guid.CreateVersion7();
        var secondKey = Guid.CreateVersion7();

        var mockFirstGenerator = Mocks.Create<ISurrogateKeyGenerator>();
        mockFirstGenerator.Setup(x => x.CreateKey()).Returns(firstKey).Verifiable();

        var mockSecondGenerator = Mocks.Create<ISurrogateKeyGenerator>();
        mockSecondGenerator.Setup(x => x.CreateKey()).Returns(secondKey).Verifiable();

        using var firstServiceProvider = CreateServiceProvider(serviceCollection =>
            serviceCollection.Replace(ServiceDescriptor.Singleton(mockFirstGenerator.Object))
        );
        using var secondServiceProvider = CreateServiceProvider(serviceCollection =>
            serviceCollection.Replace(ServiceDescriptor.Singleton(mockSecondGenerator.Object))
        );
        using var firstScope = firstServiceProvider.CreateScope();
        using var secondScope = secondServiceProvider.CreateScope();
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var firstTeam = CreateTeam();
        var secondTeam = CreateTeam();

        firstDbContext.Set<TeamEntity>().Add(firstTeam);
        secondDbContext.Set<TeamEntity>().Add(secondTeam);

        Assert.Equal(firstKey, firstTeam.Id);
        Assert.Equal(secondKey, secondTeam.Id);
    }

    [Fact]
    public void Model_SurrogateKey_IsNeverGeneratedByTheStore()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var entityType = dbContext.Model.FindEntityType(typeof(TeamEntity));
        Assert.NotNull(entityType);

        var property = entityType.FindProperty(nameof(TeamEntity.Id));
        Assert.NotNull(property);

        var valueGenerated = property.ValueGenerated;

        Assert.Equal(ValueGenerated.Never, valueGenerated);
    }

    [Fact]
    public void Model_SurrogateKeyOnANonGuidProperty_Throws()
    {
        var options = new DbContextOptionsBuilder<NonGuidDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new NonGuidDbContext(options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);

        var message = exception.Message;

        Assert.Contains("NonGuidRow.Id", message);
        Assert.Contains("is not a Guid", message);
    }

    #endregion
}
