// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Host.Database.EndToEnd.Tests.Infrastructure;
using Npgsql;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.EndToEnd.Tests;

/// <summary>
/// Proves on a real Postgres database that <c>uuid</c> values order and compare by their big-endian bytes, which is the
/// order a UUIDv7 surrogate key has in .NET. It proves the order that <c>BaseStore</c> pages by on the provider the
/// host runs.
/// </summary>
// See ADR-1517.
[Collection(PostgresCollection.Name)]
public sealed class SurrogateKeyOrderingEndToEndTests(PostgresFixture fixture)
{
    private const int KeyCount = 60;
    private const int PivotIndex = 19;

    private static Guid[] CreateShuffledKeys()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var keys = Enumerable
            .Range(0, KeyCount)
            .Select(i =>
            {
                var timestamp = start.AddMilliseconds(i / 3);
                return Guid.CreateVersion7(timestamp);
            })
            .ToList();

        var random = new Random(843);
        return keys.OrderBy(_ => random.Next()).ToArray();
    }

    private static string ToBigEndianHex(Guid key)
    {
        var bytes = key.ToByteArray(bigEndian: true);
        return Convert.ToHexString(bytes);
    }

    private static List<Guid> CreateExpectedOrder(IEnumerable<Guid> keys)
    {
        return keys.OrderBy(ToBigEndianHex, StringComparer.Ordinal).ToList();
    }

    #region OrderBy Tests

    [Fact]
    public async Task OrderBy_Uuid_OrdersByBigEndianBytes()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);

        await using var connection = new NpgsqlConnection(fixture.OwnerConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT k FROM unnest(@keys) AS t(k) ORDER BY k",
            connection
        );
        command.Parameters.AddWithValue("keys", keys);

        var actual = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            var key = reader.GetGuid(0);
            actual.Add(key);
        }

        Assert.Equal(expected, actual);
    }

    #endregion

    #region Where Tests

    [Fact]
    public async Task Where_GreaterThan_ReturnsTheKeysAfterThePivot()
    {
        var keys = CreateShuffledKeys();
        var expected = CreateExpectedOrder(keys);
        var pivot = expected[PivotIndex];
        var after = expected.Skip(PivotIndex + 1).ToList();

        await using var connection = new NpgsqlConnection(fixture.OwnerConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT k FROM unnest(@keys) AS t(k) WHERE k > @pivot ORDER BY k",
            connection
        );
        command.Parameters.AddWithValue("keys", keys);
        command.Parameters.AddWithValue("pivot", pivot);

        var actual = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            var key = reader.GetGuid(0);
            actual.Add(key);
        }

        Assert.Equal(after, actual);
    }

    #endregion
}
