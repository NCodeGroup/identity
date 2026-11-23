// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Buffers;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Server.SecretProtection;
using Xunit;

namespace NCode.Identity.Server.Tests;

public class SecretProtectionCommandLineTests : BaseTests
{
    private static SecretProtectionCommandLine Parse(params string[] args)
    {
        var command = SecretProtectionCommandLine.Parse(args);
        Assert.NotNull(command);
        Assert.Null(command.Error);
        return command;
    }

    private static string ParseError(params string[] args)
    {
        var command = SecretProtectionCommandLine.Parse(args);
        Assert.NotNull(command);
        return Assert.IsType<string>(command.Error);
    }

    private static ServiceProvider CreateServices(
        IMasterKeyRewrapper rewrapper,
        IStartupValidator? validator = null,
        CapturingLoggerProvider? loggerProvider = null
    )
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            if (loggerProvider is not null)
                builder.AddProvider(loggerProvider);
        });
        serviceCollection.AddSingleton(rewrapper);
        if (validator is not null)
            serviceCollection.AddSingleton(validator);

        return serviceCollection.BuildServiceProvider();
    }

    #region Parse Tests

    [Theory]
    [InlineData]
    [InlineData("--urls", "http://localhost")]
    [InlineData("rewrap")]
    public void Parse_ArgsThatAreNotTheCommand_ReturnNull(params string[] args)
    {
        Assert.Null(SecretProtectionCommandLine.Parse(args));
    }

    [Fact]
    public void Parse_Rewrap_HasNoOptionsByDefault()
    {
        var command = Parse("secret-protection", "rewrap");

        Assert.Equal("rewrap", command.Verb);
        Assert.Null(command.RecoveryPrivateKeyPath);
        Assert.Null(command.Owner);
        Assert.False(command.DryRun);
    }

    [Fact]
    public void Parse_EveryOptionOnRewrapRecovery()
    {
        var command = Parse(
            "secret-protection",
            "rewrap-recovery",
            "--recovery-private-key",
            "/etc/key.pem",
            "--owner",
            "tenant:acme",
            "--dry-run"
        );

        Assert.Equal("rewrap-recovery", command.Verb);
        Assert.Equal("/etc/key.pem", command.RecoveryPrivateKeyPath);
        Assert.Equal(SecretOwner.ForTenant("acme"), command.Owner);
        Assert.True(command.DryRun);
    }

    [Fact]
    public void Parse_OptionsInAnyOrder()
    {
        var command = Parse(
            "secret-protection",
            "rewrap",
            "--dry-run",
            "--owner",
            "platform:platform"
        );

        Assert.True(command.DryRun);
        Assert.Equal(SecretOwner.Platform, command.Owner);
    }

    [Theory]
    [InlineData("secret-protection")]
    [InlineData("secret-protection", "destroy")]
    [InlineData("secret-protection", "rewrap", "--force")]
    [InlineData("secret-protection", "rewrap", "--owner")]
    [InlineData("secret-protection", "rewrap", "--owner", "--dry-run")]
    [InlineData("secret-protection", "rewrap", "--recovery-private-key")]
    [InlineData("secret-protection", "rewrap", "--dry-run", "--dry-run")]
    [InlineData("secret-protection", "rewrap", "--owner", "tenant:a", "--owner", "tenant:b")]
    [InlineData(
        "secret-protection",
        "rewrap",
        "--recovery-private-key",
        "a",
        "--recovery-private-key",
        "b"
    )]
    [InlineData("secret-protection", "rewrap", "--owner", "acme")]
    [InlineData("secret-protection", "rewrap", "--owner", "tenant:")]
    [InlineData("secret-protection", "rewrap", "extra")]
    public void Parse_InvalidArgs_ReturnACommandWithAnError(params string[] args)
    {
        Assert.False(string.IsNullOrWhiteSpace(ParseError(args)));
    }

    #endregion

    #region GetHostArguments Tests

    [Fact]
    public void GetHostArguments_TheCommand_ReturnsNoArguments()
    {
        Assert.Empty(
            SecretProtectionCommandLine.GetHostArguments([
                "secret-protection",
                "rewrap",
                "--dry-run",
            ])
        );
    }

    [Fact]
    public void GetHostArguments_OtherArguments_ReturnsThem()
    {
        string[] args = ["--urls", "http://localhost"];

        Assert.Equal(args, SecretProtectionCommandLine.GetHostArguments(args));
    }

    [Fact]
    public void GetHostArguments_NoArguments_ReturnsNone()
    {
        Assert.Empty(SecretProtectionCommandLine.GetHostArguments([]));
    }

    #endregion

    #region Usage Tests

    [Fact]
    public void Usage_NamesTheVerbsAndOptions()
    {
        Assert.Contains(
            "rewrap-recovery",
            SecretProtectionCommandLine.Usage,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "--recovery-private-key",
            SecretProtectionCommandLine.Usage,
            StringComparison.Ordinal
        );
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_NoFailures_ReturnsZeroAndPassesTheOptionsToTheRewrapper()
    {
        MasterKeyRewrapRequest? captured = null;
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x =>
                x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
            )
            .Callback<MasterKeyRewrapRequest, CancellationToken>((r, _) => captured = r)
            .Returns(new ValueTask<MasterKeyRewrapResult>(new MasterKeyRewrapResult(2, 2, 0, 0)))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object);

        var exitCode = await Parse(
                "secret-protection",
                "rewrap-recovery",
                "--owner",
                "tenant:acme",
                "--dry-run"
            )
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.True(captured.RecoveryOnly);
        Assert.True(captured.DryRun);
        Assert.Equal(SecretOwner.ForTenant("acme"), captured.Owner);
        Assert.True(captured.RecoveryPrivateKeyPem.IsEmpty);
    }

    [Fact]
    public async Task RunAsync_AFailedKey_ReturnsOne()
    {
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x =>
                x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
            )
            .Returns(new ValueTask<MasterKeyRewrapResult>(new MasterKeyRewrapResult(2, 1, 0, 1)))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_AnOwnerWithNoMasterKeys_ReturnsOneAndLogsTheOwner()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x =>
                x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
            )
            .Returns(new ValueTask<MasterKeyRewrapResult>(new MasterKeyRewrapResult(0, 0, 0, 0)))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap", "--owner", "tenant:acme")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandOwnerHasNoMasterKeys
        );
        Assert.Null(logged.Exception);
    }

    [Fact]
    public async Task RunAsync_EveryOwnerWithNoMasterKeys_ReturnsZero()
    {
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x =>
                x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
            )
            .Returns(new ValueTask<MasterKeyRewrapResult>(new MasterKeyRewrapResult(0, 0, 0, 0)))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_ReadsTheKeyFileIntoTheRequestAndZeroesItAfterwards()
    {
        const string pem = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n";
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, pem, SecureEncoding.UTF8);
            string? seen = null;
            ReadOnlyMemory<char> memory = default;
            var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
            rewrapper
                .Setup(x =>
                    x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
                )
                .Callback<MasterKeyRewrapRequest, CancellationToken>(
                    (r, _) =>
                    {
                        memory = r.RecoveryPrivateKeyPem;
                        seen = memory.ToString();
                    }
                )
                .Returns(
                    new ValueTask<MasterKeyRewrapResult>(new MasterKeyRewrapResult(1, 1, 0, 0))
                )
                .Verifiable();
            using var services = CreateServices(rewrapper.Object);

            var exitCode = await Parse(
                    "secret-protection",
                    "rewrap",
                    "--recovery-private-key",
                    path
                )
                .RunAsync(services, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(pem, seen);

            // The buffers were sensitive rentals, zeroed when the command ended.
            Assert.All(memory.ToArray(), c => Assert.Equal('\0', c));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_AMissingKeyFile_ReturnsOneWithoutCallingTheRewrapper()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

        var exitCode = await Parse(
                "secret-protection",
                "rewrap",
                "--recovery-private-key",
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
            )
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(rewrapper.Invocations);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionRecoveryKeyUnreadable
        );
        Assert.IsAssignableFrom<IOException>(logged.Exception);
    }

    [Fact]
    public async Task RunAsync_AnEmptyKeyFile_ReturnsOneWithoutCallingTheRewrapper()
    {
        var path = Path.GetTempFileName();
        try
        {
            var loggerProvider = new CapturingLoggerProvider();
            var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
            using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

            var exitCode = await Parse(
                    "secret-protection",
                    "rewrap",
                    "--recovery-private-key",
                    path
                )
                .RunAsync(services, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Empty(rewrapper.Invocations);
            var logged = Assert.Single(
                loggerProvider.Logs,
                x => x.EventId.Id == Logging.EventIds.SecretProtectionRecoveryKeyUnreadable
            );
            Assert.Null(logged.Exception);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_AKeyFileOver64KiB_ReturnsOneWithoutCallingTheRewrapper()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[(64 * 1024) + 1]);
            var loggerProvider = new CapturingLoggerProvider();
            var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
            using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

            var exitCode = await Parse(
                    "secret-protection",
                    "rewrap",
                    "--recovery-private-key",
                    path
                )
                .RunAsync(services, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Empty(rewrapper.Invocations);
            var logged = Assert.Single(
                loggerProvider.Logs,
                x => x.EventId.Id == Logging.EventIds.SecretProtectionRecoveryKeyUnreadable
            );
            Assert.Null(logged.Exception);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_CancelledWhileReadingTheKeyFile_ThrowsWithoutCallingTheRewrapper()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "-----BEGIN PRIVATE KEY-----", SecureEncoding.UTF8);
            var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
            using var services = CreateServices(rewrapper.Object);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Parse("secret-protection", "rewrap", "--recovery-private-key", path)
                    .RunAsync(services, cancelled.Token)
            );

            Assert.Empty(rewrapper.Invocations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_AnInvalidCommandLine_ReturnsTwoWithoutValidatingOrRewrapping()
    {
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        var validator = CreateStrictMock<IStartupValidator>();
        using var services = CreateServices(rewrapper.Object, validator.Object);

        var command = SecretProtectionCommandLine.Parse(["secret-protection", "rewrap", "--force"]);
        Assert.NotNull(command);

        var exitCode = await command.RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Empty(validator.Invocations);
        Assert.Empty(rewrapper.Invocations);
    }

    [Fact]
    public async Task RunAsync_InvalidConfiguration_ReturnsOneWithoutCallingTheRewrapper()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        var validator = CreateStrictMock<IStartupValidator>();
        validator
            .Setup(x => x.Validate())
            .Throws(new OptionsValidationException("x", typeof(object), ["bad"]))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, validator.Object, loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(rewrapper.Invocations);
        Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandConfigurationInvalid
        );
    }

    [Fact]
    public async Task RunAsync_TwoOptionsTypesAreInvalid_ReturnsOneWithoutCallingTheRewrapper()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        var validator = CreateStrictMock<IStartupValidator>();
        validator
            .Setup(x => x.Validate())
            .Throws(
                new AggregateException(
                    new OptionsValidationException("a", typeof(object), ["bad a"]),
                    new OptionsValidationException("b", typeof(object), ["bad b"])
                )
            )
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, validator.Object, loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(rewrapper.Invocations);
        Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandConfigurationInvalid
        );
    }

    [Fact]
    public async Task RunAsync_TheValidatorThrowsAnotherException_ReturnsOneWithoutCallingTheRewrapper()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var failure = new InvalidOperationException("boom");
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        var validator = CreateStrictMock<IStartupValidator>();
        validator.Setup(x => x.Validate()).Throws(failure).Verifiable();
        using var services = CreateServices(rewrapper.Object, validator.Object, loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(rewrapper.Invocations);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandFailed
        );
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public async Task RunAsync_NoRewrapperIsRegistered_ReturnsOneAndLogsTheFailure()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddProvider(loggerProvider));
        using var services = serviceCollection.BuildServiceProvider();

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandFailed
        );
        Assert.IsType<InvalidOperationException>(logged.Exception);
    }

    [Fact]
    public async Task RunAsync_TheRewrapperThrows_ReturnsOneAndLogsTheFailure()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var failure = new InvalidOperationException("boom");
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x =>
                x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), It.IsAny<CancellationToken>())
            )
            .Returns(() => ValueTask.FromException<MasterKeyRewrapResult>(failure))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap-recovery")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandFailed
        );
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public async Task RunAsync_TheCallerCancels_PropagatesWithoutLogging()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();

        var loggerProvider = new CapturingLoggerProvider();
        var cancellation = new OperationCanceledException(cancellationToken);
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x => x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), cancellationToken))
            .Returns(() => ValueTask.FromException<MasterKeyRewrapResult>(cancellation))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Parse("secret-protection", "rewrap").RunAsync(services, cancellationToken)
        );

        Assert.Same(cancellation, thrown);
        Assert.Empty(loggerProvider.Logs);
    }

    [Fact]
    public async Task RunAsync_TheRewrapperFailsAfterTheCallerCancels_ReturnsOneAndLogsTheFailure()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();

        var loggerProvider = new CapturingLoggerProvider();
        var failure = new InvalidOperationException("The database is unreachable.");
        var rewrapper = CreateStrictMock<IMasterKeyRewrapper>();
        rewrapper
            .Setup(x => x.RewrapAsync(It.IsAny<MasterKeyRewrapRequest>(), cancellationToken))
            .Returns(() => ValueTask.FromException<MasterKeyRewrapResult>(failure))
            .Verifiable();
        using var services = CreateServices(rewrapper.Object, loggerProvider: loggerProvider);

        var exitCode = await Parse("secret-protection", "rewrap")
            .RunAsync(services, cancellationToken);

        Assert.Equal(1, exitCode);
        var logged = Assert.Single(
            loggerProvider.Logs,
            x => x.EventId.Id == Logging.EventIds.SecretProtectionCommandFailed
        );
        Assert.Same(failure, logged.Exception);
    }

    #endregion
}
