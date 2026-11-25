// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.Server.Recovery;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Platform;

/// <summary>
/// Proves that the authority and the keys a cell replicates from the directory are enforced across the platform: a
/// revoked role stops working in every cell within a publish and a poll, a revoked management key is refused at once in
/// the control-plane cell and within a poll in every other, and a management token works only where its proof says.
/// </summary>
public class ReplicatedAuthorityTests
{
    private const string TeamId = "acme";
    private const string TeamAdminUserName = "team-admin";
    private const string TeamAdminPassword = "team-admin-has-a-long-passphrase-1";
    private const string GlobalAdminUserName = "global-admin";
    private const string GlobalAdminPassword = "global-admin-has-a-long-passphrase-1";

    // The directory publishes every second and a cell polls every second, so a change reaches a cell within a few
    // seconds; the bound leaves room for a loaded machine.
    private static TimeSpan PropagationBound { get; } = TimeSpan.FromSeconds(30);

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

    private static JsonElement DecodeJwtPart(string token, int index)
    {
        var part = token.Split('.')[index];
        var bytes = Base64Url.DecodeFromChars(part);
        return JsonElement.Parse(SecureEncoding.UTF8.GetString(bytes));
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

    private static async ValueTask<PlatformHarness.ManagementCaller> SignInControlPlaneUserAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller administrator,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            cancellationToken
        );
        return await platform.CreateUserCallerAsync(
            PlatformHarness.ControlPlaneName,
            userClient,
            userName,
            password,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a team, a control-plane user who signs in and is granted <c>TeamAdmin</c> of the team, and waits until
    /// both workload cells accept the user's token for the team.
    /// </summary>
    private static async ValueTask<(
        PlatformHarness.ManagementCaller Caller,
        string PrincipalId
    )> CreateTeamAdministratorAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller administrator,
        CancellationToken cancellationToken
    )
    {
        await CreateTeamAsync(platform, administrator, cancellationToken);
        await platform.SeedLocalAccountAsync(
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            TeamAdminUserName,
            TeamAdminPassword,
            cancellationToken
        );
        var caller = await SignInControlPlaneUserAsync(
            platform,
            administrator,
            TeamAdminUserName,
            TeamAdminPassword,
            cancellationToken
        );
        var principalId =
            DecodeJwtPart(caller.AccessToken, 1).GetProperty("sub").GetString()
            ?? throw new InvalidOperationException("The user's token has no subject.");

        using var granted = await administrator.SendAsync(
            HttpMethod.Post,
            $"{PlatformHarness.ControlPlaneUrl}/api/teams/{TeamId}/admins",
            new { principalId },
            cancellationToken
        );
        var body = await granted.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(granted.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, body);

        foreach (var cellName in new[] { PlatformHarness.CellAName, PlatformHarness.CellBName })
        {
            await WaitUntilAcceptedAsync(
                platform,
                caller,
                $"{PlatformHarness.GetCellUrl(cellName)}/api/teams/{TeamId}/tenants",
                cancellationToken
            );
        }

        return (caller, principalId);
    }

    private static async ValueTask WaitUntilAcceptedAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller caller,
        string url,
        CancellationToken cancellationToken
    ) =>
        await platform.WaitUntilAsync(
            async () =>
            {
                var answer = await caller.GetAsync(url, cancellationToken);
                if (answer.Status == HttpStatusCode.OK)
                    return true;

                throw new InvalidOperationException($"{(int)answer.Status}: {answer.Body}");
            },
            $"'{url}' accepts the caller",
            cancellationToken
        );

    private static async ValueTask WaitUntilRefusedAsync(
        PlatformHarness platform,
        PlatformHarness.ManagementCaller caller,
        string url,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        await platform.WaitUntilAsync(
            async () =>
                (await caller.GetAsync(url, cancellationToken)).Status
                    is HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden
                        or HttpStatusCode.NotFound,
            $"'{url}' refuses the caller",
            cancellationToken
        );
        Assert.True(
            stopwatch.Elapsed < PropagationBound,
            $"'{url}' took {stopwatch.Elapsed} to refuse the caller."
        );
    }

    private static async ValueTask AssertRefusedAsync(
        PlatformHarness.ManagementCaller caller,
        string url,
        CancellationToken cancellationToken
    )
    {
        var answer = await caller.GetAsync(url, cancellationToken);
        Assert.True(
            answer.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{url}: {(int)answer.Status}: {answer.Body}"
        );
    }

    private static async ValueTask RemoveTeamAdministratorAsync(
        PlatformHarness.ManagementCaller administrator,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        using var removed = await administrator.SendAsync(
            HttpMethod.Delete,
            $"{PlatformHarness.ControlPlaneUrl}/api/teams/{TeamId}/admins/{principalId}",
            body: null,
            cancellationToken
        );
        var body = await removed.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(removed.StatusCode == HttpStatusCode.NoContent, body);
    }

    private static async ValueTask<string> RevokeActiveKeyAsync(
        PlatformHarness.ManagementCaller administrator,
        CancellationToken cancellationToken
    )
    {
        var keyId =
            DecodeJwtPart(administrator.AccessToken, 0).GetProperty("kid").GetString()
            ?? throw new InvalidOperationException("The management token has no key identifier.");
        using var revoked = await administrator.SendAsync(
            HttpMethod.Post,
            $"{PlatformHarness.ControlPlaneUrl}/api/platform/management-keys/{Uri.EscapeDataString(keyId)}/revoke",
            body: null,
            cancellationToken
        );
        var body = await revoked.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(revoked.StatusCode == HttpStatusCode.OK, body);
        return keyId;
    }

    #endregion

    #region Revoked role

    [Fact]
    public async Task RevokedTeamAdmin_WhenTheRoleIsRemoved_StopsWorkingInBothCellsWithinAPollOfThePublish()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        var (caller, principalId) = await CreateTeamAdministratorAsync(
            platform,
            administrator,
            timeout.Token
        );

        await RemoveTeamAdministratorAsync(administrator, principalId, timeout.Token);

        await WaitUntilRefusedAsync(
            platform,
            caller,
            $"{PlatformHarness.CellAUrl}/api/teams/{TeamId}/tenants",
            timeout.Token
        );
        await WaitUntilRefusedAsync(
            platform,
            caller,
            $"{PlatformHarness.CellBUrl}/api/teams/{TeamId}/tenants",
            timeout.Token
        );
        await WaitUntilRefusedAsync(
            platform,
            caller,
            $"{PlatformHarness.ControlPlaneUrl}/api/teams/{TeamId}/tenants",
            timeout.Token
        );
    }

    [Fact]
    public async Task RevokedTeamAdmin_WhenTheControlPlaneStopsRightAfterTheCommit_StopsWorkingInBothCellsOnceItIsBack()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        var (caller, principalId) = await CreateTeamAdministratorAsync(
            platform,
            administrator,
            timeout.Token
        );

        await RemoveTeamAdministratorAsync(administrator, principalId, timeout.Token);
        platform.Router.Stop(PlatformHarness.ControlPlaneName);

        try
        {
            // The commit is durable in the control-plane cell's outbox, so the change reaches the cells whether or not
            // the cell answers requests while it publishes.
            await WaitUntilRefusedAsync(
                platform,
                caller,
                $"{PlatformHarness.CellAUrl}/api/teams/{TeamId}/tenants",
                timeout.Token
            );
            await WaitUntilRefusedAsync(
                platform,
                caller,
                $"{PlatformHarness.CellBUrl}/api/teams/{TeamId}/tenants",
                timeout.Token
            );
        }
        finally
        {
            platform.Router.Start(PlatformHarness.ControlPlaneName);
        }

        await WaitUntilRefusedAsync(
            platform,
            caller,
            $"{PlatformHarness.ControlPlaneUrl}/api/teams/{TeamId}/tenants",
            timeout.Token
        );
    }

    [Fact]
    public async Task DisabledGlobalAdmin_WhenTheDirectoryIsStopped_CannotSignInAndItsTokenIsBoundedByItsLifetime()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await platform.RecoverGlobalAdministratorAsync(
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        var caller = await SignInControlPlaneUserAsync(
            platform,
            administrator,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        foreach (var cellName in new[] { PlatformHarness.CellAName, PlatformHarness.CellBName })
        {
            await WaitUntilAcceptedAsync(
                platform,
                caller,
                $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants",
                timeout.Token
            );
        }

        platform.Router.Stop(PlatformHarness.DirectoryName);
        await platform.DisableLocalAccountAsync(
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            GlobalAdminUserName,
            timeout.Token
        );

        // The control-plane cell issues no new token to the disabled account.
        using var browser = platform.CreateBrowser(PlatformHarness.ControlPlaneName);
        using var login = await InteractiveLogin.SubmitLoginAsync(
            browser,
            GlobalAdminUserName,
            GlobalAdminPassword,
            "/foo",
            timeout.Token
        );
        Assert.Null(InteractiveLogin.ReadSessionCookie(login));

        // With the directory unreachable no cell learns of the change, so the token it already holds is the whole
        // exposure, and its lifetime bounds it.
        var claims = DecodeJwtPart(caller.AccessToken, 1);
        var issuedAt = claims.GetProperty("iat").GetInt64();
        var expires = claims.GetProperty("exp").GetInt64();
        Assert.InRange(expires - issuedAt, 1, 3600);
        Assert.True(expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    #endregion

    #region Revoked management key

    [Fact]
    public async Task RevokedManagementKey_WhenTheActiveKeyIsRevoked_IsRefusedAtOnceInTheControlPlaneAndWithinAPollInTheCells()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

        await RevokeActiveKeyAsync(administrator, timeout.Token);

        await AssertRefusedAsync(
            administrator,
            $"{PlatformHarness.ControlPlaneUrl}/api/tenants",
            timeout.Token
        );
        await WaitUntilRefusedAsync(
            platform,
            administrator,
            $"{PlatformHarness.CellAUrl}/api/tenants",
            timeout.Token
        );
        await WaitUntilRefusedAsync(
            platform,
            administrator,
            $"{PlatformHarness.CellBUrl}/api/tenants",
            timeout.Token
        );

        var successor = await platform.CreateAdministratorAsync(timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellAName, successor, timeout.Token);
        await platform.WaitForReplicaAsync(PlatformHarness.CellBName, successor, timeout.Token);
    }

    [Fact]
    public async Task RevokedManagementKey_WhenACellIsCutOff_TheOfflineCommandRefusesTheKeyThere()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

        platform.Router.Stop(PlatformHarness.DirectoryName);
        var keyId = await RevokeActiveKeyAsync(administrator, timeout.Token);

        await AssertRefusedAsync(
            administrator,
            $"{PlatformHarness.ControlPlaneUrl}/api/tenants",
            timeout.Token
        );

        // The cut-off cells hold the key they replicated, so they still accept it.
        foreach (var cellName in new[] { PlatformHarness.CellAName, PlatformHarness.CellBName })
        {
            var answer = await administrator.GetAsync(
                $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants",
                timeout.Token
            );
            Assert.True(answer.Status == HttpStatusCode.OK, answer.Body);
        }

        var command = RevokeManagementKeyCommandLine.Parse([
            RevokeManagementKeyCommandLine.CommandName,
            keyId,
        ]);
        Assert.NotNull(command);
        Assert.Equal(0, await command.RunAsync(platform.CellB.Services, timeout.Token));

        await WaitUntilRefusedAsync(
            platform,
            administrator,
            $"{PlatformHarness.CellBUrl}/api/tenants",
            timeout.Token
        );
        var cellA = await administrator.GetAsync(
            $"{PlatformHarness.CellAUrl}/api/tenants",
            timeout.Token
        );
        Assert.True(cellA.Status == HttpStatusCode.OK, cellA.Body);

        platform.Router.Start(PlatformHarness.DirectoryName);

        await WaitUntilRefusedAsync(
            platform,
            administrator,
            $"{PlatformHarness.CellAUrl}/api/tenants",
            timeout.Token
        );
    }

    #endregion

    #region Token binding

    [Fact]
    public async Task ManagementToken_WhenItsProofNamesAnotherCell_IsRefusedAndABearerTokenIsRefusedEverywhere()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

        var accepted = await administrator.GetAsync(
            $"{PlatformHarness.CellAUrl}/api/tenants",
            timeout.Token
        );
        Assert.True(accepted.Status == HttpStatusCode.OK, accepted.Body);

        var proofForA = $"{PlatformHarness.CellAUrl}/api/tenants";
        foreach (var other in new[] { PlatformHarness.CellBUrl, PlatformHarness.ControlPlaneUrl })
        {
            var status = await administrator.GetWithProofForAsync(
                proofForA,
                $"{other}/api/tenants",
                timeout.Token
            );
            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        foreach (
            var cellName in new[]
            {
                PlatformHarness.ControlPlaneName,
                PlatformHarness.CellAName,
                PlatformHarness.CellBName,
            }
        )
        {
            var status = await administrator.GetAsBearerAsync(
                $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants",
                timeout.Token
            );
            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }
    }

    #endregion
}
