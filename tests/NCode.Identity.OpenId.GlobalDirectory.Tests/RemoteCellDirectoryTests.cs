// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteCellDirectoryTests : RemoteDirectoryTestBase
{
    private static CellDescriptor CreateDescriptor() =>
        new()
        {
            Name = "cell-a",
            Region = "westeurope",
            Kind = CellKinds.Shared,
            ManagementUrl = "https://cell-a.example.test/management",
        };

    private static CellRegistration CreateRegistration() =>
        new()
        {
            Name = "cell-a",
            Region = "westeurope",
            Kind = CellKinds.Shared,
            Jwks = JsonElement.Parse("""{"keys":[]}"""),
        };

    private static string CreateProblem(string detail) =>
        JsonSerializer.Serialize(
            new
            {
                title = "Conflict",
                status = 409,
                detail,
            }
        );

    #region ReportAsync Tests

    [Fact]
    public async Task ReportAsync_WhenTheDirectoryAccepts_PutsTheDescriptorUnderTheCell()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        await directory.ReportAsync(CreateDescriptor(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/directory/v1/cells/cell-a", request.RequestUri!.AbsolutePath);
        Assert.Contains("cell-a.example.test", request.Body);
    }

    [Fact]
    public async Task ReportAsync_WhenTheDirectoryAnswersConflict_ThrowsWithTheDetail()
    {
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.Conflict,
            CreateProblem("The cell 'cell-a' is retired.")
        );
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await directory.ReportAsync(CreateDescriptor(), CancellationToken.None)
        );

        Assert.Equal("The cell 'cell-a' is retired.", exception.Message);
    }

    #endregion

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WhenTheDirectoryAccepts_PutsTheRegistrationBody()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NoContent);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        await directory.RegisterAsync(CreateRegistration(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/directory/v1/cells/cell-a/registration", request.RequestUri!.AbsolutePath);
        var body = JsonElement.Parse(request.Body!);
        Assert.Equal("westeurope", body.GetProperty("region").GetString());
        Assert.Equal(CellKinds.Shared, body.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("jwks").ValueKind);
    }

    [Fact]
    public async Task RegisterAsync_WhenTheDirectoryAnswersConflict_ThrowsWithTheDetail()
    {
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.Conflict,
            CreateProblem("The cell 'cell-a' is retired, and a retired name is never reissued.")
        );
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await directory.RegisterAsync(CreateRegistration(), CancellationToken.None)
        );

        Assert.Contains("never reissued", exception.Message);
    }

    [Fact]
    public async Task RegisterAsync_WhenTheConflictCarriesNoDetail_ThrowsWithTheFallback()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Conflict);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await directory.RegisterAsync(CreateRegistration(), CancellationToken.None)
        );

        Assert.Equal("The directory refused to register the cell.", exception.Message);
    }

    #endregion

    #region RetireAsync Tests

    [Fact]
    public async Task RetireAsync_WhenTheDirectoryAccepts_DeletesTheCell()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NoContent);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        await directory.RetireAsync("cell-a", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/directory/v1/cells/cell-a", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RetireAsync_WhenTheCellHoldsTenants_ThrowsWithTheDetail()
    {
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.Conflict,
            CreateProblem("The cell 'cell-a' holds tenants, so it cannot be retired.")
        );
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await directory.RetireAsync("cell-a", CancellationToken.None)
        );

        Assert.Contains("holds tenants", exception.Message);
    }

    [Fact]
    public async Task RetireAsync_WhenTheDirectoryFails_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.InternalServerError);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.RetireAsync("cell-a", CancellationToken.None)
        );
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheCellIsUnknown_ReturnsNull()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync("cell-a", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheCellIsRegistered_ReadsTheFlags()
    {
        var body = JsonSerializer.Serialize(
            CreateDescriptor() with
            {
                IsRegistered = true,
                IsRetired = false,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, body);
        var directory = new RemoteCellDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync("cell-a", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsRegistered);
        Assert.False(result.IsRetired);
    }

    #endregion
}
