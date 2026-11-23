// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Endpoints.Secrets;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Endpoints.Clients;

public sealed class MachineClientEndpointProviderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    #region Helpers

    // Mapping reads no member of the handler, so the strict mocks it is built over are never called.
    private MachineClientEndpointProvider CreateProvider(bool isControlPlane) =>
        new(
            new ClientApiEndpointHandler(
                MockRepository.Create<IStoreManagerFactory>().Object,
                MockRepository.Create<IAuthorizationService>().Object,
                MockRepository.Create<IClientValidator>().Object,
                MockRepository.Create<ISecretValidator>().Object,
                MockRepository.Create<ISecretGenerator>().Object,
                MockRepository.Create<IClientKeyImporter>().Object,
                MockRepository.Create<ISecretPublicKeyConverter>().Object,
                MockRepository.Create<ISecretProtectorProvider>().Object,
                TimeProvider.System,
                MockRepository.Create<ICryptoService>().Object,
                MockRepository.Create<ISettingSerializer>().Object,
                MockRepository.Create<ISettingScopeCollectionProvider>().Object,
                MockRepository.Create<IClientSettingsEvaluator>().Object,
                MockRepository.Create<IClientCreator>().Object,
                MockRepository.Create<IClientCoherenceReporter>().Object,
                MockRepository.Create<IClientSecretIssuer>().Object,
                MockRepository.Create<IResourceOwnershipService>().Object,
                CreatePrincipalDirectory(),
                MockRepository.Create<IManagementAuditRecorder>().Object,
                new DefaultHostManagedClientChecker([]),
                MockRepository.Create<IClientKeySourceChecker>().Object,
                MockRepository.Create<IClientJwksUriChecker>().Object,
                NullLogger<ClientApiEndpointHandler>.Instance,
                MockRepository.Create<IAmbientTenantAccessor>().Object
            ),
            Options.Create(new CellOptions { IsControlPlane = isControlPlane })
        );

    private static string[] MapRoutes(MachineClientEndpointProvider provider)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        provider.Map(app);

        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];
    }

    #endregion

    #region Map Tests

    [Fact]
    public void Map_OnTheControlPlane_MapsThePlatformAndTeamMachineClientRoutes()
    {
        var routes = MapRoutes(CreateProvider(isControlPlane: true));

        Assert.Equal(
            [
                "/platform/clients",
                "/platform/clients",
                "/teams/{teamId}/clients",
                "/teams/{teamId}/clients",
            ],
            routes
        );
    }

    [Fact]
    public void Map_OnAnotherCell_MapsNoRoute()
    {
        var routes = MapRoutes(CreateProvider(isControlPlane: false));

        Assert.Empty(routes);
    }

    #endregion

    private static IControlPlanePrincipalDirectory CreatePrincipalDirectory()
    {
        var mock = new Mock<IControlPlanePrincipalDirectory>();
        mock.Setup(x =>
                x.FindAsync(It.IsAny<ControlPlanePrincipalQuery>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);
        return mock.Object;
    }
}
