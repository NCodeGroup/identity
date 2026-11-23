// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Passwords;

public sealed class DefaultPasswordAttemptLimiterTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private const string ConnectionId = "connection-1";

    private const string UserName = "alice";

    private static DateTimeOffset Now { get; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private Mock<IExpiringCounterSet> MockCounterSet { get; }

    private FakeTimeProvider TimeProvider { get; } = new(Now);

    private FakeLogger<DefaultPasswordAttemptLimiter> Logger { get; } = new();

    private DefaultPasswordAttemptLimiter Limiter { get; }

    public DefaultPasswordAttemptLimiterTests()
    {
        MockCounterSet = CreateStrictMock<IExpiringCounterSet>();
        Limiter = new DefaultPasswordAttemptLimiter(MockCounterSet.Object, TimeProvider, Logger);
    }

    #region Scaffolding

    private OpenIdContext CreateContext()
    {
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();

        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();

        return mockContext.Object;
    }

    private ResolvedConnection CreateConnection(Mock<IReadOnlySettingCollection> mockSettings) =>
        new()
        {
            Connection = new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                Name = "local",
                Strategy = ConnectionStrategyNames.Local,
                DisplayName = "Local",
                IsSystem = false,
                IsEnabled = true,
                Options = JsonElements.EmptyObject,
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = ConnectionId,
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = CreateLooseMock<IConnectionStrategy>().Object,
            Settings = mockSettings.Object,
        };

    private ResolvedConnection CreateConnectionWithLimit(int limit)
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordFailedAttemptsLimit))
            .Returns(limit)
            .Verifiable();

        return CreateConnection(mockSettings);
    }

    private ResolvedConnection CreateConnectionWithWindow(TimeSpan window)
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordFailedAttemptsWindow))
            .Returns(window)
            .Verifiable();

        return CreateConnection(mockSettings);
    }

    #endregion

    #region IsLimitReachedAsync Tests

    [Fact]
    public async Task IsLimitReachedAsync_WhenBelowTheLimit_ReturnsFalse()
    {
        var context = CreateContext();
        var connection = CreateConnectionWithLimit(3);
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        MockCounterSet
            .Setup(x => x.GetAsync(key, CancellationToken.None))
            .ReturnsAsync(2)
            .Verifiable();

        var result = await Limiter.IsLimitReachedAsync(
            context,
            connection,
            UserName,
            CancellationToken.None
        );

        Assert.False(result);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task IsLimitReachedAsync_WhenTheLimitIsReached_ReturnsTrueAndLogs()
    {
        var context = CreateContext();
        var connection = CreateConnectionWithLimit(3);
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        MockCounterSet
            .Setup(x => x.GetAsync(key, CancellationToken.None))
            .ReturnsAsync(3)
            .Verifiable();

        var result = await Limiter.IsLimitReachedAsync(
            context,
            connection,
            UserName,
            CancellationToken.None
        );

        Assert.True(result);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(EventIds.PasswordAttemptLimitReached, record.Id.Id);
    }

    [Fact]
    public async Task IsLimitReachedAsync_WhenTheCounterStoreFails_Throws()
    {
        var context = CreateContext();
        var connection = CreateConnectionWithLimit(3);
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        MockCounterSet
            .Setup(x => x.GetAsync(key, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("The counter store is down."))
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Limiter.IsLimitReachedAsync(context, connection, UserName, CancellationToken.None)
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    #endregion

    #region RecordFailureAsync Tests

    [Fact]
    public async Task RecordFailureAsync_IncrementsTheCounterUntilTheWindowEnds()
    {
        var window = TimeSpan.FromMinutes(15);
        var context = CreateContext();
        var connection = CreateConnectionWithWindow(window);
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        var expiresWhen = Now + window;
        MockCounterSet
            .Setup(x => x.IncrementAsync(key, expiresWhen, CancellationToken.None))
            .ReturnsAsync(1)
            .Verifiable();

        await Limiter.RecordFailureAsync(context, connection, UserName, CancellationToken.None);
    }

    [Fact]
    public async Task RecordFailureAsync_WhenTheCounterStoreFails_Throws()
    {
        var window = TimeSpan.FromMinutes(15);
        var context = CreateContext();
        var connection = CreateConnectionWithWindow(window);
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        var expiresWhen = Now + window;
        MockCounterSet
            .Setup(x => x.IncrementAsync(key, expiresWhen, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("The counter store is down."))
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Limiter.RecordFailureAsync(context, connection, UserName, CancellationToken.None)
        );
    }

    #endregion

    #region ResetAsync Tests

    [Fact]
    public async Task ResetAsync_RemovesTheCounter()
    {
        var key = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, UserName);
        MockCounterSet
            .Setup(x => x.RemoveAsync(key, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Limiter.ResetAsync(TenantId, ConnectionId, UserName, CancellationToken.None);
    }

    #endregion

    #region CreateKey Tests

    [Theory]
    [InlineData("alice", "ALICE")]
    [InlineData("\u212Aate", "kate")]
    [InlineData("\u017Fam", "sam")]
    [InlineData("jose\u0301", "jos\u00E9")]
    public void CreateKey_FoldsEverySpellingOfOneName(string first, string second)
    {
        var firstKey = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, first);
        var secondKey = DefaultPasswordAttemptLimiter.CreateKey(TenantId, ConnectionId, second);

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void CreateKey_SeparatesTenants()
    {
        var firstKey = DefaultPasswordAttemptLimiter.CreateKey("a", ConnectionId, UserName);
        var secondKey = DefaultPasswordAttemptLimiter.CreateKey("b", ConnectionId, UserName);

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void CreateKey_SeparatesConnections()
    {
        var firstKey = DefaultPasswordAttemptLimiter.CreateKey(TenantId, "connection-a", UserName);
        var secondKey = DefaultPasswordAttemptLimiter.CreateKey(TenantId, "connection-b", UserName);

        Assert.NotEqual(firstKey, secondKey);
    }

    #endregion
}
