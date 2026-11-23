// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore.Options;
using NCode.Identity.Mail.Mime;

namespace NCode.Identity.Mail.Smtp.Options;

/// <summary>
/// Validates <see cref="SmtpMailTransportOptions"/> when the host starts.
/// </summary>
// See ADR-1532.
internal sealed class SmtpMailTransportOptionsValidator(
    IOptions<LocalDevelopmentOptions> localDevelopmentOptions
) : IValidateOptions<SmtpMailTransportOptions>
{
    private const int MinPort = 1;

    private const int MaxPort = 65535;

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

        if (options.Port is < MinPort or > MaxPort)
        {
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Port)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Port)}') must be between {MinPort} and {MaxPort}; it is {options.Port}."
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
        else if (options.Tls == SmtpTls.None && !localDevelopmentOptions.Value.Enabled)
        {
            // Mail carries one-time codes, so it is never submitted in clear text outside a host configured for local
            // development; judging the bound value refuses every spelling that binds to None (ADR-1532, ADR-1370).
            failures.Add(
                $"{nameof(SmtpMailTransportOptions)}.{nameof(SmtpMailTransportOptions.Tls)} ('{SmtpMailTransportOptions.SectionName}:{nameof(SmtpMailTransportOptions.Tls)}') is {nameof(SmtpTls.None)}, which sends mail and the codes it carries in clear text; only a host with {LocalDevelopmentOptions.DefaultSectionName}:{nameof(LocalDevelopmentOptions.Enabled)} may use it."
            );
        }

        if (!MimeMailAddresses.IsMailAddress(options.From))
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
