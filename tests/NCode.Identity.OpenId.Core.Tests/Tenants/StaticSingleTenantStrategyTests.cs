// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Strategies;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tenants;

public sealed class StaticSingleTenantStrategyTests
{
    private static StaticSingleTenantStrategy CreateStrategy(
        IStoreManagerFactory storeManagerFactory,
        StaticSingleTenantOptions? options = null
    ) =>
        new(
            storeManagerFactory,
            Options.Create(new TenantResolutionOptions { StaticSingle = options })
        );

    [Fact]
    public void TryGetTenantId_WhenConfigured_ReturnsConfiguredIdWithoutStore()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var storeManagerFactory = mocks.Create<IStoreManagerFactory>();

        var strategy = CreateStrategy(
            storeManagerFactory.Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1" }
        );

        var result = strategy.TryGetTenantId(new DefaultHttpContext(), out var tenantId);

        Assert.True(result);
        Assert.Equal("tenant-1", tenantId);
        mocks.Verify();
    }

    [Fact]
    public void TryGetTenantId_WhenNotConfigured_ReturnsDefaultId()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var storeManagerFactory = mocks.Create<IStoreManagerFactory>();

        var strategy = CreateStrategy(storeManagerFactory.Object);

        var result = strategy.TryGetTenantId(new DefaultHttpContext(), out var tenantId);

        Assert.True(result);
        Assert.Equal(StaticSingleTenantOptions.DefaultTenantId, tenantId);
        mocks.Verify();
    }

    [Fact]
    public async Task ResolveTenantAsync_EnsuresTheConfiguredTenantExists()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var storeManagerFactory = mocks.Create<IStoreManagerFactory>();
        var provisioner = mocks.Create<ITenantProvisioner>();

        var persistedTenant = CreateTenant(isDisabled: false);

        provisioner
            .Setup(x =>
                x.EnsureCreatedAsync(
                    RootTenantOptions.DefaultTeamId,
                    "tenant-1",
                    "Tenant One",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(persistedTenant)
            .Verifiable();

        var strategy = CreateStrategy(
            storeManagerFactory.Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1", DisplayName = "Tenant One" }
        );

        var result = await strategy.ResolveTenantAsync(
            CreateHttpContext(provisioner.Object),
            CancellationToken.None
        );

        Assert.Same(persistedTenant, result);
        mocks.Verify();
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenTenantIsDisabled_ThrowsNotFound()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var provisioner = mocks.Create<ITenantProvisioner>();

        provisioner
            .Setup(x =>
                x.EnsureCreatedAsync(
                    RootTenantOptions.DefaultTeamId,
                    "tenant-1",
                    "Tenant One",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateTenant(isDisabled: true))
            .Verifiable();

        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1", DisplayName = "Tenant One" }
        );

        await Assert.ThrowsAsync<HttpResultException>(async () =>
            await strategy.ResolveTenantAsync(
                CreateHttpContext(provisioner.Object),
                CancellationToken.None
            )
        );

        mocks.Verify();
    }

    #region GetControlPlaneTenantId Tests

    [Fact]
    public void GetControlPlaneTenantId_WhenATenantIsConfigured_ReturnsTheConfiguredTenantId()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1" }
        );

        Assert.Equal("tenant-1", strategy.GetControlPlaneTenantId());
    }

    [Fact]
    public void GetControlPlaneTenantId_WhenNoTenantIsConfigured_ReturnsTheDefaultTenantId()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(mocks.Create<IStoreManagerFactory>().Object);

        Assert.Equal(StaticSingleTenantOptions.DefaultTenantId, strategy.GetControlPlaneTenantId());
    }

    #endregion

    #region GetControlPlaneTenantDisplayName Tests

    [Fact]
    public void GetControlPlaneTenantDisplayName_WhenADisplayNameIsConfigured_ReturnsIt()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1", DisplayName = "Tenant One" }
        );

        Assert.Equal("Tenant One", strategy.GetControlPlaneTenantDisplayName());
    }

    [Fact]
    public void GetControlPlaneTenantDisplayName_WhenNoneIsConfigured_ReturnsTheDefaultDisplayName()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(mocks.Create<IStoreManagerFactory>().Object);

        Assert.Equal(
            StaticSingleTenantOptions.DefaultDisplayName,
            strategy.GetControlPlaneTenantDisplayName()
        );
    }

    #endregion

    #region ResolveControlPlaneTenantAsync Tests

    [Fact]
    public async Task ResolveControlPlaneTenantAsync_Always_ReturnsTheConfiguredTenant()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var provisioner = mocks.Create<ITenantProvisioner>();

        var persistedTenant = CreateTenant(isDisabled: false);

        provisioner
            .Setup(x =>
                x.EnsureCreatedAsync(
                    RootTenantOptions.DefaultTeamId,
                    "tenant-1",
                    "Tenant One",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(persistedTenant)
            .Verifiable();

        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1", DisplayName = "Tenant One" }
        );

        // The strategy serves no other tenant, so no other tenant can issue the caller's token.
        var result = await strategy.ResolveControlPlaneTenantAsync(
            CreateHttpContext(provisioner.Object),
            CancellationToken.None
        );

        Assert.Same(persistedTenant, result);
        mocks.Verify();
    }

    #endregion

    #region IsRequiredTenant Tests

    [Theory]
    [InlineData("tenant-1", true)]
    [InlineData(RootTenantOptions.DefaultTenantId, false)]
    [InlineData("tenant-2", false)]
    public void IsRequiredTenant_RequiresTheConfiguredTenantOnly(string tenantId, bool expected)
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1" }
        );

        Assert.Equal(expected, strategy.IsRequiredTenant(tenantId));
        mocks.Verify();
    }

    #endregion

    #region GetDefaultIssuerAsync Tests

    [Fact]
    public async Task GetDefaultIssuerAsync_WhenConfiguredTenant_ReturnsTheConfiguredIssuer()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1", Issuer = "https://op.example" }
        );

        var issuer = await strategy.GetDefaultIssuerAsync("tenant-1", CancellationToken.None);

        Assert.Equal("https://op.example", issuer);
        mocks.Verify();
    }

    [Fact]
    public async Task GetDefaultIssuerAsync_WhenAnotherTenant_ReturnsNone()
    {
        var mocks = new MockRepository(MockBehavior.Strict);

        // Not even a configured Root:Issuer, which the strategy refuses at startup: it serves only its configured
        // tenant.
        var strategy = new StaticSingleTenantStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            Options.Create(
                new TenantResolutionOptions
                {
                    Root = { Issuer = "https://auth.example" },
                    StaticSingle = new StaticSingleTenantOptions
                    {
                        TenantId = "tenant-1",
                        Issuer = "https://op.example",
                    },
                }
            )
        );

        var issuer = await strategy.GetDefaultIssuerAsync(
            RootTenantOptions.DefaultTenantId,
            CancellationToken.None
        );

        Assert.Null(issuer);
        mocks.Verify();
    }

    #endregion

    #region GetDefaultMtlsEndpointAliasAuthorityAsync Tests

    [Fact]
    public async Task GetDefaultMtlsEndpointAliasAuthorityAsync_WhenConfiguredTenant_ReturnsTheConfiguredAlias()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions
            {
                TenantId = "tenant-1",
                Issuer = "https://op.example",
                MtlsEndpointAliasAuthority = "https://mtls.op.example",
            }
        );

        var alias = await strategy.GetDefaultMtlsEndpointAliasAuthorityAsync(
            "tenant-1",
            CancellationToken.None
        );

        Assert.Equal("https://mtls.op.example", alias);
        mocks.Verify();
    }

    [Fact]
    public async Task GetDefaultMtlsEndpointAliasAuthorityAsync_WhenAnotherTenant_ReturnsNone()
    {
        var mocks = new MockRepository(MockBehavior.Strict);

        // Not even a configured Root:MtlsEndpointAliasAuthority, which the strategy refuses at startup: it serves only
        // its configured tenant.
        var strategy = new StaticSingleTenantStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            Options.Create(
                new TenantResolutionOptions
                {
                    Root = { MtlsEndpointAliasAuthority = "https://mtls.example" },
                    StaticSingle = new StaticSingleTenantOptions
                    {
                        TenantId = "tenant-1",
                        Issuer = "https://op.example",
                        MtlsEndpointAliasAuthority = "https://mtls.op.example",
                    },
                }
            )
        );

        var alias = await strategy.GetDefaultMtlsEndpointAliasAuthorityAsync(
            RootTenantOptions.DefaultTenantId,
            CancellationToken.None
        );

        Assert.Null(alias);
        mocks.Verify();
    }

    #endregion

    #region ValidateTenantAsync Tests

    [Theory]
    [InlineData("https://op.example/identity", true)]
    [InlineData("https://op.example/base/identity", true)]
    [InlineData("https://op.example", false)]
    [InlineData("https://op.example/other", false)]
    public async Task ValidateTenantAsync_ChecksTheIssuerEndsWithTheTenantPath(
        string issuer,
        bool expectedValid
    )
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantPath = "/identity" }
        );

        var failures = await strategy.ValidateTenantAsync(
            "default",
            new Uri(issuer),
            null,
            mocks.Create<IStoreManager>().Object,
            CancellationToken.None
        );

        Assert.Equal(expectedValid, failures.Count == 0);
        mocks.Verify();
    }

    [Fact]
    public async Task ValidateTenantAsync_WhenConfiguredTenantHasNoIssuer_Fails()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1" }
        );

        var failures = await strategy.ValidateTenantAsync(
            "tenant-1",
            issuer: null,
            null,
            mocks.Create<IStoreManager>().Object,
            CancellationToken.None
        );

        var failure = Assert.Single(failures);
        Assert.Contains("requires an issuer", failure.Detail, StringComparison.Ordinal);
        Assert.False(failure.IsConflict);
        mocks.Verify();
    }

    [Fact]
    public async Task ValidateTenantAsync_WhenUnservedTenantHasNoIssuer_Succeeds()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var strategy = CreateStrategy(
            mocks.Create<IStoreManagerFactory>().Object,
            new StaticSingleTenantOptions { TenantId = "tenant-1" }
        );

        // A tenant the strategy does not serve needs no address.
        var failures = await strategy.ValidateTenantAsync(
            "tenant-2",
            issuer: null,
            null,
            mocks.Create<IStoreManager>().Object,
            CancellationToken.None
        );

        Assert.Empty(failures);
        mocks.Verify();
    }

    #endregion

    private static DefaultHttpContext CreateHttpContext(ITenantProvisioner provisioner) =>
        new()
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(provisioner)
                .BuildServiceProvider(),
        };

    private static PersistedTenant CreateTenant(bool isDisabled) =>
        new PersistedTenantBuilder
        {
            Issuer = "https://op.example",
            IsDisabled = isDisabled,
            DisplayName = "Tenant One",
        }.Build();
}
