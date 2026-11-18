// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Host.Database.Facades;
using NCode.Identity.OpenId.Host.Database.Logging;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class RuntimeRolePostureCheckTests
{
    private const string ViolationText =
        $"The runtime database role is not subject to row-level security: {RuntimeRolePosture.SuperuserViolation}; "
        + $"{RuntimeRolePosture.BypassesRlsViolation}; {RuntimeRolePosture.OwnsTablesViolation}. "
        + RuntimeRolePosture.Remedy;

    private FakeLogger<RuntimeRolePostureCheck> Logger { get; } = new();

    private RuntimeRolePostureCheck CreateCheck(FakeReader reader, bool localDevelopment) =>
        new(reader, new LocalDevelopmentMode(localDevelopment), Logger);

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_DeploymentViolation_ThrowsAndLogsNothing()
    {
        var check = CreateCheck(new FakeReader(new RuntimeRolePostureRow(true, true, true)), false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartAsync(CancellationToken.None)
        );

        Assert.Equal(ViolationText, exception.Message);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task StartAsync_LocalDevelopmentViolation_LogsOneWarningAndDoesNotThrow()
    {
        var check = CreateCheck(new FakeReader(new RuntimeRolePostureRow(true, true, true)), true);

        await check.StartAsync(CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.RuntimeRolePostureViolated, record.Id.Id);
        Assert.Equal(ViolationText, record.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_MissingRow_ThrowsNotFound(bool localDevelopment)
    {
        var check = CreateCheck(new FakeReader(null), localDevelopment);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartAsync(CancellationToken.None)
        );

        Assert.Equal(
            "The runtime database role was not found in pg_roles, so its row-level security posture cannot be judged.",
            exception.Message
        );
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_CompliantRow_DoesNothing(bool localDevelopment)
    {
        var check = CreateCheck(
            new FakeReader(new RuntimeRolePostureRow(false, false, false)),
            localDevelopment
        );

        await check.StartAsync(CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task StartAsync_ReaderFails_PropagatesTheException()
    {
        var expected = new TimeoutException("The database did not answer.");
        var check = CreateCheck(new FakeReader(null, expected), false);

        var actual = await Assert.ThrowsAsync<TimeoutException>(() =>
            check.StartAsync(CancellationToken.None)
        );

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task StartAsync_WhenCalled_PassesItsTokenToTheReader()
    {
        var reader = new FakeReader(new RuntimeRolePostureRow(false, false, false));
        var check = CreateCheck(reader, false);
        using var cancellationTokenSource = new CancellationTokenSource();

        await check.StartAsync(cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, reader.ReceivedToken);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public void StopAsync_Always_CompletesWithoutReadingThePosture()
    {
        var reader = new FakeReader(
            null,
            new InvalidOperationException("The posture is read only when the host starts.")
        );
        var check = CreateCheck(reader, false);

        var task = check.StopAsync(CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
    }

    #endregion

    private sealed class FakeReader(RuntimeRolePostureRow? row, Exception? exception = null)
        : IRuntimeRolePostureReader
    {
        public CancellationToken ReceivedToken { get; private set; }

        public ValueTask<RuntimeRolePostureRow?> ReadAsync(CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;

            return exception is null
                ? ValueTask.FromResult(row)
                : ValueTask.FromException<RuntimeRolePostureRow?>(exception);
        }
    }
}
