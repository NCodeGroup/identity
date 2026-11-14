// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using NCode.Identity.Bff.Sessions;
using Xunit;

namespace NCode.Identity.Bff.Tests.Sessions;

public class DefaultBffClientMemberProtectorTests : BaseTests
{
    private IDataProtectionProvider Provider { get; } = new EphemeralDataProtectionProvider();
    private DefaultBffClientMemberProtector Protector { get; }

    public DefaultBffClientMemberProtectorTests()
    {
        Protector = new DefaultBffClientMemberProtector(Provider);
    }

    private static BffSessionClient CreateClient() =>
        new("web", "https://op.example/a|b%7C", "client|id%");

    private static DateTimeOffset Absolute { get; } = new(2030, 5, 6, 7, 8, 9, TimeSpan.Zero);

    #region Protect Tests

    [Fact]
    public void Protect_Always_RoundTripsThroughTryUnprotect()
    {
        var client = CreateClient();

        var member = Protector.Protect(client, Absolute);
        var unprotected = Protector.TryUnprotect(member, out var result);

        Assert.True(unprotected);
        Assert.Equal(client, result);
    }

    [Fact]
    public void Protect_Always_DoesNotExposeTheClient()
    {
        var member = Protector.Protect(CreateClient(), Absolute);

        Assert.DoesNotContain("web", member, StringComparison.Ordinal);
        Assert.DoesNotContain("op.example", member, StringComparison.Ordinal);
    }

    [Fact]
    public void Protect_SameClientAndDay_GivesTheSameMember()
    {
        var first = Protector.Protect(CreateClient(), Absolute);
        var second = Protector.Protect(CreateClient(), Absolute.AddHours(1));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Protect_AnotherDay_GivesAnotherMember()
    {
        var first = Protector.Protect(CreateClient(), Absolute);
        var second = Protector.Protect(CreateClient(), Absolute.AddDays(1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Protect_AnotherClient_GivesAnotherMember()
    {
        var first = Protector.Protect(CreateClient(), Absolute);
        var second = Protector.Protect(
            new BffSessionClient("web", "https://op.example", "other"),
            Absolute
        );

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Protect_AfterALaterDay_ProtectsAnEarlierDayAgain()
    {
        var first = Protector.Protect(CreateClient(), Absolute);
        _ = Protector.Protect(CreateClient(), Absolute.AddDays(1));

        var again = Protector.Protect(CreateClient(), Absolute);

        Assert.NotEqual(first, again);
        Assert.True(Protector.TryUnprotect(again, out var client));
        Assert.Equal(CreateClient(), client);
    }

    #endregion

    #region TryUnprotect Tests

    [Theory]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("2030-05-06|web|authority|client")]
    public void TryUnprotect_NotAProtectedMember_ReturnsFalse(string member)
    {
        var result = Protector.TryUnprotect(member, out var client);

        Assert.False(result);
        Assert.Equal(default, client);
    }

    [Fact]
    public void TryUnprotect_TamperedMember_ReturnsFalse()
    {
        var member = Protector.Protect(CreateClient(), Absolute);
        var tampered = member[..^2] + (member[^1] == 'A' ? "BB" : "AA");

        var result = Protector.TryUnprotect(tampered, out var client);

        Assert.False(result);
        Assert.Equal(default, client);
    }

    [Fact]
    public void TryUnprotect_MemberOfAnotherPurpose_ReturnsFalse()
    {
        var other = Provider.CreateProtector("another-purpose");
        var member = other.Protect("2030-05-06|web|authority|client");

        var result = Protector.TryUnprotect(member, out var client);

        Assert.False(result);
        Assert.Equal(default, client);
    }

    [Fact]
    public void TryUnprotect_ProtectedWithAnotherPayloadShape_ReturnsFalse()
    {
        var purpose = Provider.CreateProtector(DefaultBffClientMemberProtector.Purpose);
        var member = purpose.Protect("2030-05-06|web|authority|client|extra");

        var result = Protector.TryUnprotect(member, out _);

        Assert.False(result);
    }

    #endregion
}
