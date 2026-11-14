// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// The default <see cref="IBffClientMemberProtector"/>, which protects each member with an
/// <see cref="IDataProtector"/> under the purpose <see cref="Purpose"/>.
/// </summary>
/// <remarks>
/// <see cref="IDataProtector"/> output is randomized, so the protector caches the protected member per client and UTC
/// day of absolute expiry, and drops the entries of the days before the one it protects. A replica therefore adds one
/// member per client and day to the index.
/// </remarks>
// See ADR-1443 and ADR-1018.
internal sealed class DefaultBffClientMemberProtector : IBffClientMemberProtector
{
    /// <summary>
    /// The purpose of the protector that protects the members.
    /// </summary>
    public const string Purpose = "NCode.Identity.Bff.ClientMembers";

    private IDataProtector Protector { get; }
    private ConcurrentDictionary<(BffSessionClient Client, DateOnly Day), string> Cache { get; } =
        new();

    public DefaultBffClientMemberProtector(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        Protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(BffSessionClient client, DateTimeOffset absoluteExpiresAt)
    {
        var day = DateOnly.FromDateTime(absoluteExpiresAt.UtcDateTime);

        foreach (var key in Cache.Keys)
        {
            if (key.Day < day)
                Cache.TryRemove(key, out _);
        }

        return Cache.GetOrAdd(
            (client, day),
            static (key, protector) => Create(protector, key.Client, key.Day),
            Protector
        );
    }

    /// <inheritdoc />
    public bool TryUnprotect(string member, out BffSessionClient client)
    {
        client = default;

        string plaintext;
        try
        {
            plaintext = Protector.Unprotect(member);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = plaintext.Split('|');
        if (parts.Length != 4)
            return false;

        client = new BffSessionClient(
            parts[1],
            Uri.UnescapeDataString(parts[2]),
            Uri.UnescapeDataString(parts[3])
        );
        return true;
    }

    private static string Create(IDataProtector protector, BffSessionClient client, DateOnly day) =>
        protector.Protect(
            string.Join(
                '|',
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                client.Frontend,
                Uri.EscapeDataString(client.Authority),
                Uri.EscapeDataString(client.ClientId)
            )
        );
}
