// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultDirectoryFeedCheckpointTests : BaseTests
{
    private const string UnscopedReason = "directory feed checkpoint";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IDirectoryFeedCheckpointStore> MockCheckpointStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IDisposable> MockUnscopedScope { get; }
    private List<string> Calls { get; } = [];
    private DefaultDirectoryFeedCheckpoint Checkpoint { get; }

    public DefaultDirectoryFeedCheckpointTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockCheckpointStore = CreateStrictMock<IDirectoryFeedCheckpointStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockUnscopedScope = CreateStrictMock<IDisposable>();

        Checkpoint = new DefaultDirectoryFeedCheckpoint(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object
        );
    }

    #region GetAppliedSequenceAsync Tests

    [Fact]
    public async Task GetAppliedSequenceAsync_Always_ReturnsTheStoresSequenceInsideAnUnscopedScope()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager(cancellationToken);
        MockCheckpointStore
            .Setup(x => x.GetAppliedSequenceAsync(cancellationToken))
            .Callback(() => Calls.Add("get"))
            .ReturnsAsync(42L)
            .Verifiable();

        var result = await Checkpoint.GetAppliedSequenceAsync(cancellationToken);

        Assert.Equal(42L, result);
        Assert.Equal(["begin", "get", "end"], Calls);
    }

    #endregion

    #region RecordAppliedSequenceAsync Tests

    [Fact]
    public async Task RecordAppliedSequenceAsync_Always_RecordsTheSequenceThenSavesInsideAnUnscopedScope()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager(cancellationToken);
        MockCheckpointStore
            .Setup(x => x.RecordAppliedSequenceAsync(17L, cancellationToken))
            .Callback(() => Calls.Add("record"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(cancellationToken))
            .Callback(() => Calls.Add("save"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Checkpoint.RecordAppliedSequenceAsync(17L, cancellationToken);

        Assert.Equal(["begin", "record", "save", "end"], Calls);
    }

    [Fact]
    public async Task RecordAppliedSequenceAsync_WhenTheStoreFails_DoesNotSave()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var exception = new InvalidOperationException("The checkpoint cannot be recorded.");
        SetupUnscoped();
        SetupStoreManager(cancellationToken);
        MockCheckpointStore
            .Setup(x => x.RecordAppliedSequenceAsync(17L, cancellationToken))
            .Callback(() => Calls.Add("record"))
            .ThrowsAsync(exception)
            .Verifiable();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Checkpoint.RecordAppliedSequenceAsync(17L, cancellationToken)
        );

        Assert.Same(exception, thrown);
        Assert.Equal(["begin", "record", "end"], Calls);
    }

    #endregion

    private void SetupUnscoped()
    {
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Callback(() => Calls.Add("begin"))
            .Returns(MockUnscopedScope.Object)
            .Verifiable();
        MockUnscopedScope.Setup(x => x.Dispose()).Callback(() => Calls.Add("end")).Verifiable();
    }

    private void SetupStoreManager(CancellationToken cancellationToken)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(cancellationToken))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IDirectoryFeedCheckpointStore>())
            .Returns(MockCheckpointStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }
}
