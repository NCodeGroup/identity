// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Validates <see cref="AdmissionOptions"/> when the host starts.
/// </summary>
// See ADR-1523 and ADR-1137.
internal sealed class AdmissionOptionsValidator : IValidateOptions<AdmissionOptions>
{
    private static TimeSpan MinRetryAfter { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan MaxRetryAfter { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AdmissionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.ConcurrencyLimit < 1)
        {
            failures.Add(
                $"{nameof(AdmissionOptions)}.{nameof(AdmissionOptions.ConcurrencyLimit)} ('{AdmissionOptions.SectionName}:{nameof(AdmissionOptions.ConcurrencyLimit)}') must be at least 1 when it is set; it is {options.ConcurrencyLimit}."
            );
        }

        if (options.RetryAfter < MinRetryAfter || options.RetryAfter > MaxRetryAfter)
        {
            failures.Add(
                $"{nameof(AdmissionOptions)}.{nameof(AdmissionOptions.RetryAfter)} ('{AdmissionOptions.SectionName}:{nameof(AdmissionOptions.RetryAfter)}') must be between {MinRetryAfter} and {MaxRetryAfter}; it is {options.RetryAfter}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
