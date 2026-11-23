// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tenants;

public sealed class DefaultTenantPlacerTests : IDisposable
{
    private const string PlatformTeamId = "platform";
    private const string ThisCell = "cell-a";
    private const string TenantId = "acme";
    private const string Issuer = "https://op.example/acme";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);
    private Mock<ITenantDirectory> MockTenantDirectory { get; }

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));

    public DefaultTenantPlacerTests()
    {
        MockTenantDirectory = Mocks.Create<ITenantDirectory>();
    }

    public void Dispose() => Mocks.Verify();

    private DefaultTenantPlacer CreatePlacer(ILogger<DefaultTenantPlacer> logger) =>
        new(
            MockTenantDirectory.Object,
            Options.Create(new CellOptions { Name = ThisCell }),
            Options.Create(new TenantResolutionOptions { Root = { TeamId = PlatformTeamId } }),
            TimeProvider,
            logger
        );

    private void SetupReserve(ReservationResult result, Action<TenantReservation>? callback = null)
    {
        var setup = MockTenantDirectory.Setup(x =>
            x.ReserveAsync(It.IsAny<TenantReservation>(), It.IsAny<CancellationToken>())
        );
        if (callback is not null)
            setup.Callback(
                (TenantReservation reservation, CancellationToken _) => callback(reservation)
            );
        setup.ReturnsAsync(result).Verifiable();
    }

    private void SetupPlace(
        ReservationResult result,
        Action<TenantPlacementRequest>? callback = null
    )
    {
        var setup = MockTenantDirectory.Setup(x =>
            x.PlaceAsync(It.IsAny<TenantPlacementRequest>(), It.IsAny<CancellationToken>())
        );
        if (callback is not null)
            setup.Callback(
                (TenantPlacementRequest placement, CancellationToken _) => callback(placement)
            );
        setup.ReturnsAsync(result).Verifiable();
    }

    private void VerifyNeverReleased() =>
        MockTenantDirectory.Verify(
            x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never()
        );

    private Mock<ILogger<DefaultTenantPlacer>> CreateLoggerExpectingReleaseFailure(
        Exception releaseFailure
    )
    {
        var mockLogger = Mocks.Create<ILogger<DefaultTenantPlacer>>();
        mockLogger.Setup(x => x.IsEnabled(LogLevel.Warning)).Returns(true).Verifiable();
        mockLogger
            .Setup(x =>
                x.Log(
                    LogLevel.Warning,
                    It.Is<EventId>(eventId => eventId.Id == EventIds.TenantCreationReleaseFailed),
                    It.IsAny<It.IsAnyType>(),
                    releaseFailure,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                )
            )
            .Verifiable();
        return mockLogger;
    }

    private Mock<ILogger<DefaultTenantPlacer>> CreateLoggerExpectingAnyReleaseFailure()
    {
        var mockLogger = Mocks.Create<ILogger<DefaultTenantPlacer>>();
        mockLogger.Setup(x => x.IsEnabled(LogLevel.Warning)).Returns(true).Verifiable();
        mockLogger
            .Setup(x =>
                x.Log(
                    LogLevel.Warning,
                    It.Is<EventId>(eventId => eventId.Id == EventIds.TenantCreationReleaseFailed),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                )
            )
            .Verifiable();
        return mockLogger;
    }

    #region ReserveAndPlaceAsync Tests

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenReservedAndPlaced_ReservesThenPlacesInThisCell()
    {
        var calls = new List<string>();
        TenantReservation? reservation = null;
        TenantPlacementRequest? placement = null;
        SetupReserve(
            ReservationResult.Reserved(),
            captured =>
            {
                calls.Add("reserve");
                reservation = captured;
            }
        );
        SetupPlace(
            ReservationResult.Reserved(),
            captured =>
            {
                calls.Add("place");
                placement = captured;
            }
        );
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            Issuer,
            "team-2",
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.Equal(["reserve", "place"], calls);
        Assert.NotNull(reservation);
        Assert.Equal(TenantId, reservation.TenantId);
        Assert.Equal(Issuer, reservation.Issuer);
        Assert.Equal("team-2", reservation.TeamId);
        Assert.Equal(ThisCell, reservation.Cell);
        Assert.NotNull(placement);
        Assert.Equal(TenantId, placement.TenantId);
        Assert.Equal(Issuer, placement.Issuer);
        Assert.Equal(ThisCell, placement.Cell);
        VerifyNeverReleased();
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheTeamIsThePlatformsOwnInAnotherCase_ReservesTheTenantInNoTeam()
    {
        TenantReservation? reservation = null;
        SetupReserve(ReservationResult.Reserved(), captured => reservation = captured);
        SetupPlace(ReservationResult.Reserved());
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            Issuer,
            teamId: "PLATFORM",
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.NotNull(reservation);
        Assert.Null(reservation.TeamId);
        VerifyNeverReleased();
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheTenantHasNoIssuer_ReservesAndPlacesItsIdentifierAlone()
    {
        TenantReservation? reservation = null;
        TenantPlacementRequest? placement = null;
        SetupReserve(ReservationResult.Reserved(), captured => reservation = captured);
        SetupPlace(ReservationResult.Reserved(), captured => placement = captured);
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            issuer: null,
            "team-2",
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.NotNull(reservation);
        Assert.Null(reservation.Issuer);
        Assert.NotNull(placement);
        Assert.Null(placement.Issuer);
        VerifyNeverReleased();
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheReservationIsRefused_ReturnsTheRefusalWithoutPlacingOrReleasing()
    {
        var conflict = ReservationResult.Conflict("the issuer is held");
        SetupReserve(conflict);
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            Issuer,
            "team-2",
            CancellationToken.None
        );

        Assert.Same(conflict, result);
        MockTenantDirectory.Verify(
            x => x.PlaceAsync(It.IsAny<TenantPlacementRequest>(), It.IsAny<CancellationToken>()),
            Times.Never()
        );
        VerifyNeverReleased();
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheReservationThrows_RethrowsWithoutReleasing()
    {
        var failure = new DirectoryUnavailableException("The directory is unreachable.");
        MockTenantDirectory
            .Setup(x =>
                x.ReserveAsync(It.IsAny<TenantReservation>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(failure)
            .Verifiable();
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await placer.ReserveAndPlaceAsync(TenantId, Issuer, "team-2", CancellationToken.None)
        );

        Assert.Same(failure, exception);
        VerifyNeverReleased();
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenThePlacementIsRefused_ReleasesTheReservationAndReturnsTheRefusal()
    {
        var conflict = ReservationResult.Conflict("the cell is full");
        SetupReserve(ReservationResult.Reserved());
        SetupPlace(conflict);
        var releaseToken = CancellationToken.None;
        MockTenantDirectory
            .Setup(x => x.ReleaseAsync(TenantId, It.IsAny<CancellationToken>()))
            .Callback((string _, CancellationToken token) => releaseToken = token)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            Issuer,
            "team-2",
            CancellationToken.None
        );

        Assert.Same(conflict, result);
        Assert.True(releaseToken.CanBeCanceled);
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenThePlacementThrows_ReleasesTheReservationAndRethrows()
    {
        SetupReserve(ReservationResult.Reserved());
        var failure = new DirectoryUnavailableException("The placement is unreachable.");
        MockTenantDirectory
            .Setup(x =>
                x.PlaceAsync(It.IsAny<TenantPlacementRequest>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(failure)
            .Verifiable();
        MockTenantDirectory
            .Setup(x => x.ReleaseAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var placer = CreatePlacer(NullLogger<DefaultTenantPlacer>.Instance);

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await placer.ReserveAndPlaceAsync(TenantId, Issuer, "team-2", CancellationToken.None)
        );

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheReleaseFails_LogsTheFailureAndReturnsThePlacementsRefusal()
    {
        var conflict = ReservationResult.Conflict("the cell is full");
        SetupReserve(ReservationResult.Reserved());
        SetupPlace(conflict);
        var releaseFailure = new InvalidOperationException("The release failed.");
        MockTenantDirectory
            .Setup(x => x.ReleaseAsync(TenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(releaseFailure)
            .Verifiable();
        var mockLogger = CreateLoggerExpectingReleaseFailure(releaseFailure);
        var placer = CreatePlacer(mockLogger.Object);

        var result = await placer.ReserveAndPlaceAsync(
            TenantId,
            Issuer,
            "team-2",
            CancellationToken.None
        );

        Assert.Same(conflict, result);
    }

    [Fact]
    public async Task ReserveAndPlaceAsync_WhenTheReleaseHangs_LogsTheTimeoutAndReturnsTheRefusalOnceTheReleasesBoundElapses()
    {
        var conflict = ReservationResult.Conflict("the cell is full");
        SetupReserve(ReservationResult.Reserved());
        SetupPlace(conflict);
        var releaseStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        MockTenantDirectory
            .Setup(x => x.ReleaseAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(
                async (string _, CancellationToken token) =>
                {
                    releaseStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            )
            .Verifiable();
        var mockLogger = CreateLoggerExpectingAnyReleaseFailure();
        var placer = CreatePlacer(mockLogger.Object);

        var creation = placer
            .ReserveAndPlaceAsync(TenantId, Issuer, "team-2", CancellationToken.None)
            .AsTask();
        await releaseStarted.Task;
        TimeProvider.Advance(PostCommitWork.Timeout);
        var result = await creation;

        Assert.Same(conflict, result);
    }

    #endregion
}
