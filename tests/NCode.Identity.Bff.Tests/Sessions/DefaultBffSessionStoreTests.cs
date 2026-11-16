// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Buffers;
using NCode.Identity.Bff.Sessions;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.Bff.Tests.Sessions;

public class DefaultBffSessionStoreTests : BaseTests
{
    private const string StoreKey = "store-key";

    private static DateTimeOffset Absolute { get; } = new(2026, 1, 2, 11, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Idle { get; } = new(2026, 1, 2, 4, 0, 0, TimeSpan.Zero);
    private static TimeSpan WaitTimeout { get; } = TimeSpan.FromSeconds(30);

    private Mock<IExpiringValueSet> MockValues { get; }
    private Mock<IExpiringIndexSet> MockIndexes { get; }
    private DefaultBffSessionStore Store { get; }

    public DefaultBffSessionStoreTests()
    {
        MockValues = CreateStrictMock<IExpiringValueSet>();
        MockIndexes = CreateStrictMock<IExpiringIndexSet>();
        Store = new DefaultBffSessionStore(MockValues.Object, MockIndexes.Object);
    }

    private static BffSessionMetadata CreateMetadata(
        string? sessionId = "sid-1",
        BffSessionClient? client = null,
        DateTimeOffset? absoluteExpiresAt = null
    ) =>
        new()
        {
            Frontend = client?.Frontend ?? "default",
            Authority = client?.Authority ?? "https://op.example",
            ClientId = client?.ClientId ?? "client-1",
            Issuer = "https://op.example",
            Subject = "sub-1",
            SessionId = sessionId,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero),
            LastAccessedAt = new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero),
            AbsoluteExpiresAt = absoluteExpiresAt ?? Absolute,
            IdleExpiresAt = Idle,
        };

    private static BffSessionRecord CreateRecord(string? sessionId = "sid-1") =>
        new(StoreKey, 1, CreateMetadata(sessionId), new byte[] { 1, 2, 3 });

    private static BffSessionClient CreateClient() =>
        new("default", "https://op.example", "client-1");

    private static ExpiringValue CreateStoredValue(BffSessionClient client)
    {
        using var lease = BffSessionRecordSerializer.Serialize(
            CreateMetadata(client: client),
            new byte[] { 1 },
            out var value
        );
        return new ExpiringValue(value.ToArray(), 1);
    }

    private void SetupReadableClient(BffSessionClient client, string storeKey)
    {
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None))
            .ReturnsAsync([storeKey])
            .Verifiable(Times.Once);
        MockValues
            .Setup(x => x.GetAsync("bff:session:" + storeKey, CancellationToken.None))
            .ReturnsAsync(CreateStoredValue(client))
            .Verifiable(Times.Once);
    }

    private void SetupClientIndexAdds()
    {
        var client = CreateClient();
        MockIndexes
            .Setup(x =>
                x.AddMemberAsync(
                    BffStoreKeys.ClientIndex(client),
                    StoreKey,
                    Absolute,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        MockIndexes
            .Setup(x =>
                x.AddMemberAsync(
                    BffStoreKeys.ClientsIndex,
                    "2026-01-02:" + BffStoreKeys.ClientDigest(client),
                    new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
    }

    private void SetupClientIndexMembers(BffSessionClient client, IReadOnlyList<string> storeKeys)
    {
        IReadOnlyList<string> members = ["2026-01-02:" + BffStoreKeys.ClientDigest(client)];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable(Times.Once);
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None))
            .ReturnsAsync(storeKeys)
            .Verifiable(Times.Once);
    }

    private static async ValueTask<ExpiringValue?> ReadAfterReleaseAsync(
        Task release,
        ExpiringValue? value,
        CancellationToken cancellationToken
    )
    {
        await release.WaitAsync(cancellationToken);
        return value;
    }

    #region TryCreateAsync Tests

    [Fact]
    public async Task TryCreateAsync_Added_AddsTheValueAndBothIndexMembers()
    {
        var metadata = CreateMetadata();
        var sidIndex = BffStoreKeys.SessionIdIndex(metadata.Issuer, "sid-1");
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);

        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:session:" + StoreKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(sidIndex, StoreKey, Absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(subIndex, StoreKey, Absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupClientIndexAdds();

        var result = await Store.TryCreateAsync(CreateRecord(), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryCreateAsync_Always_IndexesTheSessionByItsClient()
    {
        var metadata = CreateMetadata();
        var sidIndex = BffStoreKeys.SessionIdIndex(metadata.Issuer, "sid-1");
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);

        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:session:" + StoreKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(sidIndex, StoreKey, Absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(subIndex, StoreKey, Absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupClientIndexAdds();

        await Store.TryCreateAsync(CreateRecord(), CancellationToken.None);
    }

    [Fact]
    public async Task TryCreateAsync_WhenCalled_AddsTheClientDigestMemberToTheClientsIndex()
    {
        var client = CreateClient();
        var absolute = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.FromHours(-5));
        var metadata = CreateMetadata(null, absoluteExpiresAt: absolute);
        var record = new BffSessionRecord(StoreKey, 1, metadata, new byte[] { 1, 2, 3 });
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);

        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:session:" + StoreKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(subIndex, StoreKey, absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x =>
                x.AddMemberAsync(
                    BffStoreKeys.ClientIndex(client),
                    StoreKey,
                    absolute,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x =>
                x.AddMemberAsync(
                    BffStoreKeys.ClientsIndex,
                    "2026-01-03:" + BffStoreKeys.ClientDigest(client),
                    new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        var result = await Store.TryCreateAsync(record, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryCreateAsync_NoSessionId_AddsOnlyTheSubjectMember()
    {
        var metadata = CreateMetadata(null);
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);

        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:session:" + StoreKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockIndexes
            .Setup(x => x.AddMemberAsync(subIndex, StoreKey, Absolute, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupClientIndexAdds();

        var result = await Store.TryCreateAsync(CreateRecord(null), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryCreateAsync_KeyExists_ReturnsFalseWithoutIndexing()
    {
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:session:" + StoreKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        var result = await Store.TryCreateAsync(CreateRecord(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryCreateAsync_AdapterThrows_ThrowsStoreUnavailable()
    {
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    CancellationToken.None
                )
            )
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        var exception = await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.TryCreateAsync(CreateRecord(), CancellationToken.None)
        );

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task TryCreateAsync_Canceled_PropagatesTheCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();
        var cancellation = new OperationCanceledException(cancellationToken);
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    cancellationToken
                )
            )
            .ThrowsAsync(cancellation)
            .Verifiable();
        var record = CreateRecord();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Store.TryCreateAsync(record, cancellationToken)
        );
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_Present_ReturnsTheRecordWithTheStoredVersion()
    {
        using var lease = BffSessionRecordSerializer.Serialize(
            CreateMetadata(),
            new byte[] { 9, 8 },
            out var value
        );
        MockValues
            .Setup(x => x.GetAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(value.ToArray(), 4))
            .Verifiable();

        var record = await Store.GetAsync(StoreKey, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(StoreKey, record.StoreKey);
        Assert.Equal(4, record.Version);
        Assert.Equal("sub-1", record.Metadata.Subject);
        Assert.Equal(new byte[] { 9, 8 }, record.ProtectedPayload.ToArray());
    }

    [Fact]
    public async Task GetAsync_Absent_ReturnsNull()
    {
        MockValues
            .Setup(x => x.GetAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();

        var record = await Store.GetAsync(StoreKey, CancellationToken.None);

        Assert.Null(record);
    }

    [Fact]
    public async Task GetAsync_Malformed_ReturnsNull()
    {
        MockValues
            .Setup(x => x.GetAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(new byte[] { 2, 0 }, 1))
            .Verifiable();

        var record = await Store.GetAsync(StoreKey, CancellationToken.None);

        Assert.Null(record);
    }

    #endregion

    #region TryReplaceAsync Tests

    [Fact]
    public async Task TryReplaceAsync_Replaced_UsesTheMinimumExpiry()
    {
        MockValues
            .Setup(x =>
                x.TryReplaceAsync(
                    "bff:session:" + StoreKey,
                    3,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var result = await Store.TryReplaceAsync(CreateRecord(), 3, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryReplaceAsync_Conflict_ReturnsFalse()
    {
        MockValues
            .Setup(x =>
                x.TryReplaceAsync(
                    "bff:session:" + StoreKey,
                    3,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        var result = await Store.TryReplaceAsync(CreateRecord(), 3, CancellationToken.None);

        Assert.False(result);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_WithSessionId_RemovesTheValueAndBothMembers()
    {
        var metadata = CreateMetadata();
        var sidIndex = BffStoreKeys.SessionIdIndex(metadata.Issuer, "sid-1");
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);

        MockValues
            .Setup(x => x.TryTakeAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(new byte[] { 1 }, 1))
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(sidIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(subIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x =>
                x.RemoveMemberAsync(
                    BffStoreKeys.ClientIndex(CreateClient()),
                    StoreKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var removed = await Store.RemoveAsync(StoreKey, metadata, CancellationToken.None);

        Assert.True(removed);
    }

    [Fact]
    public async Task RemoveAsync_AlreadyGone_RemovesTheMembersAndReturnsFalse()
    {
        var metadata = CreateMetadata(null);
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);
        var clientIndex = BffStoreKeys.ClientIndex(CreateClient());

        MockValues
            .Setup(x => x.TryTakeAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(subIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(clientIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var removed = await Store.RemoveAsync(StoreKey, metadata, CancellationToken.None);

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveAsync_Always_RemovesTheClientIndexMember()
    {
        var metadata = CreateMetadata(null);
        var subIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);
        var clientIndex = BffStoreKeys.ClientIndex(CreateClient());

        MockValues
            .Setup(x => x.TryTakeAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(new byte[] { 1 }, 1))
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(subIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockIndexes
            .Setup(x => x.RemoveMemberAsync(clientIndex, StoreKey, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Store.RemoveAsync(StoreKey, metadata, CancellationToken.None);
    }

    [Fact]
    public async Task RemoveAsync_AdapterThrows_ThrowsStoreUnavailable()
    {
        MockValues
            .Setup(x => x.TryTakeAsync("bff:session:" + StoreKey, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.RemoveAsync(StoreKey, CreateMetadata(), CancellationToken.None)
        );
    }

    #endregion

    #region Lookup Tests

    [Fact]
    public async Task FindBySessionIdAsync_Called_ReturnsTheIndexMembers()
    {
        var indexKey = BffStoreKeys.SessionIdIndex("iss", "sid-1");
        IReadOnlyList<string> members = ["a", "b"];
        MockIndexes
            .Setup(x => x.GetMembersAsync(indexKey, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable();

        var result = await Store.FindBySessionIdAsync("iss", "sid-1", CancellationToken.None);

        Assert.Equal(members, result);
    }

    [Fact]
    public async Task FindBySubjectAsync_Called_ReturnsTheIndexMembers()
    {
        var indexKey = BffStoreKeys.SubjectIndex("iss", "sub-1");
        IReadOnlyList<string> members = ["a"];
        MockIndexes
            .Setup(x => x.GetMembersAsync(indexKey, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable();

        var result = await Store.FindBySubjectAsync("iss", "sub-1", CancellationToken.None);

        Assert.Equal(members, result);
    }

    [Fact]
    public async Task FindBySubjectAsync_AdapterThrows_ThrowsStoreUnavailable()
    {
        MockIndexes
            .Setup(x => x.GetMembersAsync(It.IsAny<string>(), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.FindBySubjectAsync("iss", "sub-1", CancellationToken.None)
        );
    }

    [Fact]
    public async Task FindClientsAsync_WhenMembersShareADigest_ReturnsTheClientOnce()
    {
        var client = CreateClient();
        var other = new BffSessionClient("other", "https://op.example", "client-2");
        var clientDigest = BffStoreKeys.ClientDigest(client);
        var otherDigest = BffStoreKeys.ClientDigest(other);
        IReadOnlyList<string> members =
        [
            "2026-01-02:" + clientDigest,
            "2026-01-03:" + clientDigest,
            "2026-01-02:" + otherDigest,
        ];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable(Times.Once);
        SetupReadableClient(client, "store-1");
        SetupReadableClient(other, "store-2");

        var result = await Store.FindClientsAsync([], CancellationToken.None);

        Assert.Equal([client, other], result);
        MockIndexes.Verify(
            x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None),
            Times.Once
        );
    }

    [Fact]
    public async Task FindClientsAsync_MemberWithoutReadableRecord_SkipsItWithoutRemoving()
    {
        var client = CreateClient();
        var unreadable = new BffSessionClient("other", "https://op.example", "client-2");
        var unreadableDigest = BffStoreKeys.ClientDigest(unreadable);
        IReadOnlyList<string> members =
        [
            "malformed",
            "2026-01-02:",
            "not-a-day:" + unreadableDigest,
            "2026-01-02:" + unreadableDigest,
            "2026-01-02:" + BffStoreKeys.ClientDigest(client),
        ];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable(Times.Once);
        MockIndexes
            .Setup(x =>
                x.GetMembersAsync(BffStoreKeys.ClientIndex(unreadable), CancellationToken.None)
            )
            .ReturnsAsync(["gone"])
            .Verifiable(Times.Once);
        MockValues
            .Setup(x => x.GetAsync("bff:session:gone", CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable(Times.Once);
        SetupReadableClient(client, "store-1");

        var result = await Store.FindClientsAsync([], CancellationToken.None);

        Assert.Equal([client], result);
        MockIndexes.Verify(
            x =>
                x.RemoveMemberAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task FindClientsAsync_MemberOfAnExceptedClient_SkipsItWithoutReadingItsIndex()
    {
        var client = CreateClient();
        var other = new BffSessionClient("other", "https://op.example", "client-2");
        IReadOnlyList<string> members =
        [
            "2026-01-02:" + BffStoreKeys.ClientDigest(client),
            "2026-01-02:" + BffStoreKeys.ClientDigest(other),
        ];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable(Times.Once);
        SetupReadableClient(other, "store-2");

        var result = await Store.FindClientsAsync([client], CancellationToken.None);

        Assert.Equal([other], result);
    }

    [Fact]
    public async Task FindClientsAsync_RecordOfAnotherClient_SkipsTheDigest()
    {
        var client = CreateClient();
        var other = new BffSessionClient("other", "https://op.example", "client-2");
        IReadOnlyList<string> members = ["2026-01-02:" + BffStoreKeys.ClientDigest(client)];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable(Times.Once);
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None))
            .ReturnsAsync(["store-1"])
            .Verifiable(Times.Once);
        MockValues
            .Setup(x => x.GetAsync("bff:session:store-1", CancellationToken.None))
            .ReturnsAsync(CreateStoredValue(other))
            .Verifiable(Times.Once);

        var result = await Store.FindClientsAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindClientsAsync_StoreFails_ThrowsBffStoreUnavailableException()
    {
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientsIndex, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.FindClientsAsync([], CancellationToken.None)
        );
    }

    [Fact]
    public async Task FindClientsAsync_WhenAnIndexListsMoreThanEightSessions_ReadsEightRecordsAtOnce()
    {
        var client = CreateClient();
        IReadOnlyList<string> storeKeys =
        [
            "k0",
            "k1",
            "k2",
            "k3",
            "k4",
            "k5",
            "k6",
            "k7",
            "k8",
            "k9",
        ];
        SetupClientIndexMembers(client, storeKeys);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var storeKey in storeKeys)
        {
            var key = "bff:session:" + storeKey;
            ExpiringValue? value = storeKey == "k9" ? CreateStoredValue(client) : null;
            MockValues
                .Setup(x => x.GetAsync(key, CancellationToken.None))
                .Returns(
                    (string _, CancellationToken cancellationToken) =>
                        ReadAfterReleaseAsync(release.Task, value, cancellationToken)
                )
                .Verifiable(Times.Once);
        }

        var pending = Store.FindClientsAsync([], CancellationToken.None).AsTask();

        MockValues.Verify(
            x => x.GetAsync(It.IsAny<string>(), CancellationToken.None),
            Times.Exactly(8)
        );
        Assert.False(pending.IsCompleted);

        release.SetResult();
        var result = await pending.WaitAsync(WaitTimeout);

        Assert.Equal([client], result);
    }

    [Fact]
    public async Task FindClientsAsync_WhenARoundHoldsAReadableRecord_ReadsNoLaterRound()
    {
        var client = CreateClient();
        IReadOnlyList<string> storeKeys =
        [
            "k0",
            "k1",
            "k2",
            "k3",
            "k4",
            "k5",
            "k6",
            "k7",
            "k8",
            "k9",
            "k10",
            "k11",
        ];
        SetupClientIndexMembers(client, storeKeys);
        foreach (var storeKey in storeKeys.Take(8))
        {
            var key = "bff:session:" + storeKey;
            ExpiringValue? value = storeKey == "k3" ? CreateStoredValue(client) : null;
            MockValues
                .Setup(x => x.GetAsync(key, CancellationToken.None))
                .ReturnsAsync(value)
                .Verifiable(Times.Once);
        }

        var result = await Store.FindClientsAsync([], CancellationToken.None);

        Assert.Equal([client], result);
    }

    [Fact]
    public async Task FindClientsAsync_WhenOneReadOfARoundFails_ThrowsBffStoreUnavailableException()
    {
        SetupClientIndexMembers(CreateClient(), ["k0", "k1"]);
        MockValues
            .Setup(x => x.GetAsync("bff:session:k0", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable(Times.Once);
        MockValues
            .Setup(x => x.GetAsync("bff:session:k1", CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.FindClientsAsync([], CancellationToken.None)
        );
    }

    [Fact]
    public async Task FindByClientAsync_Always_ReturnsTheIndexMembers()
    {
        var client = CreateClient();
        IReadOnlyList<string> members = ["a", "b"];
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None))
            .ReturnsAsync(members)
            .Verifiable();

        var result = await Store.FindByClientAsync(client, CancellationToken.None);

        Assert.Equal(members, result);
    }

    [Fact]
    public async Task FindByClientAsync_StoreFails_ThrowsBffStoreUnavailableException()
    {
        var client = CreateClient();
        MockIndexes
            .Setup(x => x.GetMembersAsync(BffStoreKeys.ClientIndex(client), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.FindByClientAsync(client, CancellationToken.None)
        );
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task TryAddTransactionAsync_Called_AddsUnderTheTransactionKey()
    {
        var bytes = new byte[] { 1, 2 };
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:txn:t1",
                    It.Is<ReadOnlyMemory<byte>>(v => v.ToArray().SequenceEqual(bytes)),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var result = await Store.TryAddTransactionAsync("t1", bytes, Idle, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryTakeTransactionAsync_Present_ReturnsTheValue()
    {
        MockValues
            .Setup(x => x.TryTakeAsync("bff:txn:t1", CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(new byte[] { 5, 6 }, 1))
            .Verifiable();

        var result = await Store.TryTakeTransactionAsync("t1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new byte[] { 5, 6 }, result.Value.ToArray());
    }

    [Fact]
    public async Task TryTakeTransactionAsync_Absent_ReturnsNull()
    {
        MockValues
            .Setup(x => x.TryTakeAsync("bff:txn:t1", CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();

        var result = await Store.TryTakeTransactionAsync("t1", CancellationToken.None);

        Assert.Null(result);
    }

    #endregion

    #region Lease Tests

    [Fact]
    public async Task TryAcquireRefreshLeaseAsync_WhenAdded_ReturnsTheLeaseWithTheStoredOwner()
    {
        byte[]? stored = null;
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:lease:l1",
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .Callback(
                (string _, ReadOnlyMemory<byte> value, DateTimeOffset _, CancellationToken _) =>
                    stored = value.ToArray()
            )
            .ReturnsAsync(true)
            .Verifiable();

        var result = await Store.TryAcquireRefreshLeaseAsync("l1", Idle, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("l1", result.Value.LeaseKey);
        Assert.Equal(43, result.Value.Owner.Length);
        Assert.Equal(SecureEncoding.UTF8.GetBytes(result.Value.Owner), stored);
    }

    [Fact]
    public async Task TryAcquireRefreshLeaseAsync_WhenHeld_ReturnsNull()
    {
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:lease:l1",
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        var result = await Store.TryAcquireRefreshLeaseAsync("l1", Idle, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryAcquireRefreshLeaseAsync_CalledTwice_UsesDistinctOwners()
    {
        MockValues
            .Setup(x =>
                x.TryAddAsync(
                    "bff:lease:l1",
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Idle,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var first = await Store.TryAcquireRefreshLeaseAsync("l1", Idle, CancellationToken.None);
        var second = await Store.TryAcquireRefreshLeaseAsync("l1", Idle, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Value.Owner, second.Value.Owner);
    }

    [Fact]
    public async Task IsRefreshLeaseHeldAsync_Present_ReturnsTrue()
    {
        MockValues
            .Setup(x => x.GetAsync("bff:lease:l1", CancellationToken.None))
            .ReturnsAsync(new ExpiringValue(new byte[] { 0x31 }, 1))
            .Verifiable();

        var result = await Store.IsRefreshLeaseHeldAsync("l1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsRefreshLeaseHeldAsync_Absent_ReturnsFalse()
    {
        MockValues
            .Setup(x => x.GetAsync("bff:lease:l1", CancellationToken.None))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();

        var result = await Store.IsRefreshLeaseHeldAsync("l1", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ReleaseRefreshLeaseAsync_Called_RemovesOnlyTheOwnersValue()
    {
        var expected = "owner-1"u8.ToArray();
        MockValues
            .Setup(x =>
                x.TryRemoveAsync(
                    "bff:lease:l1",
                    It.Is<ReadOnlyMemory<byte>>(v => v.ToArray().SequenceEqual(expected)),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        await Store.ReleaseRefreshLeaseAsync(
            new BffRefreshLease("l1", "owner-1"),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ReleaseRefreshLeaseAsync_WhenTheStoreTimesOut_ThrowsStoreUnavailable()
    {
        var timeout = new TaskCanceledException("timed out");
        MockValues
            .Setup(x =>
                x.TryRemoveAsync(
                    "bff:lease:l1",
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    CancellationToken.None
                )
            )
            .ThrowsAsync(timeout)
            .Verifiable();
        var lease = new BffRefreshLease("l1", "owner-1");

        var exception = await Assert.ThrowsAsync<BffStoreUnavailableException>(async () =>
            await Store.ReleaseRefreshLeaseAsync(lease, CancellationToken.None)
        );

        Assert.Same(timeout, exception.InnerException);
    }

    [Fact]
    public async Task ReleaseRefreshLeaseAsync_WhenTheCallerCancels_ThrowsOperationCanceled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();
        var cancellation = new OperationCanceledException(cancellationToken);
        MockValues
            .Setup(x =>
                x.TryRemoveAsync(
                    "bff:lease:l1",
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    cancellationToken
                )
            )
            .ThrowsAsync(cancellation)
            .Verifiable();
        var lease = new BffRefreshLease("l1", "owner-1");

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Store.ReleaseRefreshLeaseAsync(lease, cancellationToken)
        );

        Assert.Same(cancellation, exception);
    }

    #endregion

    #region BffStoreKeys Tests

    [Fact]
    public void SessionIdIndex_DifferentSplit_ReturnsDifferentKeys()
    {
        var first = BffStoreKeys.SessionIdIndex("ab", "c");
        var second = BffStoreKeys.SessionIdIndex("a", "bc");

        Assert.NotEqual(first, second);
        Assert.StartsWith("bff:sid:", first);
        Assert.StartsWith("bff:sub:", BffStoreKeys.SubjectIndex("ab", "c"));
    }

    #endregion
}
