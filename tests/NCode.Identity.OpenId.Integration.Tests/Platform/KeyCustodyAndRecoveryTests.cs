// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Tenants.Keys;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Platform;

/// <summary>
/// Proves the topology of a cell's own custody and recovery: a cell rolls its tenants' keys over and deletes their
/// secrets while the directory is down and never changes the keys of a tenant that is moving, a cell administrator
/// lists and opens only the tenants of its cell, and offline recovery restores an administrator and a sign-in that a
/// setting blocks.
/// </summary>
public class KeyCustodyAndRecoveryTests
{
    private const string TeamId = "acme";
    private const string SignInUserName = "alice";
    private const string SignInPassword = "alice-has-a-long-passphrase-1";
    private const string CellAdminUserName = "cell-admin";
    private const string CellAdminPassword = "cell-admin-has-a-long-passphrase-1";
    private const string GlobalAdminUserName = "global-admin";
    private const string GlobalAdminPassword = "global-admin-has-a-long-passphrase-1";

    private static CancellationTokenSource CreateTimeout() => new(TimeSpan.FromMinutes(3));

    #region Scaffolding

    private sealed record SecretView(
        string SecretId,
        string? Use,
        string? Algorithm,
        DateTimeOffset ActivatesWhen,
        DateTimeOffset RetiresWhen,
        DateTimeOffset ExpiresWhen,
        string ConcurrencyToken
    );

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

    private static JsonElement DecodeJwtPart(string token, int index) =>
        JsonElement.Parse(
            SecureEncoding.UTF8.GetString(Base64Url.DecodeFromChars(token.Split('.')[index]))
        );

    private static async ValueTask<List<SecretView>> GetSecretsAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var answer = await caller.GetAsync(
            $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants/{tenantId}/secrets",
            cancellationToken
        );
        Assert.True(answer.Status == HttpStatusCode.OK, answer.Body);

        return
        [
            .. JsonElement
                .Parse(answer.Body)
                .GetProperty("secrets")
                .EnumerateArray()
                .Select(secret => new SecretView(
                    secret.GetProperty("secretId").GetString() ?? string.Empty,
                    secret.GetProperty("use").GetString(),
                    secret.GetProperty("algorithm").GetString(),
                    secret.GetProperty("activatesWhen").GetDateTimeOffset(),
                    secret.GetProperty("retiresWhen").GetDateTimeOffset(),
                    secret.GetProperty("expiresWhen").GetDateTimeOffset(),
                    secret.GetProperty("concurrencyToken").GetString() ?? string.Empty
                )),
        ];
    }

    private static async ValueTask UpdateLifecycleAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string tenantId,
        SecretView secret,
        DateTimeOffset retiresWhen,
        CancellationToken cancellationToken
    )
    {
        using var response = await caller.SendAsync(
            HttpMethod.Put,
            $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants/{tenantId}/secrets/{secret.SecretId}",
            new
            {
                use = secret.Use,
                algorithm = secret.Algorithm,
                activatesWhen = secret.ActivatesWhen,
                retiresWhen,
                expiresWhen = retiresWhen.AddDays(7),
            },
            secret.ConcurrencyToken,
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, body);
    }

    private static async ValueTask<HttpStatusCode> DeleteSecretAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string tenantId,
        string secretId,
        CancellationToken cancellationToken
    )
    {
        using var response = await caller.SendAsync(
            HttpMethod.Delete,
            $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants/{tenantId}/secrets/{secretId}",
            body: null,
            cancellationToken
        );
        return response.StatusCode;
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

    private static async ValueTask PatchSettingsAsync(
        PlatformHarness.ManagementCaller caller,
        string cellName,
        string tenantId,
        IReadOnlyDictionary<string, object> settings,
        CancellationToken cancellationToken
    )
    {
        var operations = settings
            .Select(setting => new
            {
                op = "add",
                path = $"/{setting.Key}",
                value = setting.Value,
            })
            .ToArray();
        using var response = await caller.SendJsonPatchAsync(
            $"{PlatformHarness.GetCellUrl(cellName)}/api/tenants/{tenantId}/settings",
            operations,
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, body);
    }

    private static async ValueTask<PlatformHarness.ManagementCaller> SignInAsync(
        PlatformHarness platform,
        string cellName,
        PlatformHarness.UserClient client,
        string userName,
        string password,
        CancellationToken cancellationToken
    ) =>
        await platform.CreateUserCallerAsync(
            cellName,
            client,
            userName,
            password,
            cancellationToken
        );

    // The offline command changes the cell's database and announces the change, so the sign-in is tried until the
    // running cell has applied the announcement.
    private static async ValueTask<PlatformHarness.ManagementCaller> SignInOnceRefreshedAsync(
        PlatformHarness platform,
        string cellName,
        PlatformHarness.UserClient client,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        PlatformHarness.ManagementCaller? caller = null;
        await platform.WaitUntilAsync(
            async () =>
            {
                try
                {
                    caller = await SignInAsync(
                        platform,
                        cellName,
                        client,
                        userName,
                        password,
                        cancellationToken
                    );
                    return true;
                }
                catch (Xunit.Sdk.XunitException)
                {
                    return false;
                }
            },
            $"'{userName}' signs in to '{cellName}'",
            cancellationToken
        );
        return caller ?? throw new InvalidOperationException("The sign-in did not complete.");
    }

    private static async ValueTask AssertAcceptedAsync(
        PlatformHarness.ManagementCaller caller,
        string url,
        CancellationToken cancellationToken
    )
    {
        var answer = await caller.GetAsync(url, cancellationToken);
        Assert.True(answer.Status == HttpStatusCode.OK, $"{url}: {answer.Body}");
    }

    private static async ValueTask AssertNoLoginAsync(
        PlatformHarness platform,
        string cellName,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        using var browser = platform.CreateBrowser(cellName);
        using var login = await InteractiveLogin.SubmitLoginAsync(
            browser,
            userName,
            password,
            "/foo",
            cancellationToken
        );
        Assert.Null(InteractiveLogin.ReadSessionCookie(login));
    }

    #endregion

    #region Key custody (#944)

    [Fact]
    public async Task DirectoryStopped_WhenAKeyRetiresAndItsSuccessorActivates_SignInKeepsWorkingWithTheSuccessor()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await platform.SeedLocalAccountAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            SignInUserName,
            SignInPassword,
            timeout.Token
        );
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        var before = await SignInAsync(
            platform,
            PlatformHarness.CellAName,
            userClient,
            SignInUserName,
            SignInPassword,
            timeout.Token
        );
        var header = DecodeJwtPart(before.AccessToken, 0);
        var oldKeyId = header.GetProperty("kid").GetString();
        var algorithm = header.GetProperty("alg").GetString();

        platform.Router.Stop(PlatformHarness.DirectoryName);

        // The guard reads the cell's own row, so a tenant the cell holds may change its keys with no directory.
        using (var scope = platform.CellA.Services.CreateScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<ITenantKeyCustodyGuard>();
            Assert.True(
                await guard.MayChangeKeysAsync(PlatformHarness.CellATenantId, timeout.Token)
            );
        }

        var secrets = await GetSecretsAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        var oldKey = Assert.Single(secrets, secret => secret.SecretId == oldKeyId);
        var retiresWhen = DateTimeOffset.UtcNow.AddSeconds(4);
        await UpdateLifecycleAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            oldKey,
            retiresWhen,
            timeout.Token
        );

        var afterChange = await GetSecretsAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        var successor = Assert.Single(
            afterChange,
            secret => secret.Algorithm == algorithm && secret.SecretId != oldKeyId
        );
        Assert.Equal(retiresWhen, successor.ActivatesWhen);

        var wait = retiresWhen + TimeSpan.FromSeconds(1) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, timeout.Token);

        var after = await SignInAsync(
            platform,
            PlatformHarness.CellAName,
            userClient,
            SignInUserName,
            SignInPassword,
            timeout.Token
        );
        Assert.Equal(
            successor.SecretId,
            DecodeJwtPart(after.AccessToken, 0).GetProperty("kid").GetString()
        );
    }

    [Fact]
    public async Task DirectoryStopped_WhenATenantSecretIsDeleted_ItIsDeletedUnlessTheTenantIsFenced()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);

        platform.Router.Stop(PlatformHarness.DirectoryName);

        var secrets = await GetSecretsAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        Assert.True(secrets.Count >= 3);
        var deleted = secrets[^1];
        var fenced = secrets[^2];

        Assert.Equal(
            HttpStatusCode.NoContent,
            await DeleteSecretAsync(
                administrator,
                PlatformHarness.CellAName,
                PlatformHarness.CellATenantId,
                deleted.SecretId,
                timeout.Token
            )
        );

        await platform.SetKeyCustodyAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            TenantKeyCustody.Outgoing,
            PlatformHarness.CellBName,
            timeout.Token
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            await DeleteSecretAsync(
                administrator,
                PlatformHarness.CellAName,
                PlatformHarness.CellATenantId,
                fenced.SecretId,
                timeout.Token
            )
        );

        var remaining = await GetSecretsAsync(
            administrator,
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        Assert.DoesNotContain(remaining, secret => secret.SecretId == deleted.SecretId);
        Assert.Contains(remaining, secret => secret.SecretId == fenced.SecretId);
    }

    [Fact]
    public async Task TenantMidMove_WhenItsKeysNearRetirement_TheSourceCellNeverChangesThemUntilItHoldsTheTenantAgain()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(
            new Dictionary<string, string?> { ["OpenId:Tenant:KeyRollover:Interval"] = "00:00:02" },
            timeout.Token
        );
        await StartReplicatedPlatformAsync(platform, timeout.Token);
        await platform.SetKeyCustodyAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            TenantKeyCustody.Outgoing,
            PlatformHarness.CellBName,
            timeout.Token
        );

        var provisioned = await platform.GetTenantSecretsAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        var retiring = provisioned[0];
        await platform.SetTenantSecretLifecycleAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            retiring.SecretId,
            retiring.ActivatesWhen,
            DateTimeOffset.UtcNow.AddSeconds(2),
            DateTimeOffset.UtcNow.AddDays(7),
            timeout.Token
        );

        // Several passes of the rollover run while the tenant is fenced, and none changes a key.
        await Task.Delay(TimeSpan.FromSeconds(8), timeout.Token);
        var fencedSecrets = await platform.GetTenantSecretsAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            timeout.Token
        );
        Assert.Equal(
            provisioned.Select(secret => secret.SecretId).Order(StringComparer.Ordinal),
            fencedSecrets.Select(secret => secret.SecretId).Order(StringComparer.Ordinal)
        );

        // The fence alone held the change back: once the cell holds the tenant, the next pass generates the successor.
        await platform.SetKeyCustodyAsync(
            PlatformHarness.CellAName,
            PlatformHarness.CellATenantId,
            TenantKeyCustody.Held,
            otherCell: null,
            timeout.Token
        );
        await platform.WaitUntilAsync(
            async () =>
            {
                var secrets = await platform.GetTenantSecretsAsync(
                    PlatformHarness.CellAName,
                    PlatformHarness.CellATenantId,
                    timeout.Token
                );
                return secrets.Count > provisioned.Count;
            },
            "the rollover generates a successor once the tenant is held again",
            timeout.Token
        );
    }

    #endregion

    #region Cell listing (#945)

    [Fact]
    public async Task CellAdministrator_WhenItListsAndOpensTenants_SeesOnlyTheTenantsOfItsCell()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await CreateTeamAsync(platform, administrator, timeout.Token);
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

        using (
            var created = await administrator.SendAsync(
                HttpMethod.Post,
                $"{PlatformHarness.CellBUrl}/api/teams/{TeamId}/tenants",
                new
                {
                    tenantId = "beta-extra",
                    displayName = "beta-extra",
                    isDisabled = false,
                    settings = new { },
                },
                timeout.Token
            )
        )
        {
            var body = await created.Content.ReadAsStringAsync(timeout.Token);
            Assert.True(created.StatusCode == HttpStatusCode.Created, body);
        }

        var cellAdministrator = await SignInAsync(
            platform,
            PlatformHarness.CellBName,
            userClient,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );

        var listed = await ListTenantIdsAsync(
            cellAdministrator,
            PlatformHarness.CellBName,
            "/api/cell/tenants",
            timeout.Token
        );
        Assert.Contains(PlatformHarness.CellBTenantId, listed);
        Assert.Contains("beta-extra", listed);
        Assert.DoesNotContain(PlatformHarness.CellATenantId, listed);

        await AssertAcceptedAsync(
            cellAdministrator,
            $"{PlatformHarness.CellBUrl}/api/tenants/beta-extra",
            timeout.Token
        );

        // A tenant of another cell is outside the administrator's authority, whichever cell is asked.
        var otherTenant = await cellAdministrator.GetAsync(
            $"{PlatformHarness.CellBUrl}/api/tenants/{PlatformHarness.CellATenantId}",
            timeout.Token
        );
        Assert.True(
            otherTenant.Status == HttpStatusCode.Forbidden,
            $"{(int)otherTenant.Status}: {otherTenant.Body}"
        );
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

    #region Recovery (#945)

    [Fact]
    public async Task RecoverAdmin_WhenEveryPlatformAdministratorIsLockedOut_RestoresOne()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        Assert.Equal(
            0,
            await platform.RunRecoverAdminCommandAsync(
                PlatformHarness.ControlPlaneName,
                GlobalAdminUserName,
                GlobalAdminPassword,
                timeout.Token
            )
        );
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            timeout.Token
        );
        var signedIn = await SignInAsync(
            platform,
            PlatformHarness.ControlPlaneName,
            userClient,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        await AssertAcceptedAsync(
            signedIn,
            $"{PlatformHarness.ControlPlaneUrl}/api/tenants",
            timeout.Token
        );

        await platform.DisableLocalAccountAsync(
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            GlobalAdminUserName,
            timeout.Token
        );
        await AssertNoLoginAsync(
            platform,
            PlatformHarness.ControlPlaneName,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );

        Assert.Equal(
            0,
            await platform.RunRecoverAdminCommandAsync(
                PlatformHarness.ControlPlaneName,
                GlobalAdminUserName,
                GlobalAdminPassword,
                timeout.Token
            )
        );

        var restored = await SignInAsync(
            platform,
            PlatformHarness.ControlPlaneName,
            userClient,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        await AssertAcceptedAsync(
            restored,
            $"{PlatformHarness.ControlPlaneUrl}/api/tenants",
            timeout.Token
        );
    }

    [Fact]
    public async Task RecoverAdmin_WhenTheCellAdministratorIsLockedOut_RestoresOneInTheCell()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        Assert.Equal(
            0,
            await platform.RunRecoverAdminCommandAsync(
                PlatformHarness.CellBName,
                CellAdminUserName,
                CellAdminPassword,
                timeout.Token
            )
        );
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.CellBName,
            PlatformHarness.CellBTenantId,
            timeout.Token
        );

        await platform.DisableLocalAccountAsync(
            PlatformHarness.CellBName,
            PlatformHarness.CellBTenantId,
            CellAdminUserName,
            timeout.Token
        );
        await AssertNoLoginAsync(
            platform,
            PlatformHarness.CellBName,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );

        Assert.Equal(
            0,
            await platform.RunRecoverAdminCommandAsync(
                PlatformHarness.CellBName,
                CellAdminUserName,
                CellAdminPassword,
                timeout.Token
            )
        );

        var restored = await SignInAsync(
            platform,
            PlatformHarness.CellBName,
            userClient,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        Assert.Contains(
            PlatformHarness.CellBTenantId,
            await ListTenantIdsAsync(
                restored,
                PlatformHarness.CellBName,
                "/api/cell/tenants",
                timeout.Token
            )
        );
    }

    [Fact]
    public async Task RecoverSignIn_WhenASettingBlocksTheControlPlaneTenant_ATokenIsAcquired()
    {
        using var timeout = CreateTimeout();
        await using var platform = await PlatformHarness.StartAsync(timeout.Token);
        var administrator = await StartReplicatedPlatformAsync(platform, timeout.Token);
        await platform.RecoverGlobalAdministratorAsync(
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        var userClient = await PlatformHarness.RegisterUserClientAsync(
            administrator,
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            timeout.Token
        );
        await SignInAsync(
            platform,
            PlatformHarness.ControlPlaneName,
            userClient,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );

        await PatchSettingsAsync(
            administrator,
            PlatformHarness.ControlPlaneName,
            PlatformHarness.ControlPlaneTenantId,
            new Dictionary<string, object> { ["allowed_connections"] = new[] { "elsewhere" } },
            timeout.Token
        );
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await SignInAsync(
                platform,
                PlatformHarness.ControlPlaneName,
                userClient,
                GlobalAdminUserName,
                GlobalAdminPassword,
                timeout.Token
            )
        );

        Assert.Equal(
            0,
            await platform.RunRecoverSignInCommandAsync(
                PlatformHarness.ControlPlaneName,
                timeout.Token
            )
        );

        var recovered = await SignInOnceRefreshedAsync(
            platform,
            PlatformHarness.ControlPlaneName,
            userClient,
            GlobalAdminUserName,
            GlobalAdminPassword,
            timeout.Token
        );
        await AssertAcceptedAsync(
            recovered,
            $"{PlatformHarness.ControlPlaneUrl}/api/tenants",
            timeout.Token
        );
    }

    [Fact]
    public async Task RecoverSignIn_WhenSettingsBlockTheManagementTenantOfACell_ATokenIsAcquired()
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
        await SignInAsync(
            platform,
            PlatformHarness.CellBName,
            userClient,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );

        await PatchSettingsAsync(
            administrator,
            PlatformHarness.CellBName,
            PlatformHarness.CellBTenantId,
            new Dictionary<string, object>
            {
                ["email_mfa_required"] = true,
                ["password_min_length"] = 64,
                ["password_max_length"] = 64,
            },
            timeout.Token
        );
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await SignInAsync(
                platform,
                PlatformHarness.CellBName,
                userClient,
                CellAdminUserName,
                CellAdminPassword,
                timeout.Token
            )
        );

        Assert.Equal(
            0,
            await platform.RunRecoverSignInCommandAsync(PlatformHarness.CellBName, timeout.Token)
        );

        var recovered = await SignInOnceRefreshedAsync(
            platform,
            PlatformHarness.CellBName,
            userClient,
            CellAdminUserName,
            CellAdminPassword,
            timeout.Token
        );
        Assert.Contains(
            PlatformHarness.CellBTenantId,
            await ListTenantIdsAsync(
                recovered,
                PlatformHarness.CellBName,
                "/api/cell/tenants",
                timeout.Token
            )
        );
    }

    #endregion
}
