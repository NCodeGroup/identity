// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using MailKit.Net.Smtp;

namespace NCode.Identity.Mail.Smtp.Facades;

/// <summary>
/// Provides the default implementation of <see cref="ISmtpClientFactory"/>, which creates a MailKit
/// <see cref="SmtpClient"/>.
/// </summary>
// See ADR-1532.
[ExcludeFromCodeCoverage(Justification = "A passthrough to MailKit.")]
internal sealed class DefaultSmtpClientFactory : ISmtpClientFactory
{
    /// <inheritdoc />
    public ISmtpClient Create() => new SmtpClient();
}
