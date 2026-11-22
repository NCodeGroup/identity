// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using MimeKit;

namespace NCode.Identity.Mail.Smtp.Options;

/// <summary>
/// Validates <see cref="SmtpMailTransportOptions"/> when the host starts.
/// </summary>
// See ADR-1532.
internal sealed class SmtpMailTransportOptionsValidator : IValidateOptions<SmtpMailTransportOptions>
{
    private static TimeSpan MinTimeout { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan MaxTimeout { get; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmtpMailTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Host)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Host)}') must name the SMTP server; it is empty."
            );
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Port)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Port)}') must be between 1 and 65535; it is {options.Port}."
            );
        }

        if (!Enum.IsDefined(options.Tls))
        {
            var names = Enum.GetNames<SmtpTls>();
            var definedValues = string.Join(", ", names);
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Tls)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Tls)}') must be one of {definedValues}; it is {(int)options.Tls}."
            );
        }

        if (
            !MailboxAddress.TryParse(options.From, out var address)
            || !address.Address.Contains('@', StringComparison.Ordinal)
        )
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.From)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.From)}') must be one mail address; it is '{options.From}'."
            );
        }

        if (string.IsNullOrEmpty(options.UserName) != string.IsNullOrEmpty(options.Password))
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.UserName)} and {nameof(SmtpMailTransportOptions.Password)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.UserName)}' and '{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Password)}') must be set together or not at all."
            );
        }

        if (options.Timeout < MinTimeout || options.Timeout > MaxTimeout)
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Timeout)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Timeout)}') must be between {MinTimeout} and {MaxTimeout}; it is {options.Timeout}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
