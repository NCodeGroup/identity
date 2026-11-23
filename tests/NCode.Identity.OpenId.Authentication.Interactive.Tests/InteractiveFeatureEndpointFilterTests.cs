// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class InteractiveFeatureEndpointFilterTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    #region Scaffolding

    private DefaultHttpContext CreateHttpContext(bool featureEnabled)
    {
        var mockSettings = MockRepository.Create<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
            .Returns(featureEnabled)
            .Verifiable();
        var mockProvider = MockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);
        return httpContext;
    }

    #endregion

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenTheSettingIsOff_AnswersNotFoundWithoutRunningTheEndpoint()
    {
        var httpContext = CreateHttpContext(featureEnabled: false);
        var filter = new InteractiveFeatureEndpointFilter(OpenIdSettingKeys.PasswordResetEnabled);
        var endpointRan = false;

        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ =>
            {
                endpointRan = true;
                return ValueTask.FromResult<object?>(new object());
            }
        );

        Assert.IsType<NotFound>(result);
        Assert.False(endpointRan);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheSettingIsOn_RunsTheEndpoint()
    {
        var httpContext = CreateHttpContext(featureEnabled: true);
        var filter = new InteractiveFeatureEndpointFilter(OpenIdSettingKeys.PasswordResetEnabled);
        var expected = new object();

        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ => ValueTask.FromResult<object?>(expected)
        );

        Assert.Same(expected, result);
    }

    #endregion
}
