// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NCode.Buffers;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Jwt.Encoding;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DirectoryTokenEndpointTests : IAsyncLifetime
{
    private const string ControlPlaneCell = "control";
    private const string TokenPath = "/directory/v1/token";
    private const string TokenEndpoint = "https://directory.example/directory/v1/token";
    private const string JwtBearer = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private SqliteConnection Connection { get; } = new("DataSource=:memory:");
    private RSA CellRsa { get; } = RSA.Create(2048);
    private RSA OtherRsa { get; } = RSA.Create(2048);
    private FakeJwksReader JwksReader { get; } = new();
    private SecretKey? CellKeyOrNull { get; set; }
    private SecretKey? OtherKeyOrNull { get; set; }
    private WebApplication? App { get; set; }
    private HttpClient? ClientOrNull { get; set; }

    private HttpClient Client => ClientOrNull ?? throw new InvalidOperationException();
    private WebApplication Host => App ?? throw new InvalidOperationException();
    private SecretKey CellKey => CellKeyOrNull ?? throw new InvalidOperationException();
    private SecretKey OtherKey => OtherKeyOrNull ?? throw new InvalidOperationException();

    public async Task InitializeAsync()
    {
        Connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            DirectoryServerTestOptions.Create(
                ControlPlaneCell,
                "root",
                "https://control-plane.example"
            )
        );
        builder.Services.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(Connection)
        );
        var libraries = DirectoryServerTestOptions.AddDirectoryTokenServices(builder.Services);
        builder.Services.AddSingleton<IPublicJwksReader>(JwksReader);
        libraries.AddOpenIdDirectoryServer(builder.Configuration);

        var app = builder.Build();
        app.MapDirectory();

        var dbContextFactory = app.Services.GetRequiredService<
            IDbContextFactory<DirectoryDbContext>
        >();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        var factory = app.Services.GetRequiredService<ISecretKeyFactory>();
        CellKeyOrNull = factory.CreateRsa(new KeyMetadata { KeyId = "cell-key" }, CellRsa);
        OtherKeyOrNull = factory.CreateRsa(new KeyMetadata { KeyId = "other-key" }, OtherRsa);
        JwksReader.Keys = [CellKey];

        await app.StartAsync();

        App = app;
        ClientOrNull = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        ClientOrNull?.Dispose();
        if (App is not null)
            await App.DisposeAsync();

        await Connection.DisposeAsync();
        CellRsa.Dispose();
        OtherRsa.Dispose();
    }

    private async ValueTask AddCellAsync(
        string name,
        bool isRetired = false,
        CancellationToken cancellationToken = default
    )
    {
        var factory = Host.Services.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
        dbContext.Cells.Add(
            new CellRecord
            {
                Name = name,
                Region = "region-a",
                Kind = CellKinds.Shared,
                ClientJwksJson = """{"keys":[]}""",
                IsRetired = isRetired,
            }
        );
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask<string> CreateAssertionAsync(
        string cell = ControlPlaneCell,
        SecretKey? signingKey = null,
        string audience = TokenEndpoint,
        TimeSpan? expiresIn = null,
        string? jti = null,
        CancellationToken cancellationToken = default
    )
    {
        var algorithms = Host.Services.GetRequiredService<IAlgorithmCollectionProvider>();
        Assert.True(algorithms.Collection.TryGetSignatureAlgorithm("PS256", out var algorithm));

        var clientId = "cell-" + cell;
        var now = DateTimeOffset.UtcNow;
        var parameters = new JwtEncodingParameters
        {
            SigningCredentials = new JoseSigningCredentials(signingKey ?? CellKey, algorithm),
            EnsureTokenLifetime = false,
            Issuer = clientId,
            Audience = new StringValues(audience),
            IssuedAt = now.AddMinutes(-1),
            NotBefore = now.AddMinutes(-1),
            Expires = now + (expiresIn ?? TimeSpan.FromMinutes(1)),
            ExtraPayloadClaims = new Dictionary<string, object>
            {
                ["sub"] = clientId,
                ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
            },
        };
        var encoder = Host.Services.GetRequiredService<IJwtEncoder>();
        return await encoder.EncodeAsync(parameters, cancellationToken);
    }

    private async ValueTask<HttpResponseMessage> PostAsync(
        string? assertion,
        string grantType = "client_credentials",
        string assertionType = JwtBearer,
        CancellationToken cancellationToken = default
    )
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            ["client_assertion_type"] = assertionType,
        };
        if (assertion is not null)
            fields["client_assertion"] = assertion;

        using var content = new FormUrlEncodedContent(fields);
        return await Client.PostAsync(TokenPath, content, cancellationToken);
    }

    private static async ValueTask<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("error").GetString() ?? string.Empty;
    }

    #region Token Tests

    [Fact]
    public async Task Token_WhenTheAssertionIsGood_IssuesATokenTheGateAccepts()
    {
        await AddCellAsync("cell-a");
        var assertion = await CreateAssertionAsync("cell-a");

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(300, body.GetProperty("expires_in").GetInt64());
        var token = body.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cells = await Client.GetAsync("/directory/v1/cells");
        Assert.Equal(HttpStatusCode.OK, cells.StatusCode);
    }

    [Fact]
    public async Task Token_WhenTheDatabaseIsEmpty_AnswersTheAnchorsFirstRequest()
    {
        var assertion = await CreateAssertionAsync(ControlPlaneCell);

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_WhenTheAudienceIsTheIssuer_IssuesAToken()
    {
        var assertion = await CreateAssertionAsync(audience: "https://directory.example");

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_WhenTheAudienceIsWrong_AnswersInvalidClient()
    {
        var assertion = await CreateAssertionAsync(audience: "https://other.example/token");

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheJtiIsReplayed_AnswersInvalidClient()
    {
        var first = await CreateAssertionAsync(jti: "same");
        var second = await CreateAssertionAsync(jti: "same");

        var firstResponse = await PostAsync(first);
        var secondResponse = await PostAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(secondResponse));
    }

    [Fact]
    public async Task Token_WhenTheAssertionIsExpired_AnswersInvalidClient()
    {
        var assertion = await CreateAssertionAsync(expiresIn: TimeSpan.FromMinutes(-10));

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheCellIsUnknown_AnswersInvalidClient()
    {
        var assertion = await CreateAssertionAsync("cell-x");

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheCellIsRetired_AnswersInvalidClient()
    {
        await AddCellAsync("cell-b", isRetired: true);
        var assertion = await CreateAssertionAsync("cell-b");

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenAnotherKeySigns_AnswersInvalidClient()
    {
        var assertion = await CreateAssertionAsync(signingKey: OtherKey);

        var response = await PostAsync(assertion);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheGrantTypeIsNotClientCredentials_AnswersUnsupportedGrantType()
    {
        var assertion = await CreateAssertionAsync();

        var response = await PostAsync(assertion, grantType: "password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_grant_type", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheAssertionIsMissing_AnswersInvalidRequest()
    {
        var response = await PostAsync(assertion: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheAssertionTypeIsWrong_AnswersInvalidRequest()
    {
        var assertion = await CreateAssertionAsync();

        var response = await PostAsync(assertion, assertionType: "urn:other");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Token_WhenTheBodyIsNotAForm_AnswersInvalidRequest()
    {
        using var content = new StringContent("{}", SecureEncoding.UTF8, "application/json");

        var response = await Client.PostAsync(TokenPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ReadErrorAsync(response));
    }

    #endregion

    private sealed class FakeJwksReader : IPublicJwksReader
    {
        public IReadOnlyList<SecretKey> Keys { get; set; } = [];

        public bool TryRead(
            JsonElement jwks,
            [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
            [NotNullWhen(false)] out string? error
        )
        {
            keys = Keys;
            error = null;
            return true;
        }
    }
}
