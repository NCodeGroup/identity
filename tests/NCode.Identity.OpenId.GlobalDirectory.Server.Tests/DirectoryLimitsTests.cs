// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.OpenId.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryLimitsTests
{
    #region MaxTeamPathLength Tests

    [Fact]
    public void MaxTeamPathLength_Always_EqualsTheLongestPathACellFormats()
    {
        var teamIds = Enumerable.Repeat(
            new string('t', DirectoryLimits.MaxIdentifierLength),
            TeamPaths.MaxDepth
        );

        var longestPath = TeamPaths.Format(teamIds);

        Assert.Equal(DirectoryLimits.MaxTeamPathLength, longestPath.Length);
    }

    #endregion

    #region MaxCellKindLength Tests

    [Fact]
    public void MaxCellKindLength_Always_IsThirtyTwo()
    {
        Assert.Equal(32, DirectoryLimits.MaxCellKindLength);
    }

    #endregion

    #region MaxManagementUrlLength Tests

    [Fact]
    public void MaxManagementUrlLength_Always_IsTwoThousand()
    {
        Assert.Equal(2000, DirectoryLimits.MaxManagementUrlLength);
    }

    #endregion

    #region DirectoryDbContext Tests

    [Fact]
    public void DirectoryDbContext_Always_SizesTheTeamPathColumnWithTheLimit()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var dbContext = new DirectoryDbContext(options);

        var teamType = dbContext.Model.FindEntityType(typeof(TeamRecord));
        Assert.NotNull(teamType);
        var pathProperty = teamType.FindProperty(nameof(TeamRecord.NormalizedPath));
        Assert.NotNull(pathProperty);

        Assert.Equal(DirectoryLimits.MaxTeamPathLength, pathProperty.GetMaxLength());
    }

    #endregion
}
