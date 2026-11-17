// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests;

public class MigrationHistoryRulesTests
{
    private const string First = "20261001000000_InitialCreate";
    private const string Second = "20261105033727_InitialCreate";
    private const string Third = "20261110120000_AddIndex";

    #region FindUnknownMigrations Tests

    [Fact]
    public void FindUnknownMigrations_WhenEveryAppliedMigrationIsKnown_ReturnsEmpty()
    {
        var result = MigrationHistoryRules.FindUnknownMigrations(
            [First, Second],
            [Second, First, Third]
        );

        Assert.Empty(result);
    }

    [Fact]
    public void FindUnknownMigrations_WhenTheHistoryHoldsUnknownMigrations_ReturnsThemInHistoryOrder()
    {
        var result = MigrationHistoryRules.FindUnknownMigrations([Third, Second, First], [Second]);

        Assert.Equal([Third, First], result);
    }

    [Fact]
    public void FindUnknownMigrations_WhenAnIdentifierDiffersOnlyInCase_ReturnsIt()
    {
        var result = MigrationHistoryRules.FindUnknownMigrations(
            [First.ToUpperInvariant()],
            [First]
        );

        Assert.Equal([First.ToUpperInvariant()], result);
    }

    [Fact]
    public void FindUnknownMigrations_WhenTheHistoryIsEmpty_ReturnsEmpty()
    {
        var result = MigrationHistoryRules.FindUnknownMigrations([], [First]);

        Assert.Empty(result);
    }

    [Fact]
    public void FindUnknownMigrations_WhenTheAppliedMigrationsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            MigrationHistoryRules.FindUnknownMigrations(null!, [First])
        );

        Assert.Equal("appliedMigrations", exception.ParamName);
    }

    [Fact]
    public void FindUnknownMigrations_WhenTheMigrationsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            MigrationHistoryRules.FindUnknownMigrations([First], null!)
        );

        Assert.Equal("migrations", exception.ParamName);
    }

    #endregion
}
