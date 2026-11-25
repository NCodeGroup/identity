// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.GlobalDirectory.Host;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Server.Recovery;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// Runs a whole platform in one process: the global directory, the control-plane cell, a workload cell that serves one
/// static tenant (<see cref="CellAName"/>) and a workload cell that resolves tenants by host and has a management
/// tenant of its own (<see cref="CellBName"/>). Every host is a real host on a <see cref="TestServer"/>, every client
/// library of every host calls the others through one <see cref="PlatformRoutingHandler"/>, and a host is stopped and
/// started again by name. Every wait is bounded, so a defect fails a test instead of hanging it.
/// </summary>
public sealed class PlatformHarness : IAsyncDisposable
{
    /// <summary>The name of the control-plane cell.</summary>
    public const string ControlPlaneName = "control";

    /// <summary>The name of the workload cell that serves one static tenant.</summary>
    public const string CellAName = "alpha";

    /// <summary>The name of the workload cell that resolves tenants by host and has a management tenant.</summary>
    public const string CellBName = "beta";

    /// <summary>The name of the global directory host.</summary>
    public const string DirectoryName = "directory";

    /// <summary>The origin of the global directory.</summary>
    public const string DirectoryUrl = "https://directory.platform.test";

    /// <summary>The origin of the control-plane cell, which is also the issuer of its tenant.</summary>
    public const string ControlPlaneUrl = "https://control.platform.test";

    /// <summary>The origin of cell A, which is also the issuer of its tenant.</summary>
    public const string CellAUrl = "https://alpha.platform.test";

    /// <summary>The origin of cell B, which is also the issuer of its management tenant.</summary>
    public const string CellBUrl = "https://beta.platform.test";

    /// <summary>The identifier of the control-plane tenant.</summary>
    public const string ControlPlaneTenantId = "control-plane";

    /// <summary>The identifier of the tenant that cell A serves.</summary>
    public const string CellATenantId = "alpha-root";

    /// <summary>The identifier of the management tenant of cell B.</summary>
    public const string CellBTenantId = "beta-root";

    /// <summary>The longest that a host takes to start and that a wait takes to end.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    // The directory client's HTTP client is internal to the directory client library, so its name is repeated here.
    private const string DirectoryHttpClientName = "NCode.Identity.OpenId.GlobalDirectory";

    private const string UserClientRedirectUri = "https://harness.test/callback";
    private const string Region = "region-1";
    private const string ControlPlaneKind = "primary";
    private const string KeyRingName = "directory-client";
    private const string SecretKeyRing = "harness-secret";
    private const string DPoPKeyRing = "harness-dpop";

    private PlatformHarness() { }

    /// <summary>
    /// Gets the warnings and errors that the hosts logged, each with the name of its host, in the order they were
    /// logged.
    /// </summary>
    public ConcurrentQueue<string> Logs { get; } = new();

    /// <summary>
    /// Gets the handler that every client of the platform sends its requests through.
    /// </summary>
    public PlatformRoutingHandler Router { get; } = new();

    /// <summary>
    /// Gets the control-plane cell's host.
    /// </summary>
    public HostApplicationFactory ControlPlane =>
        ControlPlaneOrNull ?? throw new InvalidOperationException("The platform is not started.");

    /// <summary>
    /// Gets cell A's host.
    /// </summary>
    public HostApplicationFactory CellA =>
        CellAOrNull ?? throw new InvalidOperationException("The platform is not started.");

    /// <summary>
    /// Gets cell B's host.
    /// </summary>
    public HostApplicationFactory CellB =>
        CellBOrNull ?? throw new InvalidOperationException("The platform is not started.");

    private HostApplicationFactory? ControlPlaneOrNull { get; set; }

    private HostApplicationFactory? CellAOrNull { get; set; }

    private HostApplicationFactory? CellBOrNull { get; set; }

    private WebApplicationFactory<DirectoryHostMarker>? DirectoryOrNull { get; set; }

    private List<ServiceProvider> ClientServices { get; } = [];

    private ECDsa? DPoPKey { get; set; }

    private IReadOnlyDictionary<string, string?>? CellSettings { get; init; }

    private string KeyDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "platform-harness-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Starts the directory, the control-plane cell and both workload cells, in the order a platform is bootstrapped:
    /// the directory answers the control-plane cell's first token request, and a workload cell starts once the
    /// control-plane cell has registered it.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The started platform, which the caller disposes.</returns>
    public static ValueTask<PlatformHarness> StartAsync(CancellationToken cancellationToken) =>
        StartAsync(cellSettings: null, cancellationToken);

    /// <summary>
    /// Starts the directory, the control-plane cell and both workload cells, with configuration that every cell reads
    /// in addition to its own.
    /// </summary>
    /// <param name="cellSettings">The configuration of every cell, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The started platform, which the caller disposes.</returns>
    public static async ValueTask<PlatformHarness> StartAsync(
        IReadOnlyDictionary<string, string?>? cellSettings,
        CancellationToken cancellationToken
    )
    {
        var harness = new PlatformHarness { CellSettings = cellSettings };
        try
        {
            await harness.InitializeAsync(cancellationToken);
            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    private async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(KeyDirectory);
        var controlKey = CreateKey(ControlPlaneName);
        var alphaKey = CreateKey(CellAName);
        var betaKey = CreateKey(CellBName);

        var directory = new PlatformDirectoryFactory(controlKey.Jwks.GetRawText(), Logs);
        DirectoryOrNull = directory;
        Router.Add(
            DirectoryName,
            "directory.platform.test",
            () => new SerializingHandler(directory.Server.CreateHandler())
        );

        var controlPlane = new PlatformCellFactory(
            AddExtraSettings(
                CreateCellSettings(
                    ControlPlaneName,
                    ControlPlaneUrl,
                    controlKey.Path,
                    isControlPlane: true
                )
            ),
            Router,
            ControlPlaneName,
            Logs
        );
        ControlPlaneOrNull = controlPlane;
        Router.Add(
            ControlPlaneName,
            "control.platform.test",
            () => controlPlane.Server.CreateHandler()
        );

        var cellA = new PlatformCellFactory(
            AddExtraSettings(
                CreateCellSettings(CellAName, CellAUrl, alphaKey.Path, isControlPlane: false)
            ),
            Router,
            CellAName,
            Logs
        );
        CellAOrNull = cellA;
        Router.Add(CellAName, "alpha.platform.test", () => cellA.Server.CreateHandler());

        var cellB = new PlatformCellFactory(
            AddExtraSettings(
                CreateCellSettings(CellBName, CellBUrl, betaKey.Path, isControlPlane: false)
            ),
            Router,
            CellBName,
            Logs
        );
        CellBOrNull = cellB;
        Router.Add(CellBName, "beta.platform.test", () => cellB.Server.CreateHandler());

        DPoPKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await StartHostAsync(directory, cancellationToken);
        await StartHostAsync(controlPlane, cancellationToken);

        var administrator = await CreateAdministratorAsync(cancellationToken);
        await RegisterCellAsync(administrator, CellAName, alphaKey.Jwks, cancellationToken);
        await RegisterCellAsync(administrator, CellBName, betaKey.Jwks, cancellationToken);

        await StartHostAsync(cellA, cancellationToken);
        await StartHostAsync(cellB, cancellationToken);
    }

    /// <summary>
    /// Gets the host of a cell by its name.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <returns>The cell's host.</returns>
    public HostApplicationFactory GetCell(string name) =>
        name switch
        {
            ControlPlaneName => ControlPlane,
            CellAName => CellA,
            CellBName => CellB,
            _ => throw new ArgumentException($"The platform has no cell '{name}'.", nameof(name)),
        };

    /// <summary>
    /// Gets the origin of a cell by its name.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <returns>The cell's origin, which is its management URL.</returns>
    public static string GetCellUrl(string name) =>
        name switch
        {
            ControlPlaneName => ControlPlaneUrl,
            CellAName => CellAUrl,
            CellBName => CellBUrl,
            _ => throw new ArgumentException($"The platform has no cell '{name}'.", nameof(name)),
        };

    /// <summary>
    /// Creates an HTTP client that sends its requests through <see cref="Router"/>, so a request to a host's origin
    /// reaches that host.
    /// </summary>
    /// <returns>The client, which the caller disposes and which does not end the router.</returns>
    public HttpClient CreateClient() => new(Router, disposeHandler: false);

    /// <summary>
    /// Gets a DPoP-bound management token from the control-plane cell for the bootstrap administrator, who holds
    /// <c>GlobalAdmin</c>.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The caller, who sends requests to any cell with its token and a proof for each.</returns>
    public ValueTask<ManagementCaller> CreateAdministratorAsync(
        CancellationToken cancellationToken
    ) =>
        CreateManagementCallerAsync(
            ControlPlaneUrl,
            BootstrapAdminApplicationFactory.ClientId,
            BootstrapAdminApplicationFactory.ClientSecret,
            cancellationToken
        );

    /// <summary>
    /// Gets a DPoP-bound management token from an issuer for a client that authenticates with a shared secret.
    /// </summary>
    /// <param name="issuer">The issuer of the tenant that the client belongs to.</param>
    /// <param name="clientId">The identifier of the client.</param>
    /// <param name="clientSecret">The client's secret.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The caller, who sends requests to any cell with its token and a proof for each.</returns>
    public async ValueTask<ManagementCaller> CreateManagementCallerAsync(
        string issuer,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken
    )
    {
        var dpopKey =
            DPoPKey ?? throw new InvalidOperationException("The platform is not started.");
        var services = CreateClientServices(dpopKey, clientSecret);
        lock (ClientServices)
            ClientServices.Add(services);

        var registration = new ClientRegistration(
            issuer,
            clientId,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic
        )
        {
            KeyRing = SecretKeyRing,
            DPoPKeyRing = DPoPKeyRing,
        };

        var result = await services
            .GetRequiredService<ITokenClient>()
            .RequestTokenAsync(
                registration,
                new ClientCredentialsTokenRequest { Scope = ManagementTokenRequest.Scope },
                cancellationToken
            );
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The token request of '{clientId}' to '{issuer}' failed: {result.Error?.Error}: {result.Error?.ErrorDescription}"
            );
        }

        var thumbprint =
            result.DPoPJwkThumbprint
            ?? throw new InvalidOperationException("The management token is not DPoP-bound.");
        return new ManagementCaller(
            CreateClient(),
            services.GetRequiredService<IDPoPProofFactory>(),
            registration,
            result.Response.AccessToken
                ?? throw new InvalidOperationException("The token response has no access token."),
            thumbprint
        );
    }

    /// <summary>
    /// Registers a workload cell with the directory through the control-plane cell's management API.
    /// </summary>
    private static async ValueTask RegisterCellAsync(
        ManagementCaller administrator,
        string name,
        JsonElement jwks,
        CancellationToken cancellationToken
    )
    {
        using var response = await administrator.SendAsync(
            HttpMethod.Put,
            $"{ControlPlaneUrl}/api/cells/{name}",
            new
            {
                region = Region,
                kind = CellKinds.Shared,
                jwks,
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Registering the cell '{name}' answered {(int)response.StatusCode}: {body}"
            );
        }
    }

    /// <summary>
    /// Waits until a cell's replica holds the control plane's management keys, and until the cell accepts a management
    /// request of the caller, which needs the replicated platform authority too.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <param name="caller">A caller whose management token the control plane issued and holds
    /// <c>GlobalAdmin</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask WaitForReplicaAsync(
        string name,
        ManagementCaller caller,
        CancellationToken cancellationToken
    )
    {
        var replica = GetCell(name).Services.GetRequiredService<IControlPlaneReplica>();
        await WaitUntilAsync(
            async () => (await replica.GetManagementKeysAsync(cancellationToken)).HasKeys,
            $"the replica of '{name}' holds the management keys",
            cancellationToken
        );

        var url = $"{GetCellUrl(name)}/api/tenants";
        await WaitUntilAsync(
            async () =>
            {
                using var response = await caller.SendAsync(
                    HttpMethod.Get,
                    url,
                    body: null,
                    cancellationToken
                );
                return response.StatusCode == HttpStatusCode.OK;
            },
            $"'{name}' accepts the management token",
            cancellationToken
        );
    }

    /// <summary>
    /// Creates an HTTP client that sends its requests to one cell through <see cref="Router"/>, with automatic
    /// redirects and cookies off, as an end-user's browser is driven.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <returns>The client, which the caller disposes and which does not end the router.</returns>
    public HttpClient CreateBrowser(string cellName) =>
        new(Router, disposeHandler: false) { BaseAddress = new Uri(GetCellUrl(cellName)) };

    /// <summary>
    /// Seeds an enabled local account in a tenant of a cell, in the tenant's <c>local</c> connection.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant that the cell serves.</param>
    /// <param name="userName">The account's username.</param>
    /// <param name="password">The account's password, which the policy does not judge.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask SeedLocalAccountAsync(
        string cellName,
        string tenantId,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        using var scope = new TenantServiceScope(GetCell(cellName).Services, tenantId);
        var serviceProvider = scope.ServiceProvider;
        var storeManagerFactory = serviceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var connection = await HostApplicationFactory.GetLocalConnectionAsync(
            storeManager,
            cancellationToken
        );
        var store = HostApplicationFactory.GetLocalAccountStore(
            serviceProvider,
            storeManager,
            connection
        );
        var account = new PersistedLocalAccount
        {
            TenantId = tenantId,
            ConnectionId = connection.ConnectionId,
            LocalAccountId = Guid.NewGuid().ToString("N"),
            UserName = userName,
            DisplayName = userName,
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };
        await store.AddAsync(account, SecureEncoding.UTF8.GetBytes(password), cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the administrator of cell B's management tenant, who holds <c>CellAdmin</c> at the cell's node, as the
    /// <c>recover-admin</c> command does.
    /// </summary>
    /// <param name="userName">The administrator's username.</param>
    /// <param name="password">The administrator's password.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask RecoverCellAdministratorAsync(
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(CellBName, cancellationToken);

        await using var scope = CellB.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IAdministratorRecoveryService>()
            .RecoverAsync(
                new RecoveryAdminOptions { UserName = userName, Password = password },
                cancellationToken
            );
        if (result.Error is { } error)
        {
            throw new InvalidOperationException(
                $"Recovering the cell administrator answered {error.StatusCode}: {error.Detail}"
            );
        }
    }

    /// <summary>
    /// Creates a platform administrator in the control-plane tenant, who holds <c>GlobalAdmin</c>, as the
    /// <c>recover-admin</c> command does.
    /// </summary>
    /// <param name="userName">The administrator's username.</param>
    /// <param name="password">The administrator's password.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask RecoverGlobalAdministratorAsync(
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(ControlPlaneName, cancellationToken);

        await using var scope = ControlPlane.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IAdministratorRecoveryService>()
            .RecoverAsync(
                new RecoveryAdminOptions { UserName = userName, Password = password },
                cancellationToken
            );
        if (result.Error is { } error)
        {
            throw new InvalidOperationException(
                $"Recovering the platform administrator answered {error.StatusCode}: {error.Detail}"
            );
        }
    }

    /// <summary>
    /// Disables a local account of a tenant of a cell directly in the cell's database.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant that the cell serves.</param>
    /// <param name="userName">The account's username.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask DisableLocalAccountAsync(
        string cellName,
        string tenantId,
        string userName,
        CancellationToken cancellationToken
    )
    {
        using var scope = new TenantServiceScope(GetCell(cellName).Services, tenantId);
        var serviceProvider = scope.ServiceProvider;
        var storeManagerFactory = serviceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var connection = await HostApplicationFactory.GetLocalConnectionAsync(
            storeManager,
            cancellationToken
        );
        var store = HostApplicationFactory.GetLocalAccountStore(
            serviceProvider,
            storeManager,
            connection
        );
        var account =
            await store.GetByUserNameOrDefaultAsync(
                connection.ConnectionId,
                userName,
                cancellationToken
            ) ?? throw new InvalidOperationException($"The account '{userName}' does not exist.");
        account.IsEnabled = false;
        await store.UpdateAsync(account, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Registers a client in a tenant of a cell through the management API, for the authorization code flow with a
    /// shared secret, so a user of the tenant can get a management token.
    /// </summary>
    /// <param name="administrator">A caller whose token the cell accepts and who may create clients in the
    /// tenant.</param>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant that the client belongs to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The client's credentials.</returns>
    public static async ValueTask<UserClient> RegisterUserClientAsync(
        ManagementCaller administrator,
        string cellName,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var baseUrl = $"{GetCellUrl(cellName)}/api/tenants/{tenantId}/clients";
        using var created = await administrator.SendAsync(
            HttpMethod.Post,
            baseUrl,
            new
            {
                isDisabled = false,
                settings = new Dictionary<string, object>
                {
                    ["token_endpoint_auth_method"] = OpenIdProtocol
                        .ClientAuthenticationMethods
                        .ClientSecretBasic,
                    ["redirect_uris"] = new[] { UserClientRedirectUri },
                },
            },
            cancellationToken
        );
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Creating the client in '{tenantId}' answered {(int)created.StatusCode}: {createdBody}"
            );
        }

        var clientId =
            JsonElement.Parse(createdBody).GetProperty("clientId").GetString()
            ?? throw new InvalidOperationException("The created client has no identifier.");

        using var secret = await administrator.SendAsync(
            HttpMethod.Post,
            $"{baseUrl}/{clientId}/secrets",
            new
            {
                secretType = "symmetric",
                keySizeBits = 256,
                expiresWhen = DateTimeOffset.UtcNow.AddYears(1),
            },
            cancellationToken
        );
        var secretBody = await secret.Content.ReadAsStringAsync(cancellationToken);
        if (secret.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Creating the secret of '{clientId}' answered {(int)secret.StatusCode}: {secretBody}"
            );
        }

        var material =
            JsonElement.Parse(secretBody).GetProperty("secretMaterial").GetString()
            ?? throw new InvalidOperationException("The created secret has no material.");
        return new UserClient(clientId, material);
    }

    /// <summary>
    /// Signs an end-user in to a cell and gets a DPoP-bound management token for the user, through the authorization
    /// code flow with PKCE as a public client that is registered in the user's tenant.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="client">The credentials of a client that <see cref="RegisterUserClientAsync"/> registered in the
    /// user's tenant.</param>
    /// <param name="userName">The user's username.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The caller, who sends requests to any cell with the user's token and a proof for each.</returns>
    public async ValueTask<ManagementCaller> CreateUserCallerAsync(
        string cellName,
        UserClient client,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        var clientId = client.ClientId;
        var redirectUri = UserClientRedirectUri;
        var dpopKey =
            DPoPKey ?? throw new InvalidOperationException("The platform is not started.");

        using var browser = CreateBrowser(cellName);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            browser,
            userName,
            password,
            cancellationToken
        );

        var codeVerifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url.EncodeToString(
            SHA256.HashData(SecureEncoding.UTF8.GetBytes(codeVerifier))
        );
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = OpenIdProtocol.ResponseTypes.Code,
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = ManagementTokenRequest.Scope,
            ["state"] = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        var query = string.Join(
            '&',
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"
            )
        );
        using var authorizeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth2/authorize?{query}"
        );
        authorizeRequest.Headers.Add(
            "Cookie",
            $"{InteractiveLogin.SessionCookieName}={sessionCookie}"
        );
        using var authorizeResponse = await browser.SendAsync(authorizeRequest, cancellationToken);
        var location = authorizeResponse.Headers.Location;
        if (
            location is null
            || !location.OriginalString.StartsWith(redirectUri, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"The authorization request was not redirected to '{redirectUri}': {(int)authorizeResponse.StatusCode} {location}"
            );
        }

        var code = System.Web.HttpUtility.ParseQueryString(location.Query)["code"];
        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException(
                $"The authorization response carries no code: {location}"
            );
        }

        var services = CreateClientServices(dpopKey, client.ClientSecret);
        lock (ClientServices)
            ClientServices.Add(services);

        var registration = new ClientRegistration(
            GetCellUrl(cellName),
            clientId,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic
        )
        {
            KeyRing = SecretKeyRing,
            DPoPKeyRing = DPoPKeyRing,
        };
        var result = await services
            .GetRequiredService<ITokenClient>()
            .RequestTokenAsync(
                registration,
                new AuthorizationCodeTokenRequest(code, redirectUri, codeVerifier),
                cancellationToken
            );
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The token request of '{userName}' to '{cellName}' failed: {result.Error?.Error}: {result.Error?.ErrorDescription}"
            );
        }

        return new ManagementCaller(
            CreateClient(),
            services.GetRequiredService<IDPoPProofFactory>(),
            registration,
            result.Response.AccessToken
                ?? throw new InvalidOperationException("The token response has no access token."),
            result.DPoPJwkThumbprint
                ?? throw new InvalidOperationException("The management token is not DPoP-bound.")
        );
    }

    /// <summary>
    /// Gets the seconds since a cell's replica of the directory's documents last completed a feed pass, as the cell's
    /// readiness check reports it.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The age of the replica, in seconds.</returns>
    public async ValueTask<double> GetReplicaAgeSecondsAsync(
        string cellName,
        CancellationToken cancellationToken
    )
    {
        var report = await GetCell(cellName)
            .Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(entry => entry.Name == "directory-replica", cancellationToken);
        var entry = report.Entries["directory-replica"];
        return Convert.ToDouble(entry.Data["replicaAgeSeconds"], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Waits until a cell holds the mirror of a team that the control-plane cell created.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask WaitForTeamAsync(
        string cellName,
        string teamId,
        CancellationToken cancellationToken
    ) =>
        await WaitUntilAsync(
            async () =>
            {
                using var scope = GetCell(cellName).Services.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                using var unscoped = serviceProvider
                    .GetRequiredService<IAmbientTenantAccessor>()
                    .BeginUnscoped("platform harness");
                var storeManagerFactory =
                    serviceProvider.GetRequiredService<IStoreManagerFactory>();
                await using var storeManager = await storeManagerFactory.CreateAsync(
                    cancellationToken
                );
                var team = await storeManager
                    .GetStore<ITeamStore>()
                    .GetOrDefaultAsync(teamId, cancellationToken);
                return team is not null;
            },
            $"'{cellName}' holds the team '{teamId}'",
            cancellationToken
        );

    /// <summary>
    /// Runs the <c>recover-admin</c> command against a cell's services with the administrator in its
    /// <c>Recovery:Admin</c> configuration.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="userName">The administrator's username.</param>
    /// <param name="password">The administrator's password.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command's exit code.</returns>
    public async ValueTask<int> RunRecoverAdminCommandAsync(
        string cellName,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        var command =
            RecoverAdminCommandLine.Parse([RecoverAdminCommandLine.CommandName])
            ?? throw new InvalidOperationException("The command line does not parse.");
        var options = new RecoveryOptions
        {
            Admin = new RecoveryAdminOptions { UserName = userName, Password = password },
        };

        await using var scope = GetCell(cellName).Services.CreateAsyncScope();
        var services = new OverridingServiceProvider(
            scope.ServiceProvider,
            typeof(IOptions<RecoveryOptions>),
            Microsoft.Extensions.Options.Options.Create(options)
        );
        return await command.RunAsync(services, cancellationToken);
    }

    /// <summary>
    /// Runs the <c>recover-sign-in</c> command against a cell's services.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command's exit code.</returns>
    public async ValueTask<int> RunRecoverSignInCommandAsync(
        string cellName,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        var command =
            RecoverSignInCommandLine.Parse([RecoverSignInCommandLine.CommandName])
            ?? throw new InvalidOperationException("The command line does not parse.");

        await using var scope = GetCell(cellName).Services.CreateAsyncScope();
        return await command.RunAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>
    /// Sets the key-custody fence of a tenant that a cell holds directly in the cell's database, as a tenant move does.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="custody">One of the <c>TenantKeyCustody</c> values.</param>
    /// <param name="otherCell">The name of the other cell of the move, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask SetKeyCustodyAsync(
        string cellName,
        string tenantId,
        string custody,
        string? otherCell,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        using var scope = new TenantServiceScope(GetCell(cellName).Services, tenantId);
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var exists = await storeManager
            .GetStore<ITenantStore>()
            .SetKeyCustodyAsync(tenantId, custody, otherCell, cancellationToken);
        if (!exists)
            throw new InvalidOperationException($"The tenant '{tenantId}' does not exist.");

        await storeManager.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the secrets of a tenant that a cell holds directly from the cell's database.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tenant's secrets.</returns>
    public async ValueTask<IReadOnlyList<PersistedSecret>> GetTenantSecretsAsync(
        string cellName,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        using var scope = new TenantServiceScope(GetCell(cellName).Services, tenantId);
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var secrets = await storeManager
            .GetStore<ITenantStore>()
            .GetSecretsOrDefaultAsync(tenantId, cancellationToken);
        return [.. secrets?.Value ?? []];
    }

    /// <summary>
    /// Changes the lifecycle of a secret of a tenant that a cell holds directly in the cell's database, without the
    /// tenant seeding or announcing the change.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="secretId">The identifier of the secret.</param>
    /// <param name="activatesWhen">The instant the secret activates.</param>
    /// <param name="retiresWhen">The instant the secret retires.</param>
    /// <param name="expiresWhen">The instant the secret expires.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask SetTenantSecretLifecycleAsync(
        string cellName,
        string tenantId,
        string secretId,
        DateTimeOffset activatesWhen,
        DateTimeOffset retiresWhen,
        DateTimeOffset expiresWhen,
        CancellationToken cancellationToken
    )
    {
        await EnsureTenantPersistedAsync(cellName, cancellationToken);

        using var scope = new TenantServiceScope(GetCell(cellName).Services, tenantId);
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITenantStore>();
        var secret =
            await store.GetSecretOrDefaultAsync(tenantId, secretId, cancellationToken)
            ?? throw new InvalidOperationException($"The secret '{secretId}' does not exist.");
        await store.UpdateSecretAsync(
            tenantId,
            secret with
            {
                ActivatesWhen = activatesWhen,
                RetiresWhen = retiresWhen,
                ExpiresWhen = expiresWhen,
            },
            cancellationToken
        );
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyDictionary<string, string?> AddExtraSettings(
        Dictionary<string, string?> settings
    )
    {
        if (CellSettings is null)
            return settings;

        foreach (var (key, value) in CellSettings)
            settings[key] = value;

        return settings;
    }

    private sealed class OverridingServiceProvider(
        IServiceProvider inner,
        Type serviceType,
        object instance
    ) : IServiceProvider
    {
        public object? GetService(Type requestedType) =>
            requestedType == serviceType ? instance : inner.GetService(requestedType);
    }

    private async ValueTask EnsureTenantPersistedAsync(
        string cellName,
        CancellationToken cancellationToken
    )
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(
            $"{GetCellUrl(cellName)}/.well-known/openid-configuration",
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Waits until a condition holds, polling it, for at most <see cref="Timeout"/>.
    /// </summary>
    /// <param name="condition">The condition, which a transient failure may fail.</param>
    /// <param name="description">What the wait is for, which the failure names.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask WaitUntilAsync(
        Func<Task<bool>> condition,
        string description,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        Exception? lastFailure = null;
        try
        {
            while (true)
            {
                try
                {
                    if (await condition())
                        return;
                }
                catch (Exception exception)
                    when (exception is HttpRequestException or InvalidOperationException)
                {
                    lastFailure = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {Timeout} waiting until {description}.{Environment.NewLine}{await DescribeAsync(cancellationToken)}{Environment.NewLine}{string.Join(Environment.NewLine, Logs.TakeLast(40))}",
                lastFailure
            );
        }
    }

    /// <summary>
    /// Describes what the control-plane cell has still to publish and what the directory holds, which a failed wait
    /// quotes.
    /// </summary>
    private async ValueTask<string> DescribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            cancellation.CancelAfter(TimeSpan.FromSeconds(10));
            using var scope = ControlPlane.Services.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IDirectoryOutbox>();
            var pending = await outbox.ListPendingAsync(100, cancellation.Token);
            var local = await outbox.ListLocalRevisionsAsync(cancellation.Token);

            var dbContextFactory = DirectoryOrNull?.Services.GetRequiredService<
                IDbContextFactory<DirectoryDbContext>
            >();
            var stored = new List<string>();
            if (dbContextFactory is not null)
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(
                    cancellation.Token
                );
                stored.AddRange(
                    (await dbContext.ReplicatedDocuments.ToListAsync(cancellation.Token)).Select(
                        document =>
                            $"{document.Kind}/{document.Key}@{document.Revision}={document.DocumentJson}"
                    )
                );
            }

            var replicas = new List<string>();
            foreach (var cellName in new[] { CellAName, CellBName })
            {
                using var cellScope = GetCell(cellName).Services.CreateScope();
                var checkpoint =
                    cellScope.ServiceProvider.GetRequiredService<IDirectoryFeedCheckpoint>();
                var replica = cellScope.ServiceProvider.GetRequiredService<IControlPlaneReplica>();
                var applied = await checkpoint.GetAppliedSequenceAsync(cancellation.Token);
                var keys = await replica.GetManagementKeysAsync(cancellation.Token);
                replicas.Add(
                    $"{cellName}: applied={applied} hasKeys={keys.HasKeys} keys={keys.Keys.Count}"
                );
            }

            return $"Replicas: [{string.Join("; ", replicas)}]; Outbox pending: [{string.Join(", ", pending.Select(document => $"{document.Kind}/{document.Key}@{document.Revision}"))}]; "
                + $"local: [{string.Join(", ", local.Select(document => $"{document.Kind}/{document.Key}@{document.Revision}"))}]; "
                + $"directory: [{string.Join(", ", stored)}]";
        }
        catch (Exception exception)
        {
            return $"Describing the platform failed: {exception}";
        }
    }

    private static async ValueTask StartHostAsync(
        WebApplicationFactory<DirectoryHostMarker> factory,
        CancellationToken cancellationToken
    ) =>
        await Task.Run(() => _ = factory.Server, cancellationToken)
            .WaitAsync(Timeout, cancellationToken);

    private static async ValueTask StartHostAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    ) =>
        await Task.Run(() => _ = factory.Server, cancellationToken)
            .WaitAsync(Timeout, cancellationToken);

    private (string Path, JsonElement Jwks) CreateKey(string cellName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var path = Path.Combine(KeyDirectory, $"{cellName}-client.pem");
        File.WriteAllBytes(path, SecureEncoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem()));

        var point = key.ExportParameters(includePrivateParameters: false).Q;
        var x = Base64Url.EncodeToString(point.X!);
        var y = Base64Url.EncodeToString(point.Y!);
        var kid = Path.GetFileNameWithoutExtension(path);
        var jwks = JsonElement.Parse(
            $$"""{"keys":[{"kty":"EC","crv":"P-256","x":"{{x}}","y":"{{y}}","kid":"{{kid}}","use":"sig","alg":"ES256"}]}"""
        );
        return (path, jwks);
    }

    private static Dictionary<string, string?> CreateCellSettings(
        string name,
        string url,
        string keyPath,
        bool isControlPlane
    )
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Cell:Name"] = name,
            ["Cell:Region"] = Region,
            ["Cell:Kind"] = isControlPlane ? ControlPlaneKind : CellKinds.Shared,
            ["Cell:ManagementUrl"] = url,
            ["Cell:IsControlPlane"] = isControlPlane ? "true" : "false",
            ["Cell:ControlPlane:Issuer"] = ControlPlaneUrl,
            ["Directory:Url"] = DirectoryUrl,
            ["Directory:ClientId"] = GlobalDirectoryProtocol.CellClientIdPrefix + name,
            ["Directory:KeyRing"] = KeyRingName,
            ["Directory:PollInterval"] = "00:00:01",
            ["Directory:PublishInterval"] = "00:00:01",
            ["Directory:RequestTimeout"] = "00:00:10",
            ["Directory:TokenRequestTimeout"] = "00:00:10",
            ["Directory:TokenFailureCacheDuration"] = "00:00:01",
            [$"KeyRings:{KeyRingName}:PemFiles:0"] = keyPath,
        };

        if (isControlPlane)
        {
            // The control-plane tenant is the one static tenant of its dedicated cell.
            settings["OpenId:TenantResolution:StrategyCode"] = OpenIdConstants
                .TenantStrategyCodes
                .StaticSingle;
            settings["OpenId:TenantResolution:StaticSingle:TenantId"] = ControlPlaneTenantId;
            settings["OpenId:TenantResolution:StaticSingle:Issuer"] = url;
            settings["BootstrapAdmin:ClientId"] = BootstrapAdminApplicationFactory.ClientId;
            settings["BootstrapAdmin:ClientSecret"] = Base64Url.EncodeToString(
                SecureEncoding.UTF8.GetBytes(BootstrapAdminApplicationFactory.ClientSecret)
            );
            settings["BootstrapAdmin:TenantId"] = ControlPlaneTenantId;
        }
        else if (string.Equals(name, CellAName, StringComparison.Ordinal))
        {
            settings["OpenId:TenantResolution:StrategyCode"] = OpenIdConstants
                .TenantStrategyCodes
                .StaticSingle;
            settings["OpenId:TenantResolution:StaticSingle:TenantId"] = CellATenantId;
            settings["OpenId:TenantResolution:StaticSingle:Issuer"] = url;
        }
        else
        {
            var host = new Uri(url).Host;
            settings["OpenId:TenantResolution:StrategyCode"] = OpenIdConstants
                .TenantStrategyCodes
                .DynamicByHost;
            settings["OpenId:TenantResolution:Root:TenantId"] = CellBTenantId;
            settings["OpenId:TenantResolution:Root:Issuer"] = url;
            settings["OpenId:TenantResolution:DynamicByHost:IssuerTemplate"] =
                $"https://{{tenantId}}.{host}";
        }

        return settings;
    }

    private ServiceProvider CreateClientServices(ECDsa dpopKey, string clientSecret)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection
            .AddSecretsLibrary()
            .AddJoseLibrary()
            .AddJwtLibrary()
            .AddOpenIdClientLibrary();
        serviceCollection
            .AddHttpClient(OpenIdClientDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Router);

        serviceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
            SecretKeyRing,
            (serviceProvider, _) =>
            {
                var keyFactory = serviceProvider.GetRequiredService<ISecretKeyFactory>();
                return serviceProvider
                    .GetRequiredService<ISecretKeyCollectionProviderFactory>()
                    .CreateStatic([
                        keyFactory.CreateSymmetric(
                            new KeyMetadata { KeyId = "harness-secret" },
                            clientSecret.AsSpan()
                        ),
                    ]);
            }
        );
        serviceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
            DPoPKeyRing,
            (serviceProvider, _) =>
            {
                var keyFactory = serviceProvider.GetRequiredService<ISecretKeyFactory>();
                return serviceProvider
                    .GetRequiredService<ISecretKeyCollectionProviderFactory>()
                    .CreateStatic([
                        keyFactory.CreateEcc(new KeyMetadata { KeyId = "harness-dpop-a" }, dpopKey),
                    ]);
            }
        );
        return serviceCollection.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var cell in new[] { CellBOrNull, CellAOrNull, ControlPlaneOrNull })
        {
            if (cell is not null)
                await cell.DisposeAsync();
        }

        if (DirectoryOrNull is { } directory)
            await directory.DisposeAsync();

        foreach (var clientServices in ClientServices)
            await clientServices.DisposeAsync();

        DPoPKey?.Dispose();
        Router.Dispose();
        if (Directory.Exists(KeyDirectory))
            Directory.Delete(KeyDirectory, recursive: true);
    }

    /// <summary>
    /// The credentials of a client that a user signs in through.
    /// </summary>
    /// <param name="ClientId">The identifier of the client.</param>
    /// <param name="ClientSecret">The client's secret.</param>
    public sealed record UserClient(string ClientId, string ClientSecret);

    /// <summary>
    /// A caller of the management API that holds a DPoP-bound access token and sends each request with its own proof.
    /// </summary>
    /// <param name="client">The client that sends the requests through the platform's router.</param>
    /// <param name="proofFactory">The factory of the proofs.</param>
    /// <param name="registration">The registration of the client that owns the token and the proof key.</param>
    /// <param name="accessToken">The access token.</param>
    /// <param name="thumbprint">The thumbprint of the proof key that the token is bound to.</param>
    public sealed class ManagementCaller(
        HttpClient client,
        IDPoPProofFactory proofFactory,
        ClientRegistration registration,
        string accessToken,
        string thumbprint
    )
    {
        /// <summary>
        /// Gets the access token.
        /// </summary>
        public string AccessToken { get; } = accessToken;

        /// <summary>
        /// Gets the thumbprint of the proof key that the token is bound to.
        /// </summary>
        public string Thumbprint { get; } = thumbprint;

        /// <summary>
        /// Sends a request with the token and a fresh proof.
        /// </summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="url">The absolute URL of the request.</param>
        /// <param name="body">The JSON body, or <see langword="null"/> for none.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The response, which the caller disposes.</returns>
        public ValueTask<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string url,
            object? body,
            CancellationToken cancellationToken
        ) => SendAsync(method, url, body, ifMatch: null, cancellationToken);

        /// <summary>
        /// Sends a request with the token, a fresh proof and an <c>If-Match</c> header.
        /// </summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="url">The absolute URL of the request.</param>
        /// <param name="body">The JSON body, or <see langword="null"/> for none.</param>
        /// <param name="ifMatch">The concurrency token for the <c>If-Match</c> header, or <see langword="null"/> for
        /// none.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The response, which the caller disposes.</returns>
        public ValueTask<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string url,
            object? body,
            string? ifMatch,
            CancellationToken cancellationToken
        ) =>
            SendContentAsync(
                method,
                url,
                body is null ? null : JsonContent.Create(body),
                ifMatch,
                cancellationToken
            );

        /// <summary>
        /// Sends a <c>PATCH</c> request with a JSON Patch document, the token and a fresh proof.
        /// </summary>
        /// <param name="url">The absolute URL of the request.</param>
        /// <param name="operations">The operations of the JSON Patch document.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The response, which the caller disposes.</returns>
        public ValueTask<HttpResponseMessage> SendJsonPatchAsync(
            string url,
            object operations,
            CancellationToken cancellationToken
        ) =>
            SendContentAsync(
                HttpMethod.Patch,
                url,
                new StringContent(
                    JsonSerializer.Serialize(operations),
                    SecureEncoding.UTF8,
                    "application/json-patch+json"
                ),
                ifMatch: null,
                cancellationToken
            );

        private async ValueTask<HttpResponseMessage> SendContentAsync(
            HttpMethod method,
            string url,
            HttpContent? content,
            string? ifMatch,
            CancellationToken cancellationToken
        )
        {
            var uri = new Uri(url, UriKind.Absolute);
            var proof = await proofFactory.CreateResourceProofAsync(
                registration,
                Thumbprint,
                method,
                uri,
                AccessToken,
                cancellationToken
            );

            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                OpenIdProtocol.TokenTypes.DPoP,
                AccessToken
            );
            request.Headers.Add(OpenIdProtocol.DPoP.HeaderName, proof);
            if (ifMatch is not null)
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            request.Content = content;

            return await client.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Sends a <c>GET</c> request with the token and a proof that names another URL than the request's, as a token
        /// and proof that were meant for one cell reach another.
        /// </summary>
        /// <param name="proofUrl">The absolute URL that the proof names as its target.</param>
        /// <param name="requestUrl">The absolute URL that the request is sent to.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The status code of the answer.</returns>
        public async ValueTask<HttpStatusCode> GetWithProofForAsync(
            string proofUrl,
            string requestUrl,
            CancellationToken cancellationToken
        )
        {
            var proof = await proofFactory.CreateResourceProofAsync(
                registration,
                Thumbprint,
                HttpMethod.Get,
                new Uri(proofUrl, UriKind.Absolute),
                AccessToken,
                cancellationToken
            );

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                OpenIdProtocol.TokenTypes.DPoP,
                AccessToken
            );
            request.Headers.Add(OpenIdProtocol.DPoP.HeaderName, proof);
            using var response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode;
        }

        /// <summary>
        /// Sends a <c>GET</c> request that presents the token under the <c>Bearer</c> scheme, without a proof.
        /// </summary>
        /// <param name="url">The absolute URL of the request.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The status code of the answer.</returns>
        public async ValueTask<HttpStatusCode> GetAsBearerAsync(
            string url,
            CancellationToken cancellationToken
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode;
        }

        /// <summary>
        /// Sends a <c>GET</c> request with the token and a proof, and reads the body of the answer.
        /// </summary>
        /// <param name="url">The absolute URL of the request.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>The status code and the body text of the answer.</returns>
        public async ValueTask<(HttpStatusCode Status, string Body)> GetAsync(
            string url,
            CancellationToken cancellationToken
        )
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.StatusCode, text);
        }
    }

    /// <summary>
    /// Hosts the directory host on a <see cref="TestServer"/> over one open in-memory Sqlite connection that holds the
    /// directory's schema, with the control-plane cell's client keys registered as the anchor's.
    /// </summary>
    private sealed class PlatformDirectoryFactory(
        string controlPlaneClientJwks,
        ConcurrentQueue<string> logs
    ) : WebApplicationFactory<DirectoryHostMarker>
    {
        private const string ConnectionString =
            "Host=localhost;Database=directory;Username=directory;Password=unused";

        private SqliteConnection Connection { get; } = new("DataSource=:memory:");

        private string KeyDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "platform-directory-" + Guid.NewGuid().ToString("N"));

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Connection.Open();
            var schemaOptions = new DbContextOptionsBuilder<DirectoryDbContext>()
                .UseSqlite(Connection)
                .Options;
            using (var schemaContext = new DirectoryDbContext(schemaOptions))
            {
                schemaContext.Database.EnsureCreated();
            }

            Directory.CreateDirectory(KeyDirectory);
            var keyPath = Path.Combine(KeyDirectory, "directory.pem");
            using (var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var pem = signingKey.ExportPkcs8PrivateKeyPem();
                File.WriteAllBytes(keyPath, SecureEncoding.UTF8.GetBytes(pem));
            }

            builder.UseSolutionRelativeContentRoot(
                "hosts/NCode.Identity.OpenId.GlobalDirectory.Host"
            );

            // The host reads each value while it registers its services, so each is a host setting.
            builder.UseSetting("LocalDevelopment:Enabled", "true");
            builder.UseSetting("ConnectionStrings:directory", ConnectionString);
            builder.UseSetting("KeyRings:directory:PemFiles:0", keyPath);
            builder.UseSetting("DirectoryServer:Issuer", DirectoryUrl);
            builder.UseSetting("DirectoryServer:KeyRing", "directory");
            builder.UseSetting("DirectoryServer:ControlPlane:Cell", ControlPlaneName);
            builder.UseSetting("DirectoryServer:ControlPlane:Region", Region);
            builder.UseSetting("DirectoryServer:ControlPlane:Kind", ControlPlaneKind);
            builder.UseSetting("DirectoryServer:ControlPlane:TenantId", ControlPlaneTenantId);
            builder.UseSetting("DirectoryServer:ControlPlane:Issuer", ControlPlaneUrl);
            builder.UseSetting("DirectoryServer:ControlPlane:ClientJwks", controlPlaneClientJwks);

            builder.ConfigureTestServices(serviceCollection =>
            {
                serviceCollection.AddSingleton<ILoggerProvider>(
                    new LogQueueProvider(DirectoryName, logs)
                );
                var registrations = serviceCollection
                    .Where(descriptor => TouchesDirectoryDbContext(descriptor.ServiceType))
                    .ToList();
                foreach (var registration in registrations)
                    serviceCollection.Remove(registration);

                serviceCollection.AddDbContextFactory<DirectoryDbContext>(options =>
                    options.UseSqlite(Connection)
                );
            });
        }

        private static bool TouchesDirectoryDbContext(Type serviceType) =>
            serviceType == typeof(DirectoryDbContext)
            || serviceType == typeof(DbContextOptions)
            || (
                serviceType.IsGenericType
                && serviceType.GetGenericArguments().Contains(typeof(DirectoryDbContext))
            );

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            Connection.Dispose();
            if (Directory.Exists(KeyDirectory))
                Directory.Delete(KeyDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Hosts one cell of the platform: the reference host with the cell's settings, whose directory client and protocol
    /// client call the other hosts through the platform's router.
    /// </summary>
    private sealed class PlatformCellFactory(
        IReadOnlyDictionary<string, string?> settings,
        PlatformRoutingHandler router,
        string name,
        ConcurrentQueue<string> logs
    ) : HostApplicationFactory
    {
        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // The host reads some values while it registers its services, which a configuration source added to the
            // host builder reaches too late, so each is also a host setting.
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            builder.ConfigureAppConfiguration(configurationBuilder =>
                configurationBuilder.AddInMemoryCollection(settings)
            );

            builder.ConfigureTestServices(serviceCollection =>
            {
                serviceCollection.AddSingleton<ILoggerProvider>(new LogQueueProvider(name, logs));
                serviceCollection
                    .AddHttpClient(DirectoryHttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => router);
                serviceCollection
                    .AddHttpClient(OpenIdClientDefaults.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => router);
            });
        }
    }

    /// <summary>
    /// Records the warnings and errors of one host in a queue that the platform's failures quote.
    /// </summary>
    private sealed class LogQueueProvider(string name, ConcurrentQueue<string> logs)
        : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new LogQueue(name, categoryName, logs);

        public void Dispose() { }
    }

    private sealed class LogQueue(string name, string category, ConcurrentQueue<string> logs)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning
            || (
                logLevel >= LogLevel.Information
                && category.StartsWith(
                    "NCode.Identity.OpenId.GlobalDirectory",
                    StringComparison.Ordinal
                )
            );

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            var text = $"[{name}] {logLevel} {category}: {formatter(state, exception)}";
            logs.Enqueue(exception is null ? text : $"{text}{Environment.NewLine}{exception}");
        }
    }

    /// <summary>
    /// Handles one request at a time and reads each response whole before it takes the next, because the directory's
    /// database is one in-memory Sqlite connection, which two requests of the platform's hosts cannot use at once.
    /// </summary>
    private sealed class SerializingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private SemaphoreSlim Gate { get; } = new(1, 1);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                return response;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
