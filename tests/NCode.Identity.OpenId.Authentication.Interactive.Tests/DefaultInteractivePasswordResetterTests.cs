// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class DefaultInteractivePasswordResetterTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<OpenIdContext> MockContext { get; }
    private DefaultInteractivePasswordResetter Resetter { get; } = new();
    private ResolvedConnection Connection { get; }

    public DefaultInteractivePasswordResetterTests()
    {
        MockContext = MockRepository.Create<OpenIdContext>();
        Connection = CreateConnection(MockRepository);
    }

    public void Dispose() => MockRepository.Verify();

    private static ResolvedConnection CreateConnection(MockRepository mockRepository) =>
        new()
        {
            Connection = new PersistedConnection
            {
                TenantId = "acme",
                ConnectionId = "connection-1",
                Name = "local",
                Strategy = "local",
                DisplayName = "Username and password",
                IsEnabled = true,
                Options = JsonSerializer.SerializeToElement(new { store = "default" }),
                Settings = new PersistedConnectionSettings
                {
                    TenantId = "acme",
                    ConnectionId = "connection-1",
                    Value = JsonSerializer.SerializeToElement(new { }),
                },
            },
            Strategy = mockRepository.Create<IConnectionStrategy>().Object,
            Settings = mockRepository.Create<IReadOnlySettingCollection>().Object,
        };

    #region SendResetCodeAsync Tests

    [Fact]
    public async Task SendResetCodeAsync_Always_ReturnsAnUnguessableHandle()
    {
        var first = await Resetter.SendResetCodeAsync(
            MockContext.Object,
            Connection,
            "alice",
            TestLocales.Xx,
            CancellationToken.None
        );
        var second = await Resetter.SendResetCodeAsync(
            MockContext.Object,
            Connection,
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
        using var code = Secret.Create("123456".AsSpan());
        using var newPassword = Secret.Create("a new password".AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            MockContext.Object,
            "flow",
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractivePasswordChangeErrors.NotSupported, result.Error);
    }

    #endregion
}
