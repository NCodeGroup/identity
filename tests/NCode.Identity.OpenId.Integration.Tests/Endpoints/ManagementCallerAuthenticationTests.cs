// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Host;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

// A management route takes its caller only from a management access token, whatever principal another authentication
// scheme set. See ADR-1044.
public class ManagementCallerAuthenticationTests
{
    #region Helpers

    private static WebApplicationFactory<HostApiMarker> WithAnotherDefaultScheme(
        HostApplicationFactory factory
    ) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(serviceCollection =>
                serviceCollection
                    .AddAuthentication(TestSubjectAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestSubjectAuthenticationHandler>(
                        TestSubjectAuthenticationHandler.SchemeName,
                        configureOptions: null
                    )
            )
        );

    #endregion

    #region GetCaller Tests

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/me/teams")]
    [InlineData("/api/me/tenants")]
    public async Task GetCaller_WithoutAToken_IsAnUnauthorizedProblem(string path)
    {
        using var factory = new BootstrapAdminApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCaller_WhenTheTokensSessionWasRevoked_IsAnUnauthorizedProblem()
    {
        const string clientId = "it-revoked-session";
        const string redirectUri = "https://client.example/callback";
        const string userName = "it-revoked-session-user";
        const string password = "it-revoked-session-password";
        using var factory = new BootstrapAdminApplicationFactory();
        await factory.SeedLocalAccountAsync(userName, password, CancellationToken.None);
        await factory.SeedPublicClientAsync(clientId, [redirectUri], CancellationToken.None);
        using var caller = await factory.CreateManagementUserClientAsync(
            clientId,
            redirectUri,
            userName,
            password,
            "openid read:clients",
            CancellationToken.None
        );
        var token = caller.DefaultRequestHeaders.Authorization!.Parameter!;
        var sessionId = JsonSerializer
            .Deserialize<JsonElement>(Base64Url.DecodeFromChars(token.Split('.')[1]))
            .GetProperty("sid")
            .GetString();

        using var beforeRevocation = await caller.GetAsync("/api/me");
        var beforeBody = await beforeRevocation.Content.ReadAsStringAsync();
        Assert.True(beforeRevocation.StatusCode == HttpStatusCode.OK, beforeBody);

        using var administrator = await factory.CreateAdministratorClientAsync(
            CancellationToken.None
        );
        using var revocation = await administrator.DeleteAsync(
            $"/api/tenants/{HostApplicationFactory.TenantId}/sessions/{sessionId}"
        );
        var revocationBody = await revocation.Content.ReadAsStringAsync();
        Assert.True(revocation.StatusCode == HttpStatusCode.NoContent, revocationBody);

        using var afterRevocation = await caller.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode);
        Assert.Equal(
            "application/problem+json",
            afterRevocation.Content.Headers.ContentType?.MediaType
        );
    }

    #endregion

    #region GetManagementRoute Tests

    [Theory]
    [InlineData("/api/capabilities")]
    [InlineData("/api/me")]
    [InlineData("/api/me/teams")]
    [InlineData("/api/me/tenants")]
    [InlineData("/api/teams")]
    public async Task GetManagementRoute_WhenAnotherSchemeAuthenticatesTheRequest_IsAnUnauthorizedProblem(
        string path
    )
    {
        using var factory = new BootstrapAdminApplicationFactory();
        using var anotherSchemeFactory = WithAnotherDefaultScheme(factory);
        using var client = anotherSchemeFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestSubjectAuthenticationHandler.SubjectHeaderName, "alice");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    #endregion
}
