// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Platform;

/// <summary>
/// Proves that the two parts of the global plane and every workload cell fail and are managed alone: a cell serves its
/// users and its management callers while the control-plane cell, the directory or the other cell is down, and a
/// platform starts from empty databases in its bootstrap order.
/// </summary>
public class GlobalPlaneIsolationTests
{
    private const string TeamId = "acme";
    private const string SignInUserName = "alice";
    private const string SignInPassword = "alice-has-a-long-passphrase-1";
    private const string CellAdminUserName = "cell-admin";
    private const string CellAdminPassword = "cell-admin-has-a-long-passphrase-1";

    private static CancellationTokenSource CreateTimeout() => new(TimeSpan.FromMinutes(3));

    #region Scaffolding

    private static async ValueTask<PlatformHarness.ManagementCaller> StartReplicatedPlatformAsync(
        PlatformHarness platform,
        CancellationToken cancellationToken
    )
    {
        var administrator = await platform.CreateAdministratorAsync(cancellationToken);
        await platform.WaitForReplicaAsync(
            PlatformHarness.CellAName,
            administrator,
            cancellationToken
        );
        await platform.WaitForReplicaAsync(
            PlatformHarness.CellBName,
            administrator,
            cancellationToken
        );
        return administrator;
    }

    private static async ValueTask CreateTeamAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller administrator,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.SendAsync(
            HttpMethod.Post,
            $"{PlatformHarness.ControlPlaneUrl}/api/teams",
            new
            {
                teamId = TeamId,
                displayName = TeamId,
                isDisabled = false,
                settings = new { },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);

        await platform.WaitForTeamAsync(PlatformHarness.CellAName, TeamId, cancellationToken);
        await platform.WaitForTeamAsync(PlatformHarness.CellBName, TeamId, cancellationToken);
    }

    private static async ValueTask<(HttpStatusCode Status, string Body)> CreateTenantAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var response = await caller.SendAsync(
            HttpMethod.Post,
            $"{PlatformHarness.GetCellUrl(cellName)}/api/teams/{TeamId}/tenants",
            new
            {
                tenantId,
                displayName = tenantId,
                isDisabled = false,
                settings = new { },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, body);
    }

    private static async ValueTask<List<string?>> ListTenantIdsAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string path,
        CancellationToken cancellationToken
    )
    {
        var tenants = await caller.GetAsync(
            $"{PlatformHarness.GetCellUrl(cellName)}{path}",
            cancellationToken
        );
        Assert.True(tenants.Status == HttpStatusCode.OK, tenants.Body);
        var items = JsonElement.Parse(tenants.Body);
        var array = items.ValueKind == JsonValueKind.Array ? items : items.GetProperty("items");
        return array
            .EnumerateArray()
            .Select(tenant => tenant.GetProperty("tenantId").GetString())
            .ToList();
    }

    #endregion

    #region Control-plane cell stopped

    [Fact]
    public async Task ControlPlaneStopped_WhenBothCellsServe_UsersSignInAndValidTokensManageTenantsInEachCell()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await CreateTeamAsync(platform, administrator, timeout.Token);
        await platform.SeedLocalAccountAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            SignInUserName,
            SignInPassword,
            timeout.Token
        );
        await platform.RecoverCellAdministratorAsync(
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );

        platform.Router.Stop(PlatformHarness.ControlPlaneName);

        using var controlPlaneClient = platform.CreateClient();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            controlPlaneClient.GetAsync(
                $"{PlatformHarness.ControlPlaneUrl}/.well-known/openid-configuration",
                timeout.Token
            )
        );

        using var browserA = platform.CreateBrowser(PlatformHarness.CellAName);
        var sessionA = await InteractiveLogin.SignInAsync(
            browserA,
            SignInUserName,
            SignInPassword,
            timeout.Token
        );
        Assert.NotEmpty(sessionA);

        using var browserB = platform.CreateBrowser(PlatformHarness.CellBName);
        var sessionB = await InteractiveLogin.SignInAsync(
            browserB,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        Assert.NotEmpty(sessionB);

        Assert.Contains(
            PlatformHarness.CellATenantId,
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellAName,
                "/api/tenants",
                timeout.Token
            )
        );
        Assert.Contains(
            PlatformHarness.CellBTenantId,
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellBName,
                "/api/tenants",
                timeout.Token
            )
        );

        var created = await CreateTenantAsync(
            administrator,
            PlatformHarness.CellAName,
            "alpha-extra",
            timeout.Token
        );
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
        Assert.Contains(
            "alpha-extra",
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellAName,
                "/api/tenants",
                timeout.Token
            )
        );
    }

    [Fact]
    public async Task ControlPlaneStopped_WhenACellAdministratorSignsIn_ManagesTheTenantsOfItsCell()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await platform.RecoverCellAdministratorAsync(
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.CellBName,
            PlatformHarness.CellBTenantId,
            timeout.Token
        );

        platform.Router.Stop(PlatformHarness.ControlPlaneName);

        var cellAdministrator = await platform.CreateUserCallerAsync(
            PlatformHarness.CellBName,
            userClient,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        Assert.Contains(
            PlatformHarness.CellBTenantId,
            await ListTenantIdsAsync(
                cellAdministrator,
                PlatformHarness.CellBName,
                "/api/cell/tenants",
                timeout.Token
            )
        );

        var tenant = await cellAdministrator.GetAsync(
            $"{PlatformHarness.CellBUrl}/api/tenants/{PlatformHarness.CellBTenantId}",
            timeout.Token
        );
        Assert.True(tenant.Status == HttpStatusCode.OK, tenant.Body);

        var otherCell = await cellAdministrator.GetAsync(
            $"{PlatformHarness.CellAUrl}/api/cell/tenants",
            timeout.Token
        );
        Assert.True(
            otherCell.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{(int)otherCell.Status}: {otherCell.Body}"
        );
    }

    #endregion

    #region Directory stopped

    [Fact]
    public async Task DirectoryStopped_WhenAValidTokenManagesTenants_ManagementWorksAndCreationAnswersServiceUnavailable()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await CreateTeamAsync(platform, administrator, timeout.Token);

        platform.Router.Stop(PlatformHarness.DirectoryName);

        foreach (var cellName in new[] { PlatformHarness.CellAName, PlatformHarness.CellBName })
        {
            var tenants = await administrator.GetAsync(
                $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants",
                timeout.Token
            );
            Assert.True(tenants.Status == HttpStatusCode.OK, tenants.Body);
        }

        var refused = await CreateTenantAsync(
            administrator,
            PlatformHarness.CellAName,
            "alpha-refused",
            timeout.Token
        );
        Assert.True(
            refused.Status == HttpStatusCode.ServiceUnavailable,
            $"{(int)refused.Status}: {refused.Body}"
        );
        Assert.DoesNotContain(
            "alpha-refused",
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellAName,
                "/api/tenants",
                timeout.Token
            )
        );

        await platform.WaitUntilAsync(
            async () =>
                await platform.GetReplicaAgeSecondsAsync(PlatformHarness.CellAName, timeout.Token)
                    >= 4
                && await platform.GetReplicaAgeSecondsAsync(
                    PlatformHarness.CellBName,
                    timeout.Token
                ) >= 4,
            "the replicas of both cells grow older while the directory is down",
            timeout.Token
        );

        platform.Router.Start(PlatformHarness.DirectoryName);

        await platform.WaitUntilAsync(
            async () =>
                await platform.GetReplicaAgeSecondsAsync(PlatformHarness.CellAName, timeout.Token)
                < 3,
            "the replica of cell A completes a feed pass once the directory is back",
            timeout.Token
        );
        var created = await CreateTenantAsync(
            administrator,
            PlatformHarness.CellAName,
            "alpha-extra",
            timeout.Token
        );
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
    }

    #endregion

    #region Workload cell stopped

    [Fact]
    public async Task CellStopped_WhenCellAIsDown_CellBAndTheControlPlaneAreUnaffected()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

        platform.Router.Stop(PlatformHarness.CellAName);

        using var client = platform.CreateClient();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync($"{PlatformHarness.CellAUrl}/health/live", timeout.Token)
        );

        Assert.Contains(
            PlatformHarness.CellBTenantId,
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellBName,
                "/api/tenants",
                timeout.Token
            )
        );

        var freshAdministrator = await platform.CreateAdministratorAsync(timeout.Token);
        var cells = await freshAdministrator.GetAsync(
            $"{PlatformHarness.ControlPlaneUrl}/api/cells",
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.OK, cells.Status);

        platform.Router.Start(PlatformHarness.CellAName);

        Assert.Contains(
            PlatformHarness.CellATenantId,
            await ListTenantIdsAsync(
                administrator,
                PlatformHarness.CellAName,
                "/api/tenants",
                timeout.Token
            )
        );
    }

    #endregion

    #region Bootstrap order

    [Fact]
    public async Task StartAsync_WhenEveryDatabaseIsEmpty_TheDirectoryAnswersTheControlPlaneCellsFirstTokenRequest()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

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

        foreach (var cellName in new[] { PlatformHarness.CellAName, PlatformHarness.CellBName })
        {
            using var scope = platform.GetCell(cellName).Services.CreateScope();
            var checkpoint = scope.ServiceProvider.GetRequiredService<IDirectoryFeedCheckpoint>();
            Assert.True(await checkpoint.GetAppliedSequenceAsync(timeout.Token) > 0);
        }
    }

    #endregion
}
