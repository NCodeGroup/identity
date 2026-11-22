// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.Contexts;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class DefaultInteractivePasswordResetterTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<OpenIdContext> MockContext { get; }
    private DefaultInteractivePasswordResetter Resetter { get; } = new();

    public DefaultInteractivePasswordResetterTests()
    {
        MockContext = MockRepository.Create<OpenIdContext>();
    }

    public void Dispose() => MockRepository.Verify();

    #region SendResetCodeAsync Tests

    [Fact]
    public async Task SendResetCodeAsync_Always_ReturnsAnUnguessableHandle()
    {
        var first = await Resetter.SendResetCodeAsync(
            MockContext.Object,
            "alice",
            TestLocales.Xx,
            CancellationToken.None
        );
        var second = await Resetter.SendResetCodeAsync(
            MockContext.Object,
            "alice",
            TestLocales.Xx,
            CancellationToken.None
        );

        Assert.Equal(43, first.Length);
        Assert.Equal(43, second.Length);
        Assert.NotEqual(first, second);
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_Always_ReportsNotSupported()
    {
        var result = await Resetter.ResetPasswordAsync(
            MockContext.Object,
            "flow",
            "123456",
            "a new password",
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractivePasswordChangeErrors.NotSupported, result.Error);
    }

    #endregion
}
