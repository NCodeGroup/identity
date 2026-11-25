// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class ReplicatedRevisionContinuationTests
{
    [Fact]
    public void CreateAndTryParse_WhenKindAndKeyArePresent_RoundTrip()
    {
        var continuation = ReplicatedRevisionContinuation.Create("team-authority", "team-a");

        Assert.True(
            ReplicatedRevisionContinuation.TryParse(continuation, out var kind, out var key)
        );
        Assert.Equal("team-authority", kind);
        Assert.Equal("team-a", key);
    }

    [Fact]
    public void CreateAndTryParse_WhenKeyIsEmpty_RoundTrip()
    {
        var continuation = ReplicatedRevisionContinuation.Create(
            "platform-authority",
            string.Empty
        );

        Assert.True(
            ReplicatedRevisionContinuation.TryParse(continuation, out var kind, out var key)
        );
        Assert.Equal("platform-authority", kind);
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void TryParse_WhenKeyContainsTheSeparator_KeepsItInTheKey()
    {
        Assert.True(ReplicatedRevisionContinuation.TryParse("kind:a:b", out var kind, out var key));
        Assert.Equal("kind", kind);
        Assert.Equal("a:b", key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noseparator")]
    [InlineData(":key")]
    public void TryParse_WhenTheContinuationIsInvalid_ReturnsFalse(string? continuation)
    {
        Assert.False(
            ReplicatedRevisionContinuation.TryParse(continuation, out var kind, out var key)
        );
        Assert.Null(kind);
        Assert.Null(key);
    }

    [Fact]
    public void Create_WhenKindContainsTheSeparator_Throws()
    {
        Assert.Throws<ArgumentException>(() => ReplicatedRevisionContinuation.Create("a:b", "key"));
    }
}
