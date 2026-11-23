// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NCode.Identity.Mail.Mime;

namespace NCode.Identity.Mail.AmazonSes.Options;

/// <summary>
/// Validates <see cref="AmazonSesMailTransportOptions"/> when the host starts.
/// </summary>
// See ADR-1528.
internal sealed partial class AmazonSesMailTransportOptionsValidator
    : IValidateOptions<AmazonSesMailTransportOptions>
{
    private const int MaxTags = 50;

    private static TimeSpan MinTimeout { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan MaxTimeout { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the pattern of an AWS region name, such as <c>us-east-1</c>: two lowercase letters, one or more hyphenated
    /// lowercase words and a number.
    /// </summary>
    /// <returns>The <see cref="Regex"/> that matches a whole region name.</returns>
    // '\z' rather than '$', which also matches before a trailing newline, and ASCII classes rather than '\d', which
    // accepts any Unicode digit.
    [GeneratedRegex(@"^[a-z]{2}(-[a-z]+)+-[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();

    /// <summary>
    /// Gets the pattern of the ARN of an Amazon SES identity: the <c>arn</c> prefix with an AWS partition, the
    /// <c>ses</c> service, a region, a twelve-digit account number and an <c>identity/</c> resource that has no
    /// whitespace.
    /// </summary>
    /// <returns>The <see cref="Regex"/> that matches a whole identity ARN.</returns>
    [GeneratedRegex(
        @"^arn:aws[a-z-]*:ses:[a-z0-9-]+:[0-9]{12}:identity/\S+\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex IdentityArnPattern();

    /// <summary>
    /// Gets the pattern of the name of a configuration set: one to 64 letters, digits, underscores or hyphens.
    /// </summary>
    /// <returns>The <see cref="Regex"/> that matches a whole configuration set name.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ConfigurationSetNamePattern();

    /// <summary>
    /// Gets the pattern of the name or the value of a message tag: one to 256 letters, digits, underscores, periods, at
    /// signs or hyphens.
    /// </summary>
    /// <returns>The <see cref="Regex"/> that matches a whole tag name or tag value.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9_.@-]{1,256}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AmazonSesMailTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.Region is null || !RegionPattern().IsMatch(options.Region))
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.Region)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.Region)}') must name an AWS region such as 'us-east-1'; it is '{options.Region}'."
            );
        }

        if (!MimeMailAddresses.IsMailAddress(options.From))
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.From)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.From)}') must be one mail address; it is '{options.From}'."
            );
        }

        if (
            options.FromIdentityArn is not null
            && !IdentityArnPattern().IsMatch(options.FromIdentityArn)
        )
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.FromIdentityArn)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.FromIdentityArn)}') must be the ARN of an Amazon SES identity; it is '{options.FromIdentityArn}'."
            );
        }

        if (
            options.ConfigurationSetName is not null
            && !ConfigurationSetNamePattern().IsMatch(options.ConfigurationSetName)
        )
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.ConfigurationSetName)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.ConfigurationSetName)}') must be 1 to 64 letters, digits, underscores or hyphens; it is '{options.ConfigurationSetName}'."
            );
        }

        var tags = options.Tags;
        if (tags is { Count: > MaxTags })
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.Tags)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.Tags)}') must hold at most {MaxTags} tags; it holds {tags.Count}."
            );
        }

        if (tags is not null)
        {
            foreach (var (tagName, tagValue) in tags)
            {
                if (!TagPattern().IsMatch(tagName))
                {
                    failures.Add(
                        $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.Tags)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.Tags)}') must name each tag with 1 to 256 letters, digits, underscores, periods, at signs or hyphens; the name '{tagName}' does not."
                    );
                }

                if (tagValue is null || !TagPattern().IsMatch(tagValue))
                {
                    failures.Add(
                        $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.Tags)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.Tags)}:{tagName}') must be 1 to 256 letters, digits, underscores, periods, at signs or hyphens; it is '{tagValue}'."
                    );
                }
            }
        }

        if (options.Timeout < MinTimeout || options.Timeout > MaxTimeout)
        {
            failures.Add(
                $"{nameof(AmazonSesMailTransportOptions)}.{nameof(AmazonSesMailTransportOptions.Timeout)} ('{AmazonSesMailTransportOptions.SectionName}:{nameof(AmazonSesMailTransportOptions.Timeout)}') must be between {MinTimeout} and {MaxTimeout}; it is {options.Timeout}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
