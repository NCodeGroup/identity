// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultTeamDocumentApplierTests : BaseTests
{
    private const string TeamId = "acme";
    private const string UnscopedReason = "directory team mirror";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IMediator> MockMediator { get; }
    private FakeLogger<DefaultTeamDocumentApplier> Logger { get; } = new();
    private ServiceProvider ServiceProvider { get; }
    private DefaultTeamDocumentApplier Applier { get; }

    public DefaultTeamDocumentApplierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockMediator = CreateStrictMock<IMediator>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => MockMediator.Object);
        ServiceProvider = serviceCollection.BuildServiceProvider();

        Applier = new DefaultTeamDocumentApplier(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Logger
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServiceProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    #region ApplyAsync Tests

    [Fact]
    public async Task ApplyAsync_WhenTheTeamIsNotStored_MirrorsItAndSendsTheCommandWithSettingsChanged()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync((PersistedTeamSettings?)null)
            .Verifiable();
        SetupMirror(isApplied: true, cancellationToken);
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: true, cancellationToken);

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 7),
            cancellationToken
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSettingsAreUnchanged_SendsTheCommandWithoutSettingsChanged()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag": true}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: false, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSettingsDiffer_SendsTheCommandWithSettingsChanged()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: true, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSequenceIsStale_DoesNotSaveOrSendTheCommand()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: false, It.IsAny<CancellationToken>());

        // Strict mocks: neither a save nor a command is set up, so either would fail the test.
        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 3),
            CancellationToken.None
        );
    }

    #endregion

    #region RetireAsync Tests

    [Fact]
    public async Task RetireAsync_WhenTheTeamIsNotStored_DoesNothing()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedTeam?)null)
            .Verifiable();

        await Applier.RetireAsync(TeamId, 9, CancellationToken.None);
    }

    [Fact]
    public async Task RetireAsync_WhenNothingNamesTheTeam_RemovesItAndSendsTheCommand()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(false)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.RemoveAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false, cancellationToken);

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task RetireAsync_WhenSomethingNamesTheTeam_DisablesItAndLogsAWarning()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        MockTeamStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedTeam>(team => team.TeamId == TeamId && team.IsDisabled),
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false, cancellationToken);

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryRetiredTeamHasDependents, record.Id.Id);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Sequence", Value: "9" });
    }

    #endregion

    #region GetAppliedSequenceAsync Tests

    [Fact]
    public async Task GetAppliedSequenceAsync_ReturnsTheHighestMirroredSequence()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetMaxDirectorySequenceAsync(cancellationToken))
            .ReturnsAsync(42L)
            .Verifiable();

        var result = await Applier.GetAppliedSequenceAsync(cancellationToken);

        Assert.Equal(42L, result);
    }

    #endregion

    private static TeamDocument CreateDocument(string settingsJson, long sequence) =>
        new()
        {
            TeamId = TeamId,
            DisplayName = "Acme",
            SettingsJson = settingsJson,
            Sequence = sequence,
        };

    private static PersistedTeam CreateStoredTeam() =>
        new()
        {
            TeamId = TeamId,
            ConcurrencyToken = "token-1",
            IsDisabled = false,
            DisplayName = "Acme",
            Settings = new PersistedTeamSettings
            {
                TeamId = TeamId,
                ConcurrencyToken = "settings-token-1",
                Value = JsonElement.Parse("{}"),
            },
        };

    private void SetupUnscoped() =>
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object)
            .Verifiable();

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ITeamStore>())
            .Returns(MockTeamStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupStoredSettings(string settingsJson) =>
        MockTeamStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedTeamSettings
                {
                    TeamId = TeamId,
                    ConcurrencyToken = "settings-token-1",
                    Value = JsonElement.Parse(settingsJson),
                }
            )
            .Verifiable();

    private void SetupMirror(bool isApplied, CancellationToken cancellationToken) =>
        MockTeamStore
            .Setup(x =>
                x.MirrorAsync(
                    It.Is<PersistedTeam>(team =>
                        team.TeamId == TeamId && !team.IsDisabled && team.DisplayName == "Acme"
                    ),
                    cancellationToken
                )
            )
            .ReturnsAsync(isApplied)
            .Verifiable();

    private void SetupSave(CancellationToken cancellationToken) =>
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupCommand(bool settingsChanged, CancellationToken cancellationToken) =>
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<TeamChangedCommand>(command =>
                        command.TeamId == TeamId && command.SettingsChanged == settingsChanged
                    ),
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
}
