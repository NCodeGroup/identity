// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.Time;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public class AuditRecordOptionsValidatorTests
{
    #region Validate Tests

    [Fact]
    public void Validate_WithTheDefaults_Succeeds()
    {
        var result = new AuditRecordOptionsValidator().Validate(null, new AuditRecordOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithOutOfRangeValues_ReportsEach()
    {
        var options = new AuditRecordOptions
        {
            FlushInterval = TimeSpan.Zero,
            QueueCapacity = 0,
            Retention = TimeSpan.FromHours(23),
        };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failures = result.Failures ?? [];
        Assert.Contains(
            failures,
            failure => failure.Contains("FlushInterval", StringComparison.Ordinal)
        );
        Assert.Contains(
            failures,
            failure => failure.Contains("QueueCapacity", StringComparison.Ordinal)
        );
        Assert.Contains(
            failures,
            failure => failure.Contains("Retention", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Validate_WhenThePruneIntervalIsZero_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { PruneInterval = TimeSpan.Zero };
        var validator = new AuditRecordOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:PruneInterval'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenThePruneIntervalExceedsATimerPeriod_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions
        {
            PruneInterval = TimerPeriod.MaxValue + TimeSpan.FromMilliseconds(1),
        };
        var validator = new AuditRecordOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:PruneInterval'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamPollIntervalIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamPollInterval = TimeSpan.FromMinutes(6) };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamPollInterval'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamSettleDelayIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamSettleDelay = TimeSpan.FromHours(2) };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamSettleDelay'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamBatchSizeIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamBatchSize = 1001 };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamBatchSize'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamLeaseDurationIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamLeaseDuration = TimeSpan.FromSeconds(29) };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamLeaseDuration'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamRetryDelayIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamRetryDelay = TimeSpan.Zero };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamRetryDelay'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamMaxRetryDelayIsBelowTheRetryDelay_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions
        {
            StreamRetryDelay = TimeSpan.FromMinutes(1),
            StreamMaxRetryDelay = TimeSpan.FromSeconds(30),
        };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamMaxRetryDelay'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamMaxRetryDelayExceedsADay_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions
        {
            StreamMaxRetryDelay = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1),
        };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamMaxRetryDelay'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenTheStreamsPerPollIsOutOfRange_FailsNamingItsKey()
    {
        var options = new AuditRecordOptions { StreamsPerPoll = 0 };

        var result = new AuditRecordOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "'AuditRecords:StreamsPerPoll'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    #endregion
}
