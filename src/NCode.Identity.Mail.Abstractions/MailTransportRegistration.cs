// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Marks that a mail transport is registered, so that a host registers only one.
/// </summary>
// See ADR-1532.
[PublicAPI]
public sealed class MailTransportRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MailTransportRegistration"/> class.
    /// </summary>
    /// <param name="name">The name of the transport, such as the name of its configuration section.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
    public MailTransportRegistration(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
    }

    /// <summary>
    /// Gets the name of the transport.
    /// </summary>
    public string Name { get; }
}
