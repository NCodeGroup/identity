// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryLimitsTests
{
    #region MaxIdentifierLength Tests

    [Fact]
    public void MaxIdentifierLength_Always_EqualsTheCellsResourceIdLength()
    {
        Assert.Equal(MaxLengths.ResourceId, DirectoryLimits.MaxIdentifierLength);
    }

    #endregion

    #region MaxIssuerLength Tests

    [Fact]
    public void MaxIssuerLength_Always_EqualsTheCellsIssuerLength()
    {
        Assert.Equal(OpenIdMaxLengths.Issuer, DirectoryLimits.MaxIssuerLength);
    }

    #endregion

    #region MaxTeamDepth Tests

    [Fact]
    public void MaxTeamDepth_Always_EqualsTheCellsTeamDepth()
    {
        Assert.Equal(TeamPaths.MaxDepth, DirectoryLimits.MaxTeamDepth);
    }

    #endregion

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
