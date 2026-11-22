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

        if (
            options.RetryAfter < TimeSpan.FromSeconds(1)
            || options.RetryAfter > TimeSpan.FromHours(1)
        )
        {
            failures.Add(
                $"{nameof(AdmissionOptions)}.{nameof(AdmissionOptions.RetryAfter)} ('{AdmissionOptions.SectionName}:{nameof(AdmissionOptions.RetryAfter)}') must be between 00:00:01 and 01:00:00; it is {options.RetryAfter}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
