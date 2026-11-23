// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Marks that a mail transport is registered, so that a host registers only one. A name that is null or empty is
/// refused.
/// </summary>
/// <param name="name">
/// The name of the transport, such as the name of its configuration section, which must not be empty.
/// </param>
// See ADR-1532.
[PublicAPI]
public sealed class MailTransportRegistration(string name)
{
    /// <summary>
    /// Gets the name of the transport.
    /// </summary>
    public string Name { get; } =
        string.IsNullOrEmpty(name)
            ? throw new ArgumentException(
                "The name of the transport must not be empty.",
                nameof(name)
            )
            : name;
}
