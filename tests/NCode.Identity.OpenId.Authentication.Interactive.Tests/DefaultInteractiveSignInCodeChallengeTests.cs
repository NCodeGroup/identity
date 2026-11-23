// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Contexts;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class DefaultInteractiveSignInCodeChallengeTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<OpenIdContext> MockContext { get; }
    private ClaimsPrincipal Principal { get; } = new();
    private DefaultInteractiveSignInCodeChallenge Challenge { get; } = new();

    public DefaultInteractiveSignInCodeChallengeTests()
    {
        MockContext = MockRepository.Create<OpenIdContext>();
    }

    public void Dispose() => MockRepository.Verify();

    #region SendSignInCodeAsync Tests

    [Fact]
    public async Task SendSignInCodeAsync_Always_RefusesWithNotSupported()
    {
        var result = await Challenge.SendSignInCodeAsync(
            MockContext.Object,
            Principal,
            TestLocales.Xx,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Null(result.FlowId);
        Assert.Equal(InteractiveEmailCodeErrors.NotSupported, result.Error);
    }

    #endregion

    #region VerifySignInCodeAsync Tests

    [Fact]
    public async Task VerifySignInCodeAsync_Always_RefusesWithNotSupported()
    {
        using var code = Secret.Create("123456".AsSpan());
        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            "flow",
            code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.NotSupported, result.Error);
    }

    #endregion
}
