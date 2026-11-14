// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Server.DevelopmentEnvironment;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class BootstrapAdminOptionsValidatorTests : IDisposable
{
    private const string ControlPlaneTenantId = "default";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    // The control-plane tenant is read only when the bootstrap administrator is configured with a tenant.
    private BootstrapAdminOptionsValidator CreateValidator(bool readsControlPlaneTenant)
    {
        var tenantResolver = MockRepository.Create<ITenantResolver>();
        if (readsControlPlaneTenant)
        {
            tenantResolver
                .Setup(x => x.GetControlPlaneTenantId())
                .Returns(ControlPlaneTenantId)
                .Verifiable();
        }

        return new BootstrapAdminOptionsValidator(tenantResolver.Object);
    }

    #region Validate Tests

    [Fact]
    public void Validate_WhenNotConfigured_Succeeds()
    {
        var validator = CreateValidator(readsControlPlaneTenant: false);

        var result = validator.Validate(name: null, new BootstrapAdminOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenConfiguredWithTheControlPlaneTenant_Succeeds()
    {
        var validator = CreateValidator(readsControlPlaneTenant: true);

        var result = validator.Validate(
            name: null,
            new BootstrapAdminOptions
            {
                ClientId = "bootstrap-admin",
                ClientSecret = "c2VjcmV0",
                TenantId = ControlPlaneTenantId,
            }
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenConfiguredWithoutATenant_Fails()
    {
        // Without a named tenant the client would be seeded into every tenant, each created at runtime included.
        var validator = CreateValidator(readsControlPlaneTenant: false);

        var result = validator.Validate(
            name: null,
            new BootstrapAdminOptions { ClientId = "bootstrap-admin", ClientSecret = "c2VjcmV0" }
        );

        Assert.True(result.Failed);
        Assert.Contains(
            "BootstrapAdmin:TenantId is required",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenConfiguredWithATenantOtherThanTheControlPlaneTenant_Fails()
    {
        // The platform role is read in the control-plane tenant, so a client seeded elsewhere would hold no authority.
        var validator = CreateValidator(readsControlPlaneTenant: true);

        var result = validator.Validate(
            name: null,
            new BootstrapAdminOptions
            {
                ClientId = "bootstrap-admin",
                ClientSecret = "c2VjcmV0",
                TenantId = "workload",
            }
        );

        Assert.True(result.Failed);
        Assert.Contains(
            $"must name the control-plane tenant '{ControlPlaneTenantId}'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    #endregion
}
