// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// Proves that <see cref="PlatformHarness"/> starts a directory, a control-plane cell and two workload cells that serve
/// each other's requests, and that a stopped host refuses connections.
/// </summary>
public class PlatformHarnessTests
{
    private static CancellationTokenSource CreateTimeout() => new(TimeSpan.FromMinutes(3));

    [Fact]
    public async Task StartAsync_WhenTheHostsAreStarted_ServesTheControlPlaneAndBothCells()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellAName, administrator, timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellBName, administrator, timeout.Token);

        var cells = await administrator.GetAsync(
            $"{PlatformHarness.ControlPlaneUrl}/api/cells",
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.OK, cells.Status);
        var names = JsonElement
            .Parse(cells.Body)
            .EnumerateArray()
            .Select(cell => cell.GetProperty("name").GetString())
            .ToList();
        Assert.Contains(PlatformHarness.ControlPlaneName, names);
        Assert.Contains(PlatformHarness.CellAName, names);
        Assert.Contains(PlatformHarness.CellBName, names);

        var tenants = await administrator.GetAsync(
            $"{PlatformHarness.CellBUrl}/api/tenants",
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.OK, tenants.Status);
    }

    [Fact]
    public async Task Stop_WhenACellIsStopped_RefusesItsConnectionsUntilItIsStartedAgain()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        using var client = platform.CreateClient();

        platform.Router.Stop(PlatformHarness.CellAName);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync($"{PlatformHarness.CellAUrl}/health/live", timeout.Token)
        );

        platform.Router.Start(PlatformHarness.CellAName);
        using var response = await client.GetAsync(
            $"{PlatformHarness.CellAUrl}/.well-known/openid-configuration",
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
