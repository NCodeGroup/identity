// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Contexts;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class EmailCodeRedemptionTests : BaseTests
{
    private const string LocalAccountId = "account-1";
    private const string Email = "user@example.com";
    private const string FlowId = "flow-1";
    private const string Code = "123456";

    private Mock<ILocalAccountEmailCodes> MockEmailCodes { get; }
    private Mock<OpenIdContext> MockContext { get; }

    public EmailCodeRedemptionTests()
    {
        MockEmailCodes = CreateStrictMock<ILocalAccountEmailCodes>();
        MockContext = CreateStrictMock<OpenIdContext>();
    }

    #region Scaffolding

    private static PersistedLocalAccount Account() =>
        new()
        {
            TenantId = "tenant-1",
            LocalAccountId = LocalAccountId,
            UserName = "user",
            DisplayName = "Display Name",
            Email = Email,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private void SetupVerify(EmailCodeMatch? match) =>
        MockEmailCodes
            .Setup(x =>
                x.VerifyAsync(
                    MockContext.Object,
                    EmailCodePurposes.VerifyEmail,
                    FlowId,
                    It.Is<Secret<char>>(s => SecretArguments.Reveals(s, Code)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(match)
            .Verifiable();

    private void SetupConsume(bool consumed) =>
        MockEmailCodes
            .Setup(x =>
                x.ConsumeAsync(
                    MockContext.Object,
                    EmailCodePurposes.VerifyEmail,
                    FlowId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(consumed)
            .Verifiable();

    private ValueTask<bool> Redeem(Secret<char> code, CancellationToken cancellationToken) =>
        EmailCodeRedemption.TryRedeemAsync(
            MockEmailCodes.Object,
            MockContext.Object,
            EmailCodePurposes.VerifyEmail,
            FlowId,
            code,
            Account(),
            cancellationToken
        );

    #endregion

    #region TryRedeemAsync Tests

    [Fact]
    public async Task TryRedeemAsync_WhenNoFlowMatches_ReturnsFalseWithoutConsuming()
    {
        using var code = Secret.Create(Code.AsSpan());
        SetupVerify(null);

        var result = await Redeem(code, CancellationToken.None);

        Assert.False(result);
        MockEmailCodes.Verify(
            x =>
                x.ConsumeAsync(
                    It.IsAny<OpenIdContext>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task TryRedeemAsync_WhenTheMatchIsForAnotherAccount_ReturnsFalseWithoutConsuming()
    {
        using var code = Secret.Create(Code.AsSpan());
        SetupVerify(new EmailCodeMatch("account-2", Email));

        var result = await Redeem(code, CancellationToken.None);

        Assert.False(result);
        MockEmailCodes.Verify(
            x =>
                x.ConsumeAsync(
                    It.IsAny<OpenIdContext>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task TryRedeemAsync_WhenTheFlowWasConsumedFirst_ReturnsFalse()
    {
        using var code = Secret.Create(Code.AsSpan());
        SetupVerify(new EmailCodeMatch(LocalAccountId, Email));
        SetupConsume(consumed: false);

        var result = await Redeem(code, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenTheCodeMatchesTheAccount_ConsumesTheFlowAndReturnsTrue()
    {
        using var code = Secret.Create(Code.AsSpan());
        SetupVerify(new EmailCodeMatch(LocalAccountId, Email));
        SetupConsume(consumed: true);

        var result = await Redeem(code, CancellationToken.None);

        Assert.True(result);
    }

    #endregion
}
