// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Platform;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultPlatformDocumentApplierTests : BaseTests
{
    private const string UnscopedReason = "directory platform mirror";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IPlatformStore> MockPlatformStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IMediator> MockMediator { get; }
    private ServiceProvider ServiceProvider { get; }
    private DefaultPlatformDocumentApplier Applier { get; }

    public DefaultPlatformDocumentApplierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockPlatformStore = CreateStrictMock<IPlatformStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockMediator = CreateStrictMock<IMediator>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => MockMediator.Object);
        ServiceProvider = serviceCollection.BuildServiceProvider();

        Applier = new DefaultPlatformDocumentApplier(
            MockStoreManagerFactory.Object,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            MockAmbientTenantAccessor.Object
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
    public async Task ApplyAsync_WhenNoPlatformIsStored_MirrorsItAndSendsEmptyPreviousSettings()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockPlatformStore
            .Setup(x => x.GetSettingsOrDefaultAsync(cancellationToken))
            .ReturnsAsync((PersistedPlatformSettings?)null)
            .Verifiable();
        SetupMirror(isApplied: true, sequence: 5, cancellationToken);
        SetupSave(cancellationToken);
        SetupCommand("{}", """{"flag":true}""", cancellationToken);

        await Applier.ApplyAsync(
            new PlatformDocument { SettingsJson = """{"flag":true}""", Sequence = 5 },
            cancellationToken
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenAPlatformIsStored_SendsTheStoredSettingsAsThePreviousSettings()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockPlatformStore
            .Setup(x => x.GetSettingsOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedPlatformSettings
                {
                    ConcurrencyToken = "settings-token-1",
                    Value = JsonElement.Parse("""{"flag":false}"""),
                }
            )
            .Verifiable();
        SetupMirror(isApplied: true, sequence: 6, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand("""{"flag":false}""", """{"flag":true}""", It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            new PlatformDocument { SettingsJson = """{"flag":true}""", Sequence = 6 },
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSequenceIsStale_DoesNotSaveOrSendTheCommand()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockPlatformStore
            .Setup(x => x.GetSettingsOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedPlatformSettings?)null)
            .Verifiable();
        SetupMirror(isApplied: false, sequence: 2, It.IsAny<CancellationToken>());

        // Strict mocks: neither a save nor a command is set up, so either would fail the test.
        await Applier.ApplyAsync(
            new PlatformDocument { SettingsJson = """{"flag":true}""", Sequence = 2 },
            CancellationToken.None
        );
    }

    #endregion

    #region GetAppliedSequenceAsync Tests

    [Fact]
    public async Task GetAppliedSequenceAsync_WhenNoPlatformIsStored_ReturnsZero()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockPlatformStore
            .Setup(x => x.GetOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedPlatform?)null)
            .Verifiable();

        var result = await Applier.GetAppliedSequenceAsync(CancellationToken.None);

        Assert.Equal(0L, result);
    }

    [Fact]
    public async Task GetAppliedSequenceAsync_WhenAPlatformIsStored_ReturnsItsSequence()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockPlatformStore
            .Setup(x => x.GetOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedPlatform
                {
                    ConcurrencyToken = "token-1",
                    DirectorySequence = 11,
                    Settings = new PersistedPlatformSettings
                    {
                        ConcurrencyToken = "settings-token-1",
                        Value = JsonElement.Parse("{}"),
                    },
                }
            )
            .Verifiable();

        var result = await Applier.GetAppliedSequenceAsync(CancellationToken.None);

        Assert.Equal(11L, result);
    }

    #endregion

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
            .Setup(x => x.GetStore<IPlatformStore>())
            .Returns(MockPlatformStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupMirror(bool isApplied, long sequence, CancellationToken cancellationToken) =>
        MockPlatformStore
            .Setup(x =>
                x.MirrorSettingsAsync(
                    It.Is<PersistedPlatformSettings>(settings =>
                        settings.Value.GetRawText() == """{"flag":true}"""
                    ),
                    sequence,
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

    private void SetupCommand(
        string previousSettings,
        string settings,
        CancellationToken cancellationToken
    ) =>
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<PlatformSettingsChangedCommand>(command =>
                        command.PreviousSettings.GetRawText() == previousSettings
                        && command.Settings.GetRawText() == settings
                    ),
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
}
