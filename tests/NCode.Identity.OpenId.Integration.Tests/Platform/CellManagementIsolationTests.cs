// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Platform;

/// <summary>
/// Proves that each workload cell is set up and managed on its own: the setup command creates a workload cell's first
/// <c>CellAdmin</c> and its cell console's client on an empty database and publishes the control-plane cell's
/// administrator through the outbox; a <c>CellAdmin</c> lists the cell's tenants and creates a tenant of a team that
/// grants the cell with the control-plane cell down, and only the creation waits for the directory; and the
/// control-plane cell republishes, at its next start, a document whose rows no outbox row ever published.
/// </summary>
public class CellManagementIsolationTests
{
    private const string TeamId = "acme";
    private const string RootUserName = "root-admin";
    private const string RootPassword = "root-admin-has-a-long-passphrase-1";
    private const string CellAdminUserName = "cell-admin";
    private const string CellAdminPassword = "cell-admin-has-a-long-passphrase-1";
    private const string ConsoleClientId = "alpha-console";
    private const string ConsoleBaseUrl = "https://console.alpha.platform.test";

    // A public JWK Set of one P-256 key, which setup imports by value for the console's client.
    private const string ConsoleJwks =
        """{"keys":[{"kty":"EC","crv":"P-256","kid":"console","x":"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU","y":"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"}]}""";

    private static CancellationTokenSource CreateTimeout() => new(TimeSpan.FromMinutes(3));

    #region Scaffolding

    private static async ValueTask CreateTeamAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller administrator,
        IReadOnlyList<string> tenantCreationCells,
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
                settings = new Dictionary<string, object>
                {
                    ["tenant_creation_cells"] = tenantCreationCells,
                },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);

        await platform.WaitForTeamAsync(PlatformHarness.CellBName, TeamId, cancellationToken);
    }

    private static async ValueTask<(HttpStatusCode Status, string Body)> CreateTenantAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string teamId,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var response = await caller.SendAsync(
            HttpMethod.Post,
            $"{PlatformHarness.GetCellUrl(cellName)}/api/teams/{teamId}/tenants",
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

    private static async ValueTask<List<string?>> ListIdsAsync(
        PlatformHarness.ManagementCaller caller,
        string url,
        string property,
        CancellationToken cancellationToken
    )
    {
        var listed = await caller.GetAsync(url, cancellationToken);
        Assert.True(listed.Status == HttpStatusCode.OK, listed.Body);
        return JsonElement
            .Parse(listed.Body)
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty(property).GetString())
            .ToList();
    }

    private static async ValueTask<PlatformHarness.ManagementCaller> SignInCellAdministratorAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller administrator,
        string cellName,
        string tenantId,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            cellName,
            tenantId,
            cancellationToken
        );
        return await platform.CreateUserCallerAsync(
            cellName,
            userClient,
            userName,
            password,
            cancellationToken
        );
    }

    #endregion

    #region Setup

    [Fact]
    public async Task Setup_InAWorkloadCellWithAnEmptyDatabase_CreatesItsCellAdministratorAndItsConsoleClient()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);

        // Cell A has served no request, so setup reserves its management tenant through the directory itself.
        var exitCode = await platform.RunSetupCommandAsync(
            PlatformHarness.CellAName,
            RootUserName,
            RootPassword,
            new SetupManagementConsoleOptions
            {
                ClientId = ConsoleClientId,
                BaseUrl = ConsoleBaseUrl,
                Jwks = ConsoleJwks,
            },
            timeout.Token
        );
        Assert.Equal(0, exitCode);

        using (
            var scope = new TenantServiceScope(
                platform.CellA.Services,
                PlatformHarness.CellATenantId
            )
        )
        {
            var storeManagerFactory =
                scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
            await using var storeManager = await storeManagerFactory.CreateAsync(timeout.Token);
            var console = await storeManager
                .GetStore<IClientStore>()
                .GetOrDefaultAsync(ConsoleClientId, timeout.Token);
            Assert.NotNull(console);
        }

        await platform.WaitForReplicaAsync(PlatformHarness.CellAName, administrator, timeout.Token);
        var cellAdministrator = await SignInCellAdministratorAsync(
            platform,
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            RootUserName,
            RootPassword,
            timeout.Token
        );
        var caller = await cellAdministrator.GetAsync(
            $"{PlatformHarness.CellAUrl}/api/me",
            timeout.Token
        );
        Assert.True(caller.Status == HttpStatusCode.OK, caller.Body);
        Assert.Contains(
            BuiltInRoles.CellAdmin,
            JsonElement
                .Parse(caller.Body)
                .GetProperty("cellRoles")
                .EnumerateArray()
                .Select(role => role.GetString())
        );
        Assert.Contains(
            PlatformHarness.CellATenantId,
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellAUrl}/api/cell/tenants",
                "tenantId",
                timeout.Token
            )
        );
    }

    [Fact]
    public async Task Setup_InTheControlPlaneCell_PublishesTheRootAdministratorSoAWorkloadCellAcceptsItsToken()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);

        var exitCode = await platform.RunSetupCommandAsync(
            PlatformHarness.ControlPlaneName,
            RootUserName,
            RootPassword,
            managementConsole: null,
            timeout.Token
        );
        Assert.Equal(0, exitCode);

        // The root administrator's GlobalAdmin assignment reaches the workload cell through the outbox and the feed,
        // with no restart in between.
        var root = await SignInCellAdministratorAsync(
            platform,
            administrator,
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            RootUserName,
            RootPassword,
            timeout.Token
        );
        await platform.WaitForReplicaAsync(PlatformHarness.CellBName, root, timeout.Token);
    }

    #endregion

    #region Isolation

    [Fact]
    public async Task GlobalPlaneDown_WhenACellAdministratorManagesItsCell_CreatesATenantOnlyWhileTheDirectoryAnswers()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellBName, administrator, timeout.Token);
        await CreateTeamAsync(platform, administrator, [PlatformHarness.CellBName], timeout.Token);
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

        // The control-plane cell, and the platform console it serves, are down.
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
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/tenants",
                "tenantId",
                timeout.Token
            )
        );
        Assert.Equal(
            [TeamId],
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/teams",
                "teamId",
                timeout.Token
            )
        );

        var created = await CreateTenantAsync(
            cellAdministrator,
            PlatformHarness.CellBName,
            TeamId,
            "acme-beta",
            timeout.Token
        );
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
        Assert.Contains(
            "acme-beta",
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/tenants",
                "tenantId",
                timeout.Token
            )
        );

        // The platform's own team grants no cell, so its tenants are created by a platform administrator alone.
        var refused = await CreateTenantAsync(
            cellAdministrator,
            PlatformHarness.CellBName,
            "platform",
            "platform-extra",
            timeout.Token
        );
        Assert.True(refused.Status == HttpStatusCode.Forbidden, refused.Body);

        // With the directory down too, only the creation, which reserves the tenant's identifier, waits for it.
        platform.Router.Stop(PlatformHarness.DirectoryName);

        var unavailable = await CreateTenantAsync(
            cellAdministrator,
            PlatformHarness.CellBName,
            TeamId,
            "acme-beta-2",
            timeout.Token
        );
        Assert.True(unavailable.Status == HttpStatusCode.ServiceUnavailable, unavailable.Body);
        Assert.Contains(
            "acme-beta",
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/tenants",
                "tenantId",
                timeout.Token
            )
        );
        Assert.Equal(
            [TeamId],
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/teams",
                "teamId",
                timeout.Token
            )
        );
        var tenant = await cellAdministrator.GetAsync(
            $"{PlatformHarness.CellBUrl}/api/tenants/acme-beta",
            timeout.Token
        );
        Assert.True(tenant.Status == HttpStatusCode.OK, tenant.Body);
    }

    [Fact]
    public async Task CreateTenant_WhenTheTeamGrantsAnotherCell_RefusesTheCellAdministrator()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellBName, administrator, timeout.Token);
        await CreateTeamAsync(platform, administrator, [PlatformHarness.CellAName], timeout.Token);
        await platform.RecoverCellAdministratorAsync(
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        var cellAdministrator = await SignInCellAdministratorAsync(
            platform,
            administrator,
            PlatformHarness.CellBName,
            PlatformHarness.CellBTenantId,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );

        Assert.Empty(
            await ListIdsAsync(
                cellAdministrator,
                $"{PlatformHarness.CellBUrl}/api/cell/teams",
                "teamId",
                timeout.Token
            )
        );
        var refused = await CreateTenantAsync(
            cellAdministrator,
            PlatformHarness.CellBName,
            TeamId,
            "acme-beta",
            timeout.Token
        );
        Assert.True(refused.Status == HttpStatusCode.Forbidden, refused.Body);
    }

    #endregion

    #region Recovery

    [Fact]
    public async Task OutboxPublisher_WhenADocumentWasNeverPublished_RepublishesItAtTheNextStart()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await platform.CreateAdministratorAsync(timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellAName, administrator, timeout.Token);

        var me = await administrator.GetAsync(
            $"{PlatformHarness.ControlPlaneUrl}/api/me",
            timeout.Token
        );
        Assert.True(me.Status == HttpStatusCode.OK, me.Body);
        var principalId =
            JsonElement.Parse(me.Body).GetProperty("principalId").GetString()
            ?? throw new InvalidOperationException("The caller has no principal identifier.");
        var replica = platform.CellA.Services.GetRequiredService<IControlPlaneReplica>();

        // The platform authority document carries the administrator's GlobalAdmin assignment to cell A.
        async Task<bool> HoldsGlobalAdminAsync()
        {
            var authority = await replica.GetAuthorityAsync(principalId, null, timeout.Token);
            return authority.PlatformRoles.Contains(BuiltInRoles.GlobalAdmin);
        }

        Assert.True(await HoldsGlobalAdminAsync());

        // The platform is left as the setup of a control-plane cell that did not publish leaves it: the rows exist, and
        // neither a source revision, an outbox row, the directory nor the workload cell holds the document.
        await platform.ForgetPublicationAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            string.Empty,
            PlatformHarness.CellAName,
            timeout.Token
        );
        Assert.False(await HoldsGlobalAdminAsync());

        await platform.RestartOutboxPublisherAsync(timeout.Token);

        await platform.WaitUntilAsync(
            HoldsGlobalAdminAsync,
            "cell A holds the republished platform authority",
            timeout.Token
        );
    }

    #endregion
}
