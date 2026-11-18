// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class IsolatedStepTests : BaseTests
{
    private List<Exception> Failures { get; } = [];

    private ValueTask RunAsync(Func<ValueTask> step, CancellationToken cancellationToken) =>
        IsolatedStep.RunAsync(step, Failures.Add, cancellationToken);

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_WhenTheStepSucceeds_RunsItAndReportsNoFailure()
    {
        var isRun = false;

        await RunAsync(
            () =>
            {
                isRun = true;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.True(isRun);
        Assert.Empty(Failures);
    }

    [Fact]
    public async Task RunAsync_WhenTheStepFails_ReportsTheFailure()
    {
        var exception = new InvalidOperationException("The step is unavailable.");

        await RunAsync(() => ValueTask.FromException(exception), CancellationToken.None);

        Assert.Same(exception, Assert.Single(Failures));
    }

    [Fact]
    public async Task RunAsync_WhenTheStepFailsAndTheCallersTokenIsCancelled_ReportsTheFailure()
    {
        using var cts = new CancellationTokenSource();
        var exception = new InvalidOperationException("The step is unavailable.");

        await cts.CancelAsync();
        await RunAsync(() => ValueTask.FromException(exception), cts.Token);

        Assert.Same(exception, Assert.Single(Failures));
    }

    [Fact]
    public async Task RunAsync_WhenTheStepIsCancelledAndTheCallersTokenIsNotCancelled_ReportsTheFailure()
    {
        var exception = new OperationCanceledException();

        await RunAsync(() => ValueTask.FromException(exception), CancellationToken.None);

        Assert.Same(exception, Assert.Single(Failures));
    }

    [Fact]
    public async Task RunAsync_WhenTheStepIsCancelledAndTheCallersTokenIsCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var exception = new OperationCanceledException(cts.Token);

        await cts.CancelAsync();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RunAsync(() => ValueTask.FromException(exception), cts.Token)
        );

        Assert.Same(exception, thrown);
        Assert.Empty(Failures);
    }

    #endregion
}
