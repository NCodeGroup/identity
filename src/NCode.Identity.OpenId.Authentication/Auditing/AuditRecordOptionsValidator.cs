// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Authentication.OutboundHttp;
using NCode.Identity.Time;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// Validates <see cref="AuditRecordOptions"/> when the host starts.
/// </summary>
// See ADR-1303 and ADR-1137.
internal sealed class AuditRecordOptionsValidator(IOptions<OutboundHttpOptions> outboundHttpOptions)
    : IValidateOptions<AuditRecordOptions>
{
    private const int MaxStreamBatchSize = 1000;

    private const int MaxStreamsPerPoll = 1000;

    private IOptions<OutboundHttpOptions> OutboundHttp { get; } = outboundHttpOptions;

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
            MaxStreamBatchSize
        );

        AddRangeFailure(
            failures,
            nameof(AuditRecordOptions.StreamLeaseDuration),
            options.StreamLeaseDuration,
            ThirtySeconds,
            OneHour
        );

        var attemptTimeout = OutboundHttp.Value.AttemptTimeout;
        if (options.StreamLeaseDuration <= attemptTimeout * 2)
        {
            failures.Add(
                $"{nameof(AuditRecordOptions)}.{nameof(AuditRecordOptions.StreamLeaseDuration)} ('{AuditRecordOptions.SectionName}:{nameof(AuditRecordOptions.StreamLeaseDuration)}') must be more than twice {nameof(OutboundHttpOptions)}.{nameof(OutboundHttpOptions.AttemptTimeout)} ('{OutboundHttpOptions.SectionName}:{nameof(OutboundHttpOptions.AttemptTimeout)}', {attemptTimeout}), so a batch sent before half the lease is spent ends while the lease is held; it is {options.StreamLeaseDuration}."
            );
        }

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
            MaxStreamsPerPoll
        );

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Adds a failure to the list when a value lies outside an inclusive range.
    /// </summary>
    /// <typeparam name="T">The type of the value, which must be comparable with itself.</typeparam>
    /// <param name="failures">The failures to add to.</param>
    /// <param name="propertyName">The name of the option that holds the value.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="minValue">The least value that is accepted.</param>
    /// <param name="maxValue">The greatest value that is accepted.</param>
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
