// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Teams;
using NCode.Mediator;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class PostCommitCommandSenderTests : BaseTests
{
    private static TeamChangedCommand Command { get; } = new("team-1", true);

    private Mock<IMediator> MockMediator { get; }
    private FakeTimeProvider TimeProvider { get; } = new();
    private ServiceProvider ServiceProvider { get; }
    private List<Exception> Failures { get; } = [];

    public PostCommitCommandSenderTests()
    {
        MockMediator = CreateStrictMock<IMediator>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => MockMediator.Object);
        ServiceProvider = serviceCollection.BuildServiceProvider();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServiceProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    private ValueTask SendAsync(CancellationToken cancellationToken) =>
        PostCommitCommandSender.SendAsync(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider,
            Command,
            Failures.Add,
            cancellationToken
        );

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WhenTheCommandSucceeds_SendsItAndReportsNoFailure()
    {
        MockMediator
            .Setup(x => x.SendAsync(Command, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await SendAsync(CancellationToken.None);

        Assert.Empty(Failures);
    }

    [Fact]
    public async Task SendAsync_WhenTheCommandFails_ReportsTheFailure()
    {
        var exception = new InvalidOperationException("The mediator is unavailable.");
        MockMediator
            .Setup(x => x.SendAsync(Command, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception)
            .Verifiable();

        await SendAsync(CancellationToken.None);

        Assert.Same(exception, Assert.Single(Failures));
    }

    [Fact]
    public async Task SendAsync_WhenTheCommandIsCancelledAndTheCallersTokenIsCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        MockMediator
            .Setup(x => x.SendAsync(Command, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException())
            .Verifiable();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await SendAsync(cts.Token)
        );

        Assert.Empty(Failures);
    }

    #endregion
}
