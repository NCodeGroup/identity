// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public partial class DefaultLocalAccountEmailCodesTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string LocalAccountId = "account-1";
    private const string Email = "user@example.com";
    private const string FlowId = "flow-1";
    private const string Code = "123456";
    private const string Purpose = EmailCodePurposes.ResetPassword;

    private static DateTimeOffset Now { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    private Mock<IExpiringValueSet> MockValueSet { get; }
    private Mock<IExpiringCounterSet> MockCounterSet { get; }
    private Mock<IMailSender> MockMailSender { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private FakeTimeProvider TimeProvider { get; } = new(Now);
    private DefaultLocalAccountEmailCodes EmailCodes { get; }

    public DefaultLocalAccountEmailCodesTests()
    {
        MockValueSet = CreateStrictMock<IExpiringValueSet>();
        MockCounterSet = CreateStrictMock<IExpiringCounterSet>();
        MockMailSender = CreateStrictMock<IMailSender>();
        MockCryptoService = CreateStrictMock<ICryptoService>();
        EmailCodes = new DefaultLocalAccountEmailCodes(
            MockValueSet.Object,
            MockCounterSet.Object,
            MockMailSender.Object,
            MockCryptoService.Object,
            TimeProvider
        );
    }

    #region Scaffolding

    private static string FlowKey { get; } =
        DefaultLocalAccountEmailCodes.CreateFlowKey(TenantId, Purpose, FlowId);

    private static string SendsKey { get; } =
        ExpiringKeys.Create(
            DefaultLocalAccountEmailCodes.SendsKeyPurpose,
            TenantId,
            Purpose,
            LocalAccountId
        );

    private Mock<OpenIdContext> CreateContext(Mock<IReadOnlySettingCollection>? mockSettings)
    {
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();

        if (mockSettings is not null)
        {
            var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
            mockProvider.Setup(x => x.Collection).Returns(mockSettings.Object).Verifiable();
            mockTenant.Setup(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        }

        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        return mockContext;
    }

    private Mock<IReadOnlySettingCollection> CreateSettings(int sendsPerHour)
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailCodeSendsPerHour))
            .Returns(sendsPerHour)
            .Verifiable();
        return mockSettings;
    }

    private void SetupSends(long count) =>
        MockCounterSet
            .Setup(x =>
                x.IncrementAsync(
                    SendsKey,
                    Now + TimeSpan.FromHours(1),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(count)
            .Verifiable();

    private static PersistedLocalAccount Account() =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = LocalAccountId,
            UserName = "user",
            Email = Email,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private static byte[] Serialize(int attempts) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new EmailCodeState(
                LocalAccountId,
                Email,
                DefaultLocalAccountEmailCodes.HashCode(FlowId, Code),
                attempts,
                Now + Lifetime
            ),
            EmailCodeJsonContext.Default.EmailCodeState
        );

    private static EmailCodeState Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, EmailCodeJsonContext.Default.EmailCodeState)!;

    private void SetupGet(byte[]? bytes, long version = 3) =>
        MockValueSet
            .Setup(x => x.GetAsync(FlowKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes is null ? null : new ExpiringValue(bytes, version))
            .Verifiable();

    private void SetupReplace(long version, bool replaced, Action<byte[]>? onReplace = null) =>
        MockValueSet
            .Setup(x =>
                x.TryReplaceAsync(
                    FlowKey,
                    version,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Now + Lifetime,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, long, ReadOnlyMemory<byte>, DateTimeOffset, CancellationToken>(
                (_, _, value, _, _) => onReplace?.Invoke(value.ToArray())
            )
            .ReturnsAsync(replaced)
            .Verifiable();

    private void SetupRemove() =>
        MockValueSet
            .Setup(x => x.RemoveAsync(FlowKey, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigits();

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WithinTheHourlyLimit_StoresOnlyAHashOfTheCodeAndMailsIt()
    {
        var mockSettings = CreateSettings(sendsPerHour: 5);
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailCodeLifetime))
            .Returns(Lifetime)
            .Verifiable();
        mockSettings
            .Setup(x => x.GetValue(InteractiveSettingKeys.BrandingDisplayName))
            .Returns("Acme")
            .Verifiable();
        var mockContext = CreateContext(mockSettings);
        SetupSends(count: 5);
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();

        byte[]? storedBytes = null;
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Now + Lifetime,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ReadOnlyMemory<byte>, DateTimeOffset, CancellationToken>(
                (_, value, _, _) => storedBytes = value.ToArray()
            )
            .ReturnsAsync(true)
            .Verifiable();

        MailMessage? message = null;
        MockMailSender
            .Setup(x => x.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MailMessage, CancellationToken>((sent, _) => message = sent)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var flowId = await EmailCodes.SendAsync(
            mockContext.Object,
            Purpose,
            Account(),
            LocalAccountMailComposerTests.CreateLocale(),
            CancellationToken.None
        );

        Assert.Equal(FlowId, flowId);
        Assert.NotNull(message);
        Assert.Equal(Email, message.To);
        Assert.Equal("Acme", message.FromDisplayName);
        Assert.Equal("Reset subject", message.Subject);

        var match = SixDigits().Match(message.TextBody);
        Assert.True(match.Success);
        Assert.Contains("within 10 minutes", message.TextBody);

        Assert.NotNull(storedBytes);
        var state = Deserialize(storedBytes);
        Assert.Equal(LocalAccountId, state.LocalAccountId);
        Assert.Equal(Email, state.Email);
        Assert.Equal(0, state.Attempts);
        Assert.Equal(Now + Lifetime, state.ExpiresWhen);
        Assert.Equal(DefaultLocalAccountEmailCodes.HashCode(FlowId, match.Value), state.CodeHash);
    }

    [Fact]
    public async Task SendAsync_PastTheHourlyLimit_SendsNothing()
    {
        var mockSettings = CreateSettings(sendsPerHour: 5);
        var mockContext = CreateContext(mockSettings);
        SetupSends(count: 6);

        var flowId = await EmailCodes.SendAsync(
            mockContext.Object,
            Purpose,
            Account(),
            LocalAccountMailComposerTests.CreateLocale(),
            CancellationToken.None
        );

        Assert.Null(flowId);
    }

    [Fact]
    public async Task SendAsync_WhenTheFlowHandleExists_Throws()
    {
        var mockSettings = CreateSettings(sendsPerHour: 5);
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailCodeLifetime))
            .Returns(Lifetime)
            .Verifiable();
        var mockContext = CreateContext(mockSettings);
        SetupSends(count: 1);
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Now + Lifetime,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await EmailCodes.SendAsync(
                mockContext.Object,
                Purpose,
                Account(),
                LocalAccountMailComposerTests.CreateLocale(),
                CancellationToken.None
            )
        );

        MockMailSender.VerifyNoOtherCalls();
    }

    #endregion

    #region SendDecoyAsync Tests

    private static string DecoyKeyFor(string loweredIdentifier) =>
        ExpiringKeys.Create(
            DefaultLocalAccountEmailCodes.DecoySendsKeyPurpose,
            TenantId,
            Purpose,
            Convert.ToHexString(SHA256.HashData(SecureEncoding.UTF8.GetBytes(loweredIdentifier)))
        );

    private static DateTimeOffset DecoyExpiry { get; } =
        Now + DefaultLocalAccountEmailCodes.DecoyLifetime;

    [Fact]
    public async Task SendDecoyAsync_Always_StoresAFlowNoCodeVerifiesAndSendsNoMail()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockCounterSet
            .Setup(x =>
                x.IncrementAsync(DecoyKeyFor("user"), DecoyExpiry, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(1)
            .Verifiable();
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();

        byte[]? storedBytes = null;
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    DecoyExpiry,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ReadOnlyMemory<byte>, DateTimeOffset, CancellationToken>(
                (_, value, _, _) => storedBytes = value.ToArray()
            )
            .ReturnsAsync(true)
            .Verifiable();

        var flowId = await EmailCodes.SendDecoyAsync(
            mockContext.Object,
            Purpose,
            "User",
            CancellationToken.None
        );

        Assert.Equal(FlowId, flowId);
        Assert.NotNull(storedBytes);
        var state = Deserialize(storedBytes);
        Assert.Equal(0, state.Attempts);
        Assert.Equal(DecoyExpiry, state.ExpiresWhen);
        for (var number = 0; number < 1_000; ++number)
        {
            var code = number.ToString("D6", CultureInfo.InvariantCulture);
            Assert.NotEqual(DefaultLocalAccountEmailCodes.HashCode(FlowId, code), state.CodeHash);
        }

        MockMailSender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendDecoyAsync_Always_CountsUnderAKeyOfItsOwn()
    {
        var mockContext = CreateContext(mockSettings: null);
        var decoyKey = DecoyKeyFor("user");
        MockCounterSet
            .Setup(x => x.IncrementAsync(decoyKey, DecoyExpiry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Verifiable();
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    DecoyExpiry,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        await EmailCodes.SendDecoyAsync(
            mockContext.Object,
            Purpose,
            "USER",
            CancellationToken.None
        );

        Assert.NotEqual(SendsKey, decoyKey);
        MockCounterSet.Verify(
            x =>
                x.IncrementAsync(
                    SendsKey,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockCounterSet.Verify(
            x => x.IncrementAsync(decoyKey, DecoyExpiry, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SendDecoyAsync_Always_ExpiresItsEntriesWithinAMinute()
    {
        var mockContext = CreateContext(mockSettings: null);
        DateTimeOffset? counterExpiry = null;
        MockCounterSet
            .Setup(x =>
                x.IncrementAsync(
                    DecoyKeyFor("user"),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, DateTimeOffset, CancellationToken>(
                (_, expiresWhen, _) => counterExpiry = expiresWhen
            )
            .ReturnsAsync(1)
            .Verifiable();
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();

        DateTimeOffset? valueExpiry = null;
        byte[]? storedBytes = null;
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ReadOnlyMemory<byte>, DateTimeOffset, CancellationToken>(
                (_, value, expiresWhen, _) =>
                {
                    storedBytes = value.ToArray();
                    valueExpiry = expiresWhen;
                }
            )
            .ReturnsAsync(true)
            .Verifiable();

        await EmailCodes.SendDecoyAsync(
            mockContext.Object,
            Purpose,
            "user",
            CancellationToken.None
        );

        var latest = Now + TimeSpan.FromMinutes(1);
        Assert.Equal(latest, counterExpiry);
        Assert.Equal(latest, valueExpiry);
        Assert.NotNull(storedBytes);
        Assert.Equal(latest, Deserialize(storedBytes).ExpiresWhen);
    }

    [Fact]
    public async Task SendDecoyAsync_WhenTheFlowHandleExists_Throws()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockCounterSet
            .Setup(x =>
                x.IncrementAsync(DecoyKeyFor("user"), DecoyExpiry, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(1)
            .Verifiable();
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    DecoyExpiry,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await EmailCodes.SendDecoyAsync(
                mockContext.Object,
                Purpose,
                "user",
                CancellationToken.None
            )
        );
    }

    #endregion

    #region AddDecoyFlowAsync Tests

    [Fact]
    public async Task AddDecoyFlowAsync_Always_StoresAFlowNoCodeVerifiesWithoutCounting()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(FlowId)
            .Verifiable();

        byte[]? storedBytes = null;
        MockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    FlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    DecoyExpiry,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ReadOnlyMemory<byte>, DateTimeOffset, CancellationToken>(
                (_, value, _, _) => storedBytes = value.ToArray()
            )
            .ReturnsAsync(true)
            .Verifiable();

        var flowId = await EmailCodes.AddDecoyFlowAsync(
            mockContext.Object,
            Purpose,
            CancellationToken.None
        );

        Assert.Equal(FlowId, flowId);
        Assert.NotNull(storedBytes);
        var state = Deserialize(storedBytes);
        Assert.Equal(string.Empty, state.LocalAccountId);
        Assert.Equal(string.Empty, state.Email);
        Assert.Equal(0, state.Attempts);
        Assert.Equal(DecoyExpiry, state.ExpiresWhen);
        for (var number = 0; number < 1_000; ++number)
        {
            var code = number.ToString("D6", CultureInfo.InvariantCulture);
            Assert.NotEqual(DefaultLocalAccountEmailCodes.HashCode(FlowId, code), state.CodeHash);
        }

        MockCounterSet.VerifyNoOtherCalls();
        MockMailSender.VerifyNoOtherCalls();
    }

    #endregion

    #region VerifyAsync Tests

    [Fact]
    public async Task VerifyAsync_WhenTheCodeIsRight_ReturnsTheAccountAndTheAddress()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(Serialize(attempts: 2));
        SetupReplace(version: 3, replaced: true);

        var match = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.NotNull(match);
        Assert.Equal(LocalAccountId, match.LocalAccountId);
        Assert.Equal(Email, match.Email);
    }

    [Fact]
    public async Task VerifyAsync_Always_CountsTheGuessBeforeJudgingIt()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(Serialize(attempts: 2));

        byte[]? replacedBytes = null;
        SetupReplace(version: 3, replaced: true, onReplace: bytes => replacedBytes = bytes);

        var match = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.NotNull(match);
        Assert.NotNull(replacedBytes);
        Assert.Equal(3, Deserialize(replacedBytes).Attempts);
        MockValueSet.Verify(
            x =>
                x.TryReplaceAsync(
                    FlowKey,
                    3,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    Now + Lifetime,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task VerifyAsync_WithAWrongCode_CountsTheAttempt()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(Serialize(attempts: 2));

        byte[]? replacedBytes = null;
        SetupReplace(version: 3, replaced: true, onReplace: bytes => replacedBytes = bytes);

        var accountId = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            "000000",
            CancellationToken.None
        );

        Assert.Null(accountId);
        Assert.NotNull(replacedBytes);
        var state = Deserialize(replacedBytes);
        Assert.Equal(3, state.Attempts);
        Assert.Equal(LocalAccountId, state.LocalAccountId);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheFlowHasUsedItsAttempts_RefusesTheRightCodeAndEndsTheFlow()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(Serialize(attempts: DefaultLocalAccountEmailCodes.MaxAttempts));
        SetupRemove();

        var match = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.Null(match);
        MockValueSet.Verify(
            x =>
                x.TryReplaceAsync(
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task VerifyAsync_WhenAnotherGuessChangedTheFlow_ReadsItAgainAndCountsAtTheNewVersion()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockValueSet
            .SetupSequence(x => x.GetAsync(FlowKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiringValue(Serialize(attempts: 1), Version: 3))
            .ReturnsAsync(new ExpiringValue(Serialize(attempts: 2), Version: 4));
        SetupReplace(version: 3, replaced: false);

        byte[]? replacedBytes = null;
        SetupReplace(version: 4, replaced: true, onReplace: bytes => replacedBytes = bytes);

        var match = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.NotNull(match);
        Assert.NotNull(replacedBytes);
        Assert.Equal(3, Deserialize(replacedBytes).Attempts);
        MockValueSet.Verify(
            x => x.GetAsync(FlowKey, It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task VerifyAsync_WhenTheFifthGuessIsWrong_EndsTheFlow()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(Serialize(attempts: DefaultLocalAccountEmailCodes.MaxAttempts - 1));
        SetupReplace(version: 3, replaced: true);
        SetupRemove();

        var accountId = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            "000000",
            CancellationToken.None
        );

        Assert.Null(accountId);
    }

    [Fact]
    public async Task VerifyAsync_ForAnUnknownFlow_ReturnsNull()
    {
        var mockContext = CreateContext(mockSettings: null);
        SetupGet(bytes: null);

        var accountId = await EmailCodes.VerifyAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.Null(accountId);
    }

    #endregion

    #region ConsumeAsync Tests

    [Fact]
    public async Task ConsumeAsync_WhenTheFlowExists_TakesItAndReturnsTrue()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockValueSet
            .Setup(x => x.TryTakeAsync(FlowKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiringValue(Serialize(attempts: 0), Version: 3))
            .Verifiable();

        var consumed = await EmailCodes.ConsumeAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            CancellationToken.None
        );

        Assert.True(consumed);
    }

    [Fact]
    public async Task ConsumeAsync_WhenTheFlowIsGone_ReturnsFalse()
    {
        var mockContext = CreateContext(mockSettings: null);
        MockValueSet
            .Setup(x => x.TryTakeAsync(FlowKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExpiringValue?)null)
            .Verifiable();

        var consumed = await EmailCodes.ConsumeAsync(
            mockContext.Object,
            Purpose,
            FlowId,
            CancellationToken.None
        );

        Assert.False(consumed);
    }

    #endregion
}
