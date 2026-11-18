// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using IdGen;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses SQLite because the EF InMemory provider does not enforce the unique index that keeps the platform to one row.
public sealed class PlatformStoreMirrorTests : IDisposable
{
    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private PlatformStore Store { get; }

    public PlatformStoreMirrorTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();
        Store = new PlatformStore(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<IIdGenerator<long>>(),
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    private static PersistedPlatformSettings CreateSettings(string settingsJson) =>
        new() { ConcurrencyToken = string.Empty, Value = JsonElement.Parse(settingsJson) };

    private async ValueTask<bool> MirrorAsync(
        string settingsJson,
        long sequence,
        CancellationToken cancellationToken = default
    )
    {
        var isApplied = await Store.MirrorSettingsAsync(
            CreateSettings(settingsJson),
            sequence,
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
        return isApplied;
    }

    #region MirrorSettingsAsync Tests

    [Fact]
    public async Task MirrorSettingsAsync_WhenNoRowExists_InsertsIt()
    {
        var isApplied = await MirrorAsync("""{"a":1}""", sequence: 3);

        Assert.True(isApplied);
        var platform = await Store.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(platform);
        Assert.Equal(OpenIdResourceIds.Platform, platform.ResourceId);
        Assert.Equal(3, platform.DirectorySequence);
        Assert.Equal("""{"a":1}""", platform.Settings.Value.GetRawText());
        Assert.False(string.IsNullOrEmpty(platform.ConcurrencyToken));
        Assert.False(string.IsNullOrEmpty(platform.Settings.ConcurrencyToken));
    }

    [Fact]
    public async Task MirrorSettingsAsync_WhenTheSequenceIsNewer_UpdatesTheSettings()
    {
        await MirrorAsync("""{"a":1}""", sequence: 3);
        var before = await Store.GetSettingsOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(before);
        DbContext.ChangeTracker.Clear();

        var isApplied = await MirrorAsync("""{"a":2}""", sequence: 4);

        Assert.True(isApplied);
        var platform = await Store.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(platform);
        Assert.Equal(4, platform.DirectorySequence);
        Assert.Equal("""{"a":2}""", platform.Settings.Value.GetRawText());
        Assert.NotEqual(before.ConcurrencyToken, platform.Settings.ConcurrencyToken);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public async Task MirrorSettingsAsync_WhenTheSequenceIsNotNewer_LeavesTheRowAlone(long sequence)
    {
        await MirrorAsync("""{"a":1}""", sequence: 3);

        var isApplied = await MirrorAsync("""{"a":2}""", sequence);

        Assert.False(isApplied);
        var platform = await Store.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(platform);
        Assert.Equal(3, platform.DirectorySequence);
        Assert.Equal("""{"a":1}""", platform.Settings.Value.GetRawText());
    }

    #endregion
}
