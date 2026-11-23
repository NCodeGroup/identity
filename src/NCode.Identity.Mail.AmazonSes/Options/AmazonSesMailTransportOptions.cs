// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail.AmazonSes.Options;

/// <summary>
/// Contains the options for the Amazon SES mail transport, bound from the <see cref="SectionName"/> configuration
/// section.
/// </summary>
// See ADR-1528.
[PublicAPI]
public sealed class AmazonSesMailTransportOptions
{
    /// <summary>
    /// Gets the name of the configuration section the options are bound from.
    /// </summary>
    public const string SectionName = "Mail:AmazonSes";

    /// <summary>
    /// Gets or sets the AWS region, such as <c>us-east-1</c>, whose Amazon SES endpoint sends the mail.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address every message is sent from, which a verified Amazon SES identity authorizes.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the sender when a message names none, or <see langword="null"/> for none.
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Gets or sets the ARN of the Amazon SES identity that authorizes the sender when it belongs to another AWS
    /// account (sending authorization), or <see langword="null"/> to send under the caller's own identity.
    /// </summary>
    public string? FromIdentityArn { get; set; }

    /// <summary>
    /// Gets or sets the name of the Amazon SES configuration set that every message is sent with, or
    /// <see langword="null"/> for none.
    /// </summary>
    public string? ConfigurationSetName { get; set; }

    /// <summary>
    /// Gets or sets the tags, by name, that every message is sent with. An empty dictionary sends none.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets how long a call to Amazon SES waits before it fails. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
