// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStorePlatformTests : DefaultDirectoryStoreTestBase
{
    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenNothingIsPublished_ReturnsNull()
    {
        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);

        Assert.Null(document);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenNothingIsPublished_StoresTheDocumentWithItsSequence()
    {
        await Platform.PublishAsync(
            new PlatformDocument { SettingsJson = SettingsWithLimit(7), Sequence = 99 },
            CancellationToken.None
        );

        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(SettingsWithLimit(7), document.SettingsJson);
        Assert.Equal(1, document.Sequence);
    }

    [Fact]
    public async Task PublishAsync_WhenADocumentIsPublished_ReplacesItInTheSingleRow()
    {
        await PublishPlatformAsync(maxTenants: 7);
        await PublishTeamAsync("team-1");

        await PublishPlatformAsync(maxTenants: 2);

        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(SettingsWithLimit(2), document.SettingsJson);
        Assert.Equal(3, document.Sequence);
        Assert.Equal(1, await CountPlatformRowsAsync());
    }

    #endregion
}
