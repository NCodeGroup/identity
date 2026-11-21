// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using NCode.Disposables;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.Platform;
using NCode.Identity.OpenId.Tenants;
using NCode.Mediator;
using NCode.PropertyBag;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Contexts;

public class DefaultOpenIdContextFactoryTests : BaseTests
{
    #region CreateForTenantAsync Tests

    [Fact]
    public async Task CreateForTenantAsync_WhenCreated_GivesTheContextAChildOfTheTenantsBag()
    {
        const string tenantId = "tenant-1";
        var cancellationToken = CancellationToken.None;
        var httpContext = new DefaultHttpContext();
        var mockMediator = CreateStrictMock<IMediator>();
        var platformBag = new ConcurrentPropertyBagDictionary();
        var tenantBag = new ConcurrentPropertyBagDictionary();
        IPropertyBag? contextBag = null;

        var mockEnvironment = CreateStrictMock<OpenIdEnvironment>();
        var mockPlatform = CreateStrictMock<OpenIdPlatform>();
        mockPlatform.Setup(x => x.PropertyBag).Returns(platformBag).Verifiable();
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.PropertyBag).Returns(tenantBag).Verifiable();
        mockTenant
            .Protected()
            .Setup<ValueTask>("DisposeAsyncCore")
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var mockContext = CreateStrictMock<OpenIdContext>();

        var mockEnvironmentProvider = CreateStrictMock<IOpenIdEnvironmentProvider>();
        mockEnvironmentProvider.Setup(x => x.Get()).Returns(mockEnvironment.Object).Verifiable();
        var mockPlatformProvider = CreateStrictMock<IOpenIdPlatformProvider>();
        mockPlatformProvider
            .Setup(x => x.GetAsync(mockEnvironment.Object, cancellationToken))
            .ReturnsAsync(mockPlatform.Object)
            .Verifiable();
        var mockTenantFactory = CreateStrictMock<IOpenIdTenantFactory>();
        mockTenantFactory
            .Setup(x =>
                x.CreateTenantByIdAsync(
                    httpContext,
                    mockEnvironment.Object,
                    mockPlatform.Object,
                    tenantId,
                    platformBag,
                    cancellationToken
                )
            )
            .ReturnsAsync(mockTenant.Object.AsSharedReference())
            .Verifiable();

        var factory = CreatePartialMock<DefaultOpenIdContextFactory>(
            mockEnvironmentProvider.Object,
            mockPlatformProvider.Object,
            mockTenantFactory.Object
        );
        factory
            .Setup(x =>
                x.CreateContext(
                    httpContext,
                    mockMediator.Object,
                    mockEnvironment.Object,
                    mockPlatform.Object,
                    It.IsAny<AsyncSharedReferenceLease<OpenIdTenant>>(),
                    It.IsAny<IPropertyBag>()
                )
            )
            .Callback<
                HttpContext,
                IMediator,
                OpenIdEnvironment,
                OpenIdPlatform,
                AsyncSharedReferenceLease<OpenIdTenant>,
                IPropertyBag
            >((_, _, _, _, _, bag) => contextBag = bag)
            .Returns(mockContext.Object)
            .Verifiable();

        var result = await factory.Object.CreateForTenantAsync(
            httpContext,
            tenantId,
            mockMediator.Object,
            cancellationToken
        );

        Assert.Same(mockContext.Object, result);
        Assert.Same(tenantBag, Assert.IsType<LayeredPropertyBag>(contextBag).Parent);
    }

    #endregion

    #region CreateForBackgroundAsync Tests

    [Fact]
    public async Task CreateForBackgroundAsync_CreatesTheTenantsContextFromTheServicesWithoutARequest()
    {
        const string tenantId = "tenant-1";
        var cancellationToken = CancellationToken.None;
        var mockMediator = CreateStrictMock<IMediator>();
        var mockContext = CreateStrictMock<OpenIdContext>();
        await using var serviceProvider = new ServiceCollection()
            .AddSingleton(mockMediator.Object)
            .BuildServiceProvider();
        var factory = CreatePartialMock<DefaultOpenIdContextFactory>(
            CreateStrictMock<IOpenIdEnvironmentProvider>().Object,
            CreateStrictMock<IOpenIdPlatformProvider>().Object,
            CreateStrictMock<IOpenIdTenantFactory>().Object
        );
        factory
            .Setup(x =>
                x.CreateForTenantAsync(
                    It.Is<HttpContext>(h =>
                        h.RequestServices == serviceProvider && h.GetEndpoint() == null
                    ),
                    tenantId,
                    mockMediator.Object,
                    cancellationToken
                )
            )
            .ReturnsAsync(mockContext.Object)
            .Verifiable();

        var result = await factory.Object.CreateForBackgroundAsync(
            serviceProvider,
            tenantId,
            cancellationToken
        );

        Assert.Same(mockContext.Object, result);
    }

    #endregion
}
