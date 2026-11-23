// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public class UnprotectedSigningSecretsTests
{
    #region Add Tests

    [Fact]
    public void Add_Always_HoldsTheBytesItWasGiven()
    {
        byte[] first = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] second = [9, 10, 11, 12];
        using var secrets = new UnprotectedSigningSecrets();

        secrets.Add(first);
        secrets.Add(second);

        Assert.Equal(2, secrets.Count);
        Assert.True(secrets[0].SequenceEqual(first));
        Assert.True(secrets[1].SequenceEqual(second));
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_Always_ReleasesEveryRental()
    {
        var secrets = new UnprotectedSigningSecrets();
        secrets.Add(new byte[] { 1, 2, 3, 4 });
        secrets.Add(new byte[] { 5, 6, 7, 8 });

        secrets.Dispose();

        Assert.Throws<ObjectDisposedException>(() => secrets[0].Length);
        Assert.Throws<ObjectDisposedException>(() => secrets[1].Length);
    }

    #endregion
}
