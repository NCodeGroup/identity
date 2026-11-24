// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Management.Contracts.Connections;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

// A tenant signs users in through connections: an account belongs to one connection, and a client's
// allowed_connections decides which connections it accepts (ADR-1535).
public class ConnectionLifecycleTests
{
    private const string OpenClientId = "it-connection-open-client";
    private const string RestrictedClientId = "it-connection-restricted-client";
    private const string ClientSecret = "confidential-secret-value";
    private const string SecondConnectionName = "second";
    private const string Email = "shared@example.test";
    private const string LocalPassword = "local-connection-passphrase";
    private const string SecondPassword = "second-connection-passphrase";
    private const string ConnectionsPath =
        $"/api/tenants/{HostApplicationFactory.TenantId}/connections";

    #region Sign-in Tests

    [Fact]
    public async Task PostToken_WhenTheSameEmailExistsInTwoConnections_SignsInThroughTheNamedConnection()
    {
        using var factory = new BootstrapAdminApplicationFactory();
        var (localConnectionId, secondConnectionId) = await SeedTwoConnectionsAsync(
            factory,
            CancellationToken.None
        );
        await SeedAccountAsync(factory, localConnectionId, LocalPassword, CancellationToken.None);
        await SeedAccountAsync(factory, secondConnectionId, SecondPassword, CancellationToken.None);
        using var client = factory.CreateClient();

        var (localStatus, localBody) = await PostPasswordGrantAsync(
            client,
            OpenClientId,
            "local",
            LocalPassword,
            CancellationToken.None
        );
        var (secondStatus, secondBody) = await PostPasswordGrantAsync(
            client,
            OpenClientId,
            SecondConnectionName,
            SecondPassword,
            CancellationToken.None
        );
        var (crossedStatus, _) = await PostPasswordGrantAsync(
            client,
            OpenClientId,
            "local",
            SecondPassword,
            CancellationToken.None
        );

        Assert.True(localStatus == HttpStatusCode.OK, localBody);
        Assert.True(secondStatus == HttpStatusCode.OK, secondBody);
        Assert.NotEqual(HttpStatusCode.OK, crossedStatus);
    }

    [Fact]
    public async Task PostToken_WhenTheClientDoesNotAllowTheRequestedConnection_ReturnsInvalidRequest()
    {
        using var factory = new BootstrapAdminApplicationFactory();
        var (localConnectionId, secondConnectionId) = await SeedTwoConnectionsAsync(
            factory,
            CancellationToken.None
        );
        await SeedAccountAsync(factory, localConnectionId, LocalPassword, CancellationToken.None);
        await SeedAccountAsync(factory, secondConnectionId, SecondPassword, CancellationToken.None);
        using var client = factory.CreateClient();

        var (allowedStatus, allowedBody) = await PostPasswordGrantAsync(
            client,
            RestrictedClientId,
            "local",
            LocalPassword,
            CancellationToken.None
        );
        var (refusedStatus, refusedBody) = await PostPasswordGrantAsync(
            client,
            RestrictedClientId,
            SecondConnectionName,
            SecondPassword,
            CancellationToken.None
        );

        Assert.True(allowedStatus == HttpStatusCode.OK, allowedBody);
        Assert.Equal(HttpStatusCode.BadRequest, refusedStatus);
        Assert.Equal(
            "invalid_request",
            JsonElement.Parse(refusedBody).GetProperty("error").GetString()
        );
    }

    #endregion

    #region Scaffolding

    private static async ValueTask<(
        string LocalConnectionId,
        string SecondConnectionId
    )> SeedTwoConnectionsAsync(
        BootstrapAdminApplicationFactory factory,
        CancellationToken cancellationToken
    )
    {
        using var administrator = await factory.CreateAdministratorClientAsync(cancellationToken);

        await factory.SeedConfidentialClientAsync(
            OpenClientId,
            ClientSecret,
            [OpenIdProtocol.GrantTypes.Password],
            cancellationToken
        );
        await factory.SeedSharedSecretClientWithSettingsAsync(
            RestrictedClientId,
            ClientSecret,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretPost,
            new Dictionary<string, object>
            {
                ["grant_types_supported"] = new[] { OpenIdProtocol.GrantTypes.Password },
                ["allowed_connections"] = new[] { "local" },
            },
            [],
            cancellationToken
        );
        foreach (var clientId in new[] { OpenClientId, RestrictedClientId })
        {
            await factory.SeedResourceServerWithClientGrantAsync(
                clientId,
                $"{clientId}-api",
                $"https://{clientId}.integration.test",
                [TestPlatformSettingsProvider.ApiScope],
                cancellationToken
            );
        }

        using var response = await administrator.PostAsJsonAsync(
            ConnectionsPath,
            new CreateConnectionRequest
            {
                Name = SecondConnectionName,
                DisplayName = "Second connection",
                Strategy = "local",
                Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            },
            cancellationToken
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ConnectionResource>(
            cancellationToken
        );
        Assert.NotNull(created);

        using var scope = factory.CreateTenantScope();
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var localConnectionId = await HostApplicationFactory.GetLocalConnectionIdAsync(
            storeManager,
            cancellationToken
        );
        return (localConnectionId, created.ConnectionId);
    }

    private static async ValueTask SeedAccountAsync(
        HostApplicationFactory factory,
        string connectionId,
        string password,
        CancellationToken cancellationToken
    )
    {
        using var scope = factory.CreateTenantScope();
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var connection = await storeManager
            .GetStore<IConnectionStore>()
            .GetOrDefaultAsync(connectionId, cancellationToken);
        Assert.NotNull(connection);
        await HostApplicationFactory
            .GetLocalAccountStore(scope.ServiceProvider, storeManager, connection)
            .AddAsync(
                new PersistedLocalAccount
                {
                    TenantId = HostApplicationFactory.TenantId,
                    ConnectionId = connectionId,
                    LocalAccountId = Guid.NewGuid().ToString("N"),
                    UserName = null,
                    DisplayName = Email,
                    Email = Email,
                    EmailVerified = true,
                    IsEnabled = true,
                    Claims = [],
                    ConcurrencyToken = string.Empty,
                },
                SecureEncoding.UTF8.GetBytes(password),
                cancellationToken
            );
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    private static async ValueTask<(HttpStatusCode StatusCode, string Body)> PostPasswordGrantAsync(
        HttpClient client,
        string clientId,
        string connection,
        string password,
        CancellationToken cancellationToken
    )
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = OpenIdProtocol.GrantTypes.Password,
                ["client_id"] = clientId,
                ["client_secret"] = ClientSecret,
                ["username"] = Email,
                ["password"] = password,
                ["connection"] = connection,
                ["scope"] = TestPlatformSettingsProvider.ApiScope,
            }
        );

        using var response = await client.PostAsync("/oauth2/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, body);
    }

    #endregion
}
