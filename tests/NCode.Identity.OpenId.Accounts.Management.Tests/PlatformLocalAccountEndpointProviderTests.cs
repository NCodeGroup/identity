// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class PlatformLocalAccountEndpointProviderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    #region Helpers

    // Mapping reads no member of the handler, so the strict mocks it is built over are never called.
    private PlatformLocalAccountEndpointProvider CreateProvider(bool isControlPlane)
    {
        var validator = MockRepository.Create<ILocalAccountValidator>().Object;
        var cryptoService = MockRepository.Create<ICryptoService>().Object;
        var effectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>().Object;
        var passwordPolicy = MockRepository.Create<IPasswordPolicy>().Object;
        var localAccountPasswords = new LocalAccountPasswords(
            effectiveSettingsResolver,
            passwordPolicy
        );
        var creator = new DefaultLocalAccountCreator(
            validator,
            cryptoService,
            localAccountPasswords
        );

        var storeManagerFactory = MockRepository.Create<IStoreManagerFactory>().Object;
        var authorizationService = MockRepository.Create<IAuthorizationService>().Object;
        var auditRecorder = MockRepository.Create<IManagementAuditRecorder>().Object;
        var sessionService = MockRepository.Create<ISessionService>().Object;
        var openIdContextFactory = MockRepository.Create<IOpenIdContextFactory>().Object;
        var postCommitSecurityEffects = MockRepository.Create<IPostCommitSecurityEffects>().Object;
        var ambientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>().Object;
        var handler = new LocalAccountApiEndpointHandler(
            storeManagerFactory,
            authorizationService,
            cryptoService,
            validator,
            creator,
            auditRecorder,
            sessionService,
            openIdContextFactory,
            localAccountPasswords,
            postCommitSecurityEffects,
            ambientTenantAccessor
        );

        var cellOptions = new CellOptions { IsControlPlane = isControlPlane };
        var options = Options.Create(cellOptions);
        return new PlatformLocalAccountEndpointProvider(handler, options);
    }

    private static string[] MapRoutes(IEndpointProvider provider)
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
    public void Map_OnTheControlPlane_MapsThePlatformRoutes()
    {
        var provider = CreateProvider(isControlPlane: true);

        var routes = MapRoutes(provider);

        Assert.Equal(["/platform/local-accounts", "/platform/local-accounts"], routes);
    }

    [Fact]
    public void Map_OnAnotherCell_MapsNoRoute()
    {
        var provider = CreateProvider(isControlPlane: false);

        var routes = MapRoutes(provider);

        Assert.Empty(routes);
    }

    [Theory]
    [InlineData("POST", "/platform/local-accounts", new[] { 400, 401, 403, 404, 409, 412, 415 })]
    [InlineData("GET", "/platform/local-accounts", new[] { 400, 401, 403 })]
    public async Task Map_OnTheControlPlane_DeclaresAndDescribesEveryProblemARouteAnswers(
        string httpMethod,
        string routePattern,
        int[] statusCodes
    )
    {
        var provider = CreateProvider(isControlPlane: true);

        var declared = await EndpointProblemMetadata.ReadStatusCodesAsync(
            provider,
            httpMethod,
            routePattern,
            CancellationToken.None
        );
        var undescribed = await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
            provider,
            httpMethod,
            routePattern,
            CancellationToken.None
        );

        Assert.Equal(statusCodes, declared);
        Assert.Empty(undescribed);
    }

    #endregion
}
