// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

public class ControlPlaneTenantDirectoryTests
{
    #region GetJwks Tests

    [Fact]
    public async Task GetJwks_WhenTheDirectoryCannotBeReached_ServesTheControlPlaneTenantsKeys()
    {
        // The directory accepts only tokens the control-plane cell's control-plane tenant issues, so that tenant is
        // created, judged and keyed without the directory (issue #918).
        using var factory = new HostApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(serviceCollection =>
            {
                serviceCollection.RemoveAll<ITenantDirectory>();
                serviceCollection.AddSingleton<ITenantDirectory, UnreachableTenantDirectory>();
            })
        );
        var client = host.CreateClient();

        using var response = await client.GetAsync("/oauth2/jwks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonElement.Parse(json);

        Assert.True(jsonElement.TryGetProperty("keys", out var keys));
        Assert.Equal(JsonValueKind.Array, keys.ValueKind);
        Assert.True(keys.GetArrayLength() >= 1);
    }

    #endregion
}
