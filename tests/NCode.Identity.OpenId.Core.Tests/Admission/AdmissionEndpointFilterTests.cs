// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.Authentication.Admission;
using NCode.Identity.OpenId.Authentication.Endpoints;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Admission;

public sealed class AdmissionEndpointFilterTests : BaseTests
{
    private Mock<IRequestAdmissionGate> MockGate { get; }

    private Mock<OpenIdContext> MockContext { get; }

    private Mock<IReadOnlySettingCollection> MockTenantSettings { get; }

    private DefaultHttpContext HttpContext { get; } = new();

    private ServiceProvider MetricsProvider { get; }

    private MetricCollector<long> Collector { get; }

    private AdmissionEndpointFilter Filter { get; }

    public AdmissionEndpointFilterTests()
    {
        // The meter factory scopes the meter to this test, so the collector sees only this filter's measurements.
        MetricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = MetricsProvider.GetRequiredService<IMeterFactory>();
        Collector = new MetricCollector<long>(
            meterFactory,
            OpenIdAuthenticationMetrics.MeterName,
            AuthenticationMetrics.AdmissionShedName
        );

        MockGate = CreateStrictMock<IRequestAdmissionGate>();
        MockContext = CreateStrictMock<OpenIdContext>();
        MockTenantSettings = CreateStrictMock<IReadOnlySettingCollection>();
        Filter = new AdmissionEndpointFilter(
            MockGate.Object,
            Options.Create(new AdmissionOptions { RetryAfter = TimeSpan.FromSeconds(2) }),
            new AuthenticationMetrics(meterFactory)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Collector.Dispose();
            MetricsProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Scaffolding

    private void SetupFeature()
    {
        var mockFeature = CreateStrictMock<IOpenIdContextFeature>();
        mockFeature.Setup(x => x.OpenIdContext).Returns(MockContext.Object).Verifiable();
        HttpContext.Features.Set(mockFeature.Object);
    }

    private void SetupTenant()
    {
        var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
        mockProvider.Setup(x => x.Collection).Returns(MockTenantSettings.Object).Verifiable();
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        MockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
    }

    private void SetupCapacityPercent(int? capacityPercent)
    {
        var value = capacityPercent.GetValueOrDefault();
        MockTenantSettings
            .Setup(x => x.TryGetValue(OpenIdSettingKeys.AdmissionCapacityPercent, out value))
            .Returns(capacityPercent.HasValue)
            .Verifiable();
    }

    private void SetupEnvironmentType() =>
        MockTenantSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EnvironmentType))
            .Returns(OpenIdConstants.TenantEnvironmentTypes.Production)
            .Verifiable();

    private ValueTask<object?> InvokeAsync(
        EndpointFilterDelegate next,
        CancellationToken cancellationToken
    )
    {
        HttpContext.RequestAborted = cancellationToken;
        return Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(HttpContext), next);
    }

    #endregion

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WithoutAnOpenIdContext_RunsTheEndpointWithoutTheGate()
    {
        var expected = new object();
        var endpointRan = false;

        var result = await InvokeAsync(
            _ =>
            {
                endpointRan = true;
                return ValueTask.FromResult<object?>(expected);
            },
            CancellationToken.None
        );

        Assert.True(endpointRan);
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheEndpointIsNotMetered_RunsTheEndpointWithoutTheGate()
    {
        // The filter never reads the context of an unmetered request.
        HttpContext.Features.Set(CreateStrictMock<IOpenIdContextFeature>().Object);
        var mockMetered = CreateStrictMock<IOpenIdEndpointMeteredMetadata>();
        mockMetered.Setup(x => x.IsMetered).Returns(false).Verifiable();
        var metadata = new EndpointMetadataCollection(mockMetered.Object);
        HttpContext.SetEndpoint(new Endpoint(null, metadata, "test"));
        var expected = new object();
        var endpointRan = false;

        var result = await InvokeAsync(
            _ =>
            {
                endpointRan = true;
                return ValueTask.FromResult<object?>(expected);
            },
            CancellationToken.None
        );

        Assert.True(endpointRan);
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTenantSetsNoShare_EntersWithTheWholeCapacity()
    {
        SetupFeature();
        SetupTenant();
        SetupCapacityPercent(null);
        MockGate.Setup(x => x.TryEnter(100)).Returns(true).Verifiable();
        MockGate.Setup(x => x.Exit()).Verifiable();
        var expected = new object();

        var result = await InvokeAsync(
            _ => ValueTask.FromResult<object?>(expected),
            CancellationToken.None
        );

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenAdmitted_RunsTheEndpointAndExits()
    {
        SetupFeature();
        SetupTenant();
        SetupCapacityPercent(60);
        MockGate.Setup(x => x.TryEnter(60)).Returns(true).Verifiable();
        MockGate.Setup(x => x.Exit()).Verifiable();
        var expected = new object();
        var endpointRan = false;

        var result = await InvokeAsync(
            _ =>
            {
                // The request holds its place while the endpoint runs.
                MockGate.Verify(x => x.Exit(), Times.Never);
                endpointRan = true;
                return ValueTask.FromResult<object?>(expected);
            },
            CancellationToken.None
        );

        Assert.True(endpointRan);
        Assert.Same(expected, result);
        MockGate.Verify(x => x.Exit(), Times.Once);
        Assert.Empty(Collector.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheEndpointThrows_StillExits()
    {
        SetupFeature();
        SetupTenant();
        SetupCapacityPercent(60);
        MockGate.Setup(x => x.TryEnter(60)).Returns(true).Verifiable();
        MockGate.Setup(x => x.Exit()).Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeAsync(_ => throw new InvalidOperationException(), CancellationToken.None)
        );

        MockGate.Verify(x => x.Exit(), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenShed_AnswersServiceUnavailableWithoutRunningTheEndpoint()
    {
        SetupFeature();
        SetupTenant();
        SetupCapacityPercent(60);
        SetupEnvironmentType();
        MockGate.Setup(x => x.TryEnter(60)).Returns(false).Verifiable();
        var mockError = CreateLooseMock<IOpenIdError>();
        mockError.SetupAllProperties();
        var mockErrorFactory = CreateLooseMock<IOpenIdErrorFactory>();
        mockErrorFactory
            .Setup(x => x.Create(OpenIdProtocol.ErrorCodes.TemporarilyUnavailable))
            .Returns(mockError.Object)
            .Verifiable();
        MockContext.Setup(x => x.ErrorFactory).Returns(mockErrorFactory.Object).Verifiable();
        var endpointRan = false;

        var result = await InvokeAsync(
            _ =>
            {
                endpointRan = true;
                return ValueTask.FromResult<object?>(null);
            },
            CancellationToken.None
        );

        Assert.False(endpointRan);
        Assert.IsAssignableFrom<IResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, mockError.Object.StatusCode);
        MockGate.Verify(x => x.Exit(), Times.Never);
        var measurement = Assert.Single(Collector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            OpenIdConstants.TenantEnvironmentTypes.Production,
            measurement.Tags.GetValueOrDefault(AuthenticationMetrics.TenantEnvironmentTypeAttribute)
        );
    }

    #endregion
}
