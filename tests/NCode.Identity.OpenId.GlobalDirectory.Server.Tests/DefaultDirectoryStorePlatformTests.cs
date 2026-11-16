// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
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

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheStoredDocumentIsNotAnEnvelope_ThrowsJsonException()
    {
        await PublishPlatformAsync(maxTenants: null);
        await ReplacePlatformDocumentJsonAsync("""{"settings":{}}""");

        await Assert.ThrowsAsync<JsonException>(async () =>
            await Platform.GetOrDefaultAsync(CancellationToken.None)
        );
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenNothingIsPublished_StoresTheDocumentWithItsSequence()
    {
        await Platform.PublishAsync(
            new PlatformDocument
            {
                Settings = JsonElement.Parse(SettingsWithLimit(7)),
                Sequence = 99,
            },
            CancellationToken.None
        );

        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(SettingsWithLimit(7), document.Settings.GetRawText());
        Assert.Equal(1, document.Sequence);
    }

    [Fact]
    public async Task PublishAsync_WhenNothingIsPublished_StoresTheDocumentAndItsChangeAsAVersionedEnvelope()
    {
        await PublishPlatformAsync(maxTenants: 7);

        var documentJson = await ReadPlatformDocumentJsonAsync();
        Assert.NotNull(documentJson);
        Assert.Equal(documentJson, await ReadChangeDocumentJsonAsync(1));
        var envelope = JsonElement.Parse(documentJson);
        Assert.Equal(1, envelope.GetProperty("$ver").GetInt32());
        var payload = envelope.GetProperty("$payload");
        Assert.Equal(0, payload.GetProperty("sequence").GetInt64());
        Assert.Equal(7, payload.GetProperty("settings").GetProperty("max_tenants").GetInt32());
    }

    [Fact]
    public async Task PublishAsync_WhenADocumentIsPublished_ReplacesItInTheSingleRow()
    {
        await PublishPlatformAsync(maxTenants: 7);
        await PublishTeamAsync("team-1");

        await PublishPlatformAsync(maxTenants: 2);

        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(SettingsWithLimit(2), document.Settings.GetRawText());
        Assert.Equal(3, document.Sequence);
        Assert.Equal(1, await CountPlatformRowsAsync());
    }

    [Fact]
    public async Task PublishAsync_WhenThePlatformWasPublishedBefore_KeepsOnlyItsLatestChange()
    {
        await PublishPlatformAsync(maxTenants: 7);
        await PublishTeamAsync("team-1");

        await PublishPlatformAsync(maxTenants: 2);

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);
        Assert.Equal([2L, 3L], changes.Select(change => change.Sequence));
        Assert.Equal("team-1", changes[0].TeamId);
        Assert.NotNull(changes[0].Document);
        Assert.Null(changes[1].TeamId);
        Assert.Equal(SettingsWithLimit(2), changes[1].Platform?.Settings.GetRawText());
    }

    #endregion
}
