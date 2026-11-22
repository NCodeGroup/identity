// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryHostTests
{
    private static Uri BaseAddress { get; } = new("https://localhost:5005/");

    private static HttpClient CreateClient(DirectoryHostFactory factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = BaseAddress,
                AllowAutoRedirect = false,
                HandleCookies = false,
            }
        );

    #region Liveness Tests

    [Fact]
    public async Task GetLive_WhenTheHostRuns_ReturnsOk()
    {
        await using var factory = new DirectoryHostFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLive_WhenTheHostAnswers_SendsNoServerHeader()
    {
        await using var factory = new DirectoryHostFactory();
        using var client = CreateClient(factory);
        var options = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        using var response = await client.GetAsync("/health/live");

        Assert.False(options.AddServerHeader);
        Assert.False(response.Headers.Contains("Server"));
    }

    #endregion

    #region Services Tests

    [Fact]
    public async Task Services_WhenTheHostStarts_RegistersNoDataProtection()
    {
        // The directory protects nothing that outlives the pod, so it carries no Data Protection key ring
        // (issue #920).
        await using var factory = new DirectoryHostFactory();

        var services = factory.Services;

        Assert.Null(services.GetService<IDataProtectionProvider>());
        Assert.Null(services.GetService<IKeyManager>());
    }

    #endregion

    #region Tenant Tests

    [Fact]
    public async Task GetTenant_WithoutABearerToken_ReturnsUnauthorized()
    {
        await using var factory = new DirectoryHostFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/directory/v1/tenants/acme");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
