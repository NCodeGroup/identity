// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class RuntimeRolePostureTests
{
    #region Evaluate Tests

    [Fact]
    public void Evaluate_NoViolation_ReturnsEmpty()
    {
        var result = RuntimeRolePosture.Evaluate(false, false, false);

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_Superuser_ReturnsSuperuserViolation()
    {
        var result = RuntimeRolePosture.Evaluate(true, false, false);

        Assert.Equal([RuntimeRolePosture.SuperuserViolation], result);
    }

    [Fact]
    public void Evaluate_BypassesRls_ReturnsBypassViolation()
    {
        var result = RuntimeRolePosture.Evaluate(false, true, false);

        Assert.Equal([RuntimeRolePosture.BypassesRlsViolation], result);
    }

    [Fact]
    public void Evaluate_OwnsTenantTables_ReturnsOwnershipViolation()
    {
        var result = RuntimeRolePosture.Evaluate(false, false, true);

        Assert.Equal([RuntimeRolePosture.OwnsTablesViolation], result);
    }

    [Fact]
    public void Evaluate_AllViolations_ReturnsEachInOrder()
    {
        var result = RuntimeRolePosture.Evaluate(true, true, true);

        Assert.Equal(
            [
                RuntimeRolePosture.SuperuserViolation,
                RuntimeRolePosture.BypassesRlsViolation,
                RuntimeRolePosture.OwnsTablesViolation,
            ],
            result
        );
    }

    #endregion
}
