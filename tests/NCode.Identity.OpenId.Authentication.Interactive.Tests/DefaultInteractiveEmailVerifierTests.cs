// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Contexts;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class DefaultInteractiveEmailVerifierTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<OpenIdContext> MockContext { get; }
    private ClaimsPrincipal Principal { get; } = new();
    private DefaultInteractiveEmailVerifier Verifier { get; } = new();

    public DefaultInteractiveEmailVerifierTests()
    {
        MockContext = MockRepository.Create<OpenIdContext>();
    }

    public void Dispose() => MockRepository.Verify();

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_Always_ReturnsNull()
    {
        var status = await Verifier.GetStatusAsync(
            MockContext.Object,
            Principal,
            CancellationToken.None
        );

        Assert.Null(status);
    }

    #endregion

    #region SendVerificationCodeAsync Tests

    [Fact]
    public async Task SendVerificationCodeAsync_Always_RefusesWithNotSupported()
    {
        var result = await Verifier.SendVerificationCodeAsync(
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

    #region VerifyEmailAsync Tests

    [Fact]
    public async Task VerifyEmailAsync_Always_RefusesWithNotSupported()
    {
        using var code = Secret.Create("123456".AsSpan());
        var result = await Verifier.VerifyEmailAsync(
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
