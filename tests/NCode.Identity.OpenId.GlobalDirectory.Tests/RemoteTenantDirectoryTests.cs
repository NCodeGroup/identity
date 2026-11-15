// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteTenantDirectoryTests : RemoteDirectoryTestBase
{
    private const string ConflictBody =
        """{"type":"about:blank","title":"Conflict","status":409,"detail":"The issuer is held by another tenant."}""";

    private const string NotFoundBody =
        """{"type":"about:blank","title":"Not Found","status":404,"detail":"The directory has no record of tenant 'acme'."}""";

    private static TenantReservation Reservation { get; } =
        new()
        {
            TenantId = "tenant-1",
            Issuer = "https://tenant-1.example.test",
            Cell = "cell-a",
        };

    private static TenantIssuerChange IssuerChange { get; } =
        new()
        {
            TenantId = "tenant-1",
            PreviousIssuer = "https://tenant-1.example.test",
            Issuer = "https://login.tenant-1.example.test",
        };

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenDirectoryAnswersOk_ReturnsReserved()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ReserveAsync(Reservation, CancellationToken.None);

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenDirectoryAnswersOk_PostsTheReservationWithTheBearerToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await directory.ReserveAsync(Reservation, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://directory.example.test/directory/v1/tenants/reserve",
            request.RequestUri?.AbsoluteUri
        );
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        Assert.NotNull(request.Body);
        Assert.Equal(
            Reservation,
            JsonSerializer.Deserialize<TenantReservation>(request.Body, WireOptions)
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenDirectoryAnswersConflict_ReturnsConflictWithTheProblemDetail()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, ConflictBody);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ReserveAsync(Reservation, CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("The issuer is held by another tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenConflictCarriesNoProblemDetail_ReturnsConflictWithTheFallback()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, "not json");
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ReserveAsync(Reservation, CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("The directory refused to reserve a tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ReserveAsync(Reservation, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheRequestTimesOut_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(
            new TaskCanceledException("The request timed out.", new TimeoutException())
        );
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ReserveAsync(Reservation, CancellationToken.None)
        );

        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheConnectionIsRefused_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(new HttpRequestException("Connection refused."));
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ReserveAsync(Reservation, CancellationToken.None)
        );

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    #endregion

    #region ChangeIssuerAsync Tests

    [Fact]
    public async Task ChangeIssuerAsync_WhenDirectoryAnswersOk_ReturnsReserved()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None);

        Assert.True(result.IsReserved);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://directory.example.test/directory/v1/tenants/change-issuer",
            request.RequestUri?.AbsoluteUri
        );
        Assert.NotNull(request.Body);
        Assert.Equal(
            IssuerChange,
            JsonSerializer.Deserialize<TenantIssuerChange>(request.Body, WireOptions)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenDirectoryAnswersConflict_ReturnsConflictWithTheProblemDetail()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, ConflictBody);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("The issuer is held by another tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenDirectoryAnswersNotFound_ReturnsConflictWithTheProblemDetail()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound, NotFoundBody);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("The directory has no record of tenant 'acme'.", result.ConflictDetail);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheRequestTimesOut_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(
            new TaskCanceledException("The request timed out.", new TimeoutException())
        );
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheConnectionIsRefused_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(new HttpRequestException("Connection refused."));
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ChangeIssuerAsync(IssuerChange, CancellationToken.None)
        );
    }

    #endregion

    #region OfferMoveAsync Tests

    [Fact]
    public async Task OfferMoveAsync_WhenDirectoryAnswersNoContent_ReturnsOfferedAndPostsTheOfferWithTheBearerToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NoContent);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.OfferMoveAsync(
            "tenant 1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.True(result.IsOffered);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://directory.example.test/directory/v1/tenants/tenant%201/move-offers",
            request.RequestUri?.AbsoluteUri
        );
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        Assert.NotNull(request.Body);
        Assert.Equal(
            new TenantMoveOfferRequest { ToCell = "cell-b" },
            JsonSerializer.Deserialize<TenantMoveOfferRequest>(request.Body, WireOptions)
        );
    }

    [Fact]
    public async Task OfferMoveAsync_WhenDirectoryAnswersConflict_ReturnsConflictWithTheProblemDetail()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, ConflictBody);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.OfferMoveAsync(
            "tenant-1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsOffered);
        Assert.Equal("The issuer is held by another tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task OfferMoveAsync_WhenConflictCarriesNoProblemDetail_ReturnsConflictWithTheFallback()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, "not json");
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.OfferMoveAsync(
            "tenant-1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsOffered);
        Assert.Equal("The directory refused to record a move offer.", result.ConflictDetail);
    }

    [Fact]
    public async Task OfferMoveAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.OfferMoveAsync("tenant-1", "cell-a", "cell-b", CancellationToken.None)
        );
    }

    #endregion

    #region MoveAsync Tests

    [Fact]
    public async Task MoveAsync_WhenDirectoryAnswersOk_ReturnsMovedAndPostsTheMoveWithTheBearerToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.MoveAsync(
            "tenant 1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.True(result.IsMoved);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://directory.example.test/directory/v1/tenants/tenant%201/move?fromCell=cell-a&toCell=cell-b",
            request.RequestUri?.AbsoluteUri
        );
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
    }

    [Fact]
    public async Task MoveAsync_WhenDirectoryAnswersConflict_ReturnsConflictWithTheProblemDetail()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, ConflictBody);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.MoveAsync(
            "tenant-1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal("The issuer is held by another tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task MoveAsync_WhenConflictCarriesNoProblemDetail_ReturnsConflictWithTheFallback()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict, "not json");
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.MoveAsync(
            "tenant-1",
            "cell-a",
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal("The directory refused to move a tenant.", result.ConflictDetail);
    }

    [Fact]
    public async Task MoveAsync_WhenDirectoryAnswersUnprocessableEntity_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.UnprocessableEntity);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.MoveAsync("tenant-1", "cell-a", "cell-b", CancellationToken.None)
        );
    }

    [Fact]
    public async Task MoveAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.MoveAsync("tenant-1", "cell-a", "cell-b", CancellationToken.None)
        );
    }

    #endregion

    #region GetPlacementOrDefaultAsync Tests

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenDirectoryAnswersNotFound_ReturnsNull()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        var result = await directory.GetPlacementOrDefaultAsync("tenant-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTenantDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.GetPlacementOrDefaultAsync("tenant-1", CancellationToken.None)
        );
    }

    #endregion
}
