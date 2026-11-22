// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Time;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// Validates <see cref="AuditRecordOptions"/> when the host starts.
/// </summary>
// See ADR-1303 and ADR-1137.
internal sealed class AuditRecordOptionsValidator : IValidateOptions<AuditRecordOptions>
{
    private static TimeSpan OneSecond { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan ThirtySeconds { get; } = TimeSpan.FromSeconds(30);

    private static TimeSpan FiveMinutes { get; } = TimeSpan.FromMinutes(5);

    private static TimeSpan OneHour { get; } = TimeSpan.FromHours(1);

    private static TimeSpan OneDay { get; } = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuditRecordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!TimerPeriod.IsValid(options.FlushInterval))
        {
            failures.Add(
                $"{nameof(AuditRecordOptions)}.{nameof(AuditRecordOptions.FlushInterval)} ('{AuditRecordOptions.SectionName}:{nameof(AuditRecordOptions.FlushInterval)}') must be between {TimerPeriod.MinValue} and {TimerPeriod.MaxValue}, the periods a timer accepts; it is {options.FlushInterval}."
            );
        }

        if (options.QueueCapacity < 1)
        {
            failures.Add(
                $"{nameof(AuditRecordOptions)}.{nameof(AuditRecordOptions.QueueCapacity)} ('{AuditRecordOptions.SectionName}:{nameof(AuditRecordOptions.QueueCapacity)}') must be at least 1; it is {options.QueueCapacity}."
            );
        }

        if (options.Retention < OneDay)
        {
            failures.Add(
                $"{nameof(AuditRecordOptions)}.{nameof(AuditRecordOptions.Retention)} ('{AuditRecordOptions.SectionName}:{nameof(AuditRecordOptions.Retention)}') must be at least one day; it is {options.Retention}."
            );
        }

        if (!TimerPeriod.IsValid(options.PruneInterval))
        {
            failures.Add(
                $"{nameof(AuditRecordOptions)}.{nameof(AuditRecordOptions.PruneInterval)} ('{AuditRecordOptions.SectionName}:{nameof(AuditRecordOptions.PruneInterval)}') must be between {TimerPeriod.MinValue} and {TimerPeriod.MaxValue}, the periods a timer accepts; it is {options.PruneInterval}."
            );
        }

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamPollInterval),
            options.StreamPollInterval,
            OneSecond,
            FiveMinutes
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamSettleDelay),
            options.StreamSettleDelay,
            OneSecond,
            OneHour
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamBatchSize),
            options.StreamBatchSize,
            1,
            1000
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamLeaseDuration),
            options.StreamLeaseDuration,
            ThirtySeconds,
            OneHour
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamRetryDelay),
            options.StreamRetryDelay,
            OneSecond,
            OneHour
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamMaxRetryDelay),
            options.StreamMaxRetryDelay,
            options.StreamRetryDelay,
            OneDay
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamsPerPoll),
            options.StreamsPerPoll,
            1,
            1000
        );

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // See ADR-1534.
    private static void AddRangeFailure<T>(
        List<string> failures,
        string propertyName,
        T value,
        T minValue,
        T maxValue
    )
        where T : IComparable<T>
    {
        if (value.CompareTo(minValue) >= 0 && value.CompareTo(maxValue) <= 0)
            return;

        failures.Add(
            $"{nameof(AuditRecordOptions)}.{propertyName} ('{AuditRecordOptions.SectionName}:{propertyName}') must be between {minValue} and {maxValue}; it is {value}."
        );
    }
}
