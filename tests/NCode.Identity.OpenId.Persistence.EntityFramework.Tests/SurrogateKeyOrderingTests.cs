// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

// Keyset paging compares and orders a UUIDv7 key as a Guid; the providers must agree with .NET (ADR-1517).
public class SurrogateKeyOrderingTests
{
    private const int KeyCount = 60;
    private const int PivotIndex = 19;

    private sealed class KeyRow
    {
        public Guid Id { get; set; }
    }

    private sealed class KeyDbContext(DbContextOptions<KeyDbContext> options) : DbContext(options)
    {
        public DbSet<KeyRow> Rows => Set<KeyRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<KeyRow>(builder =>
            {
                builder.HasKey(row => row.Id);
                builder.Property(row => row.Id).ValueGeneratedNever();
            });
        }
    }

    private static List<Guid> CreateShuffledKeys()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var keys = Enumerable
            .Range(0, KeyCount)
            .Select(i => Guid.CreateVersion7(start.AddMilliseconds(i / 3)))
            .ToList();

        var random = new Random(843);
        return keys.OrderBy(_ => random.Next()).ToList();
    }

    private static List<Guid> CreateExpectedOrder(IEnumerable<Guid> keys)
    {
        return keys.OrderBy(
                key => Convert.ToHexString(key.ToByteArray(bigEndian: true)),
                StringComparer.Ordinal
            )
            .ToList();
    }

    private static void Insert(KeyDbContext context, IEnumerable<Guid> keys)
    {
        context.Rows.AddRange(keys.Select(key => new KeyRow { Id = key }));
        context.SaveChanges();
    }

    #region Order Tests

    [Fact]
    public void CompareTo_Version7Keys_OrdersByBigEndianBytes()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);

        var actual = keys.Order().ToList();

        Assert.Equal(expected, actual);
    }

    #endregion

    #region OrderBy Tests

    [Fact]
    public void OrderBy_InMemory_OrdersByBigEndianBytes()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);

        var options = new DbContextOptionsBuilder<KeyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new KeyDbContext(options);
        Insert(context, keys);

        var actual = context.Rows.OrderBy(row => row.Id).Select(row => row.Id).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OrderBy_Sqlite_OrdersByBigEndianBytes()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<KeyDbContext>().UseSqlite(connection).Options;
        using var context = new KeyDbContext(options);
        context.Database.EnsureCreated();
        Insert(context, keys);

        var actual = context.Rows.OrderBy(row => row.Id).Select(row => row.Id).ToList();

        Assert.Equal(expected, actual);
    }

    #endregion

    #region Where Tests

    [Fact]
    public void Where_GreaterThanOnSqlite_ReturnsTheKeysAfterThePivot()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);
        var pivot = expected[PivotIndex];

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<KeyDbContext>().UseSqlite(connection).Options;
        using var context = new KeyDbContext(options);
        context.Database.EnsureCreated();
        Insert(context, keys);

        var actual = context
            .Rows.Where(row => row.Id > pivot)
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToList();

        Assert.Equal(expected.Skip(PivotIndex + 1).ToList(), actual);
    }

    #endregion
}
