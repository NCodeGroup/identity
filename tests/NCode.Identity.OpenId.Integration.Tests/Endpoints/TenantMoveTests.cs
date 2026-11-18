// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.RemoteKeys;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

/// <summary>
/// Moves a disabled tenant between two cells through the management API of each: the receiving cell issues a transfer
/// key, the sending cell exports the tenant under it, the receiving cell imports the package, the sending cell releases
/// what it moved, and the receiving cell enables the tenant. The cells share one global directory and one key store,
/// each cell owns its database, and a tenant moves only within its region.
/// </summary>
public class TenantMoveTests
{
    private const string TenantId = "acme";
    private const string UserName = "move-user";
    private const string Password = "move-user-password-long";
    private const string CellA = "cell-a";
    private const string CellB = "cell-b";
    private const string CellC = "cell-c";
    private const string CellD = "cell-d";
    private const string RegionEurope = "eu";
    private const string RegionAmerica = "us";

    #region Move Tests

    [Fact]
    public async Task Move_WhenTheTenantIsExportedImportedAndReleased_ServesTheTenantFromTheReceivingCell()
    {
        using var provider = new TestRemoteKeyProvider();
        var directory = CreateDirectory();
        using var cellA = new CellApplicationFactory(provider, directory, CellA, RegionEurope);
        using var cellB = new CellApplicationFactory(provider, directory, CellB, RegionEurope);
        using var administratorA = await cellA.CreateAdministratorClientAsync(
            CancellationToken.None
        );
        using var administratorB = await cellB.CreateAdministratorClientAsync(
            CancellationToken.None
        );

        await CreateTenantAsync(administratorA, CancellationToken.None);
        var clientId = await CreateClientAsync(administratorA, CancellationToken.None);
        var clientSecret = await CreateClientSecretAsync(
            administratorA,
            clientId,
            CancellationToken.None
        );
        await CreateLocalAccountAsync(administratorA, CancellationToken.None);
        var keyIds = await GetRemoteKeyIdsAsync(cellA, CancellationToken.None);
        Assert.NotEmpty(keyIds);
        Assert.Empty(keyIds.Except(await ListKeyIdsAsync(provider, CellA, CancellationToken.None)));
        Assert.Empty(
            (await ListKeyIdsAsync(provider, CellB, CancellationToken.None)).Intersect(keyIds)
        );

        await SetDisabledAsync(administratorA, isDisabled: true, CancellationToken.None);
        var transferKey = await CreateTransferKeyAsync(administratorB, CancellationToken.None);
        var package = await ExportAsync(
            administratorA,
            transferKey.Jwk,
            CellB,
            CancellationToken.None
        );

        using var importResponse = await administratorB.PostAsJsonAsync(
            "/api/tenant-imports",
            new { kid = transferKey.Kid, package },
            CancellationToken.None
        );
        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.True(importResponse.StatusCode == HttpStatusCode.OK, importBody);
        var imported = JsonElement.Parse(importBody);
        Assert.Equal(TenantId, imported.GetProperty("tenantId").GetString());
        Assert.Equal(CellA, imported.GetProperty("sourceCell").GetString());

        using var releaseResponse = await administratorA.PostAsync(
            $"/api/tenants/{TenantId}/release-moved",
            content: null,
            CancellationToken.None
        );
        var releaseBody = await releaseResponse.Content.ReadAsStringAsync();
        Assert.True(releaseResponse.StatusCode == HttpStatusCode.NoContent, releaseBody);

        await SetDisabledAsync(administratorB, isDisabled: false, CancellationToken.None);

        var placement = await directory.GetPlacementOrDefaultAsync(
            TenantId,
            CancellationToken.None
        );
        Assert.NotNull(placement);
        Assert.Equal(CellB, placement.Cell);

        using var tokenResponse = await PostTokenAsync(
            cellB,
            clientId,
            clientSecret,
            CancellationToken.None
        );
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.StatusCode == HttpStatusCode.OK, tokenBody);
        Assert.False(
            string.IsNullOrEmpty(
                JsonElement.Parse(tokenBody).GetProperty("access_token").GetString()
            ),
            tokenBody
        );

        Assert.True(await VerifyPasswordAsync(cellB, CancellationToken.None));

        Assert.Empty(keyIds.Except(await ListKeyIdsAsync(provider, CellB, CancellationToken.None)));
        Assert.Empty(
            (await ListKeyIdsAsync(provider, CellA, CancellationToken.None)).Intersect(keyIds)
        );
        Assert.Empty(provider.DestroyedKeyIds);

        Assert.False(await HasTenantRowAsync(cellA, CancellationToken.None));
        Assert.True(await HasTenantRowAsync(cellB, CancellationToken.None));
    }

    [Fact]
    public async Task Import_WhenTheReceivingCellIsNotTheTargetCell_ReturnsUnprocessableEntity()
    {
        using var provider = new TestRemoteKeyProvider();
        var directory = CreateDirectory();
        using var cellA = new CellApplicationFactory(provider, directory, CellA, RegionEurope);
        using var cellB = new CellApplicationFactory(provider, directory, CellB, RegionEurope);
        using var cellD = new CellApplicationFactory(provider, directory, CellD, RegionEurope);
        using var administratorA = await cellA.CreateAdministratorClientAsync(
            CancellationToken.None
        );
        using var administratorD = await cellD.CreateAdministratorClientAsync(
            CancellationToken.None
        );

        await CreateTenantAsync(administratorA, CancellationToken.None);
        await SetDisabledAsync(administratorA, isDisabled: true, CancellationToken.None);
        var transferKey = await CreateTransferKeyAsync(administratorD, CancellationToken.None);
        var package = await ExportAsync(
            administratorA,
            transferKey.Jwk,
            CellB,
            CancellationToken.None
        );

        using var importResponse = await administratorD.PostAsJsonAsync(
            "/api/tenant-imports",
            new { kid = transferKey.Kid, package },
            CancellationToken.None
        );

        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.True(importResponse.StatusCode == HttpStatusCode.UnprocessableEntity, importBody);
        Assert.False(await HasTenantRowAsync(cellD, CancellationToken.None));
        var placement = await directory.GetPlacementOrDefaultAsync(
            TenantId,
            CancellationToken.None
        );
        Assert.NotNull(placement);
        Assert.Equal(CellA, placement.Cell);
    }

    [Fact]
    public async Task Export_WhenTheTargetCellIsInAnotherRegion_ReturnsConflict()
    {
        using var provider = new TestRemoteKeyProvider();
        var directory = CreateDirectory();
        using var cellA = new CellApplicationFactory(provider, directory, CellA, RegionEurope);
        using var cellC = new CellApplicationFactory(provider, directory, CellC, RegionAmerica);
        using var administratorA = await cellA.CreateAdministratorClientAsync(
            CancellationToken.None
        );
        using var administratorC = await cellC.CreateAdministratorClientAsync(
            CancellationToken.None
        );

        await CreateTenantAsync(administratorA, CancellationToken.None);
        await SetDisabledAsync(administratorA, isDisabled: true, CancellationToken.None);
        var transferKey = await CreateTransferKeyAsync(administratorC, CancellationToken.None);

        using var exportResponse = await administratorA.PostAsJsonAsync(
            $"/api/tenants/{TenantId}/export",
            new { jwk = transferKey.Jwk, targetCell = CellC },
            CancellationToken.None
        );

        var exportBody = await exportResponse.Content.ReadAsStringAsync();
        Assert.True(exportResponse.StatusCode == HttpStatusCode.Conflict, exportBody);
        var placement = await directory.GetPlacementOrDefaultAsync(
            TenantId,
            CancellationToken.None
        );
        Assert.NotNull(placement);
        Assert.Equal(CellA, placement.Cell);
    }

    #endregion

    #region Scaffolding

    private static InMemoryTenantDirectory CreateDirectory() =>
        new(
            new Dictionary<string, string>
            {
                [CellA] = RegionEurope,
                [CellB] = RegionEurope,
                [CellC] = RegionAmerica,
                [CellD] = RegionEurope,
            }
        );

    private static async ValueTask CreateTenantAsync(
        HttpClient administrator,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsJsonAsync(
            $"/api/teams/{HostApplicationFactory.TeamId}/tenants",
            new
            {
                tenantId = TenantId,
                displayName = TenantId,
                isDisabled = false,
                settings = new { },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
    }

    private static async ValueTask<string> CreateClientAsync(
        HttpClient administrator,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsJsonAsync(
            $"/api/tenants/{TenantId}/clients",
            new
            {
                isDisabled = false,
                settings = new Dictionary<string, object>
                {
                    ["token_endpoint_auth_method"] = OpenIdProtocol
                        .ClientAuthenticationMethods
                        .ClientSecretPost,
                },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonElement.Parse(body).GetProperty("clientId").GetString()!;
    }

    private static async ValueTask<string> CreateClientSecretAsync(
        HttpClient administrator,
        string clientId,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsJsonAsync(
            $"/api/tenants/{TenantId}/clients/{clientId}/secrets",
            new
            {
                secretType = "symmetric",
                keySizeBits = 256,
                expiresWhen = DateTimeOffset.UtcNow.AddYears(1),
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonElement.Parse(body).GetProperty("secretMaterial").GetString()!;
    }

    private static async ValueTask CreateLocalAccountAsync(
        HttpClient administrator,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsJsonAsync(
            $"/api/tenants/{TenantId}/local-accounts",
            new { userName = UserName, password = Password },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
    }

    private static async ValueTask SetDisabledAsync(
        HttpClient administrator,
        bool isDisabled,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tenants/{TenantId}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            op = "replace",
                            path = "/isDisabled",
                            value = isDisabled,
                        },
                    }
                ),
                SecureEncoding.UTF8,
                "application/json-patch+json"
            ),
        };
        using var response = await administrator.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, body);
    }

    private static async ValueTask<(string Kid, JsonElement Jwk)> CreateTransferKeyAsync(
        HttpClient administrator,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsync(
            "/api/tenant-imports/transfer-keys",
            content: null,
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.IsSuccessStatusCode, body);
        var resource = JsonElement.Parse(body);
        return (resource.GetProperty("kid").GetString()!, resource.GetProperty("jwk").Clone());
    }

    private static async ValueTask<string> ExportAsync(
        HttpClient administrator,
        JsonElement jwk,
        string targetCell,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.PostAsJsonAsync(
            $"/api/tenants/{TenantId}/export",
            new { jwk, targetCell },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonElement.Parse(body).GetProperty("package").GetString()!;
    }

    private static async ValueTask<HttpResponseMessage> PostTokenAsync(
        HostApplicationFactory factory,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken
    )
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth2/token")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                }
            ),
        };
        request.Headers.Host = $"{TenantId}.auth.test";
        return await client.SendAsync(request, cancellationToken);
    }

    private static async ValueTask<List<string>> ListKeyIdsAsync(
        TestRemoteKeyProvider provider,
        string deployment,
        CancellationToken cancellationToken
    )
    {
        var keyIds = new List<string>();
        await foreach (
            var listing in provider.ListKeysAsync(
                new RemoteKeyListRequest { Deployment = deployment },
                cancellationToken
            )
        )
        {
            keyIds.Add(listing.KeyId);
        }

        return keyIds;
    }

    private static async ValueTask<List<string>> GetRemoteKeyIdsAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    )
    {
        using var serviceScope = factory.CreateTenantScope(TenantId);
        await using var storeManager = await serviceScope
            .ServiceProvider.GetRequiredService<IStoreManagerFactory>()
            .CreateAsync(cancellationToken);
        var secrets = await storeManager
            .GetStore<ITenantStore>()
            .GetSecretsAsync(TenantId, cancellationToken);
        var keyIds = new List<string>();
        foreach (var secret in secrets.Value)
        {
            if (
                secret.SecretEncoding == SecretEncodings.Remote
                && RemoteKeyReference.TryParse(secret.EncodedValue, out var reference)
            )
            {
                keyIds.Add(reference.KeyId);
            }
        }

        return keyIds;
    }

    private static async ValueTask<bool> VerifyPasswordAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    )
    {
        using var serviceScope = factory.CreateTenantScope(TenantId);
        await using var storeManager = await serviceScope
            .ServiceProvider.GetRequiredService<IStoreManagerFactory>()
            .CreateAsync(cancellationToken);
        var result = await storeManager
            .GetStore<ILocalAccountStore>()
            .VerifyCredentialAsync(
                UserName,
                SecureEncoding.UTF8.GetBytes(Password),
                (_, _) => ValueTask.FromResult(false),
                cancellationToken
            );
        return result.IsVerified;
    }

    private static async ValueTask<bool> HasTenantRowAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    )
    {
        using var serviceScope = factory.CreateTenantScope(TenantId);
        await using var storeManager = await serviceScope
            .ServiceProvider.GetRequiredService<IStoreManagerFactory>()
            .CreateAsync(cancellationToken);
        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(TenantId, cancellationToken);
        return tenant is not null;
    }

    #endregion
}
