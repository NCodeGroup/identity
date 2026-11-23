// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// Holds what a test reads from a mail that <see cref="CapturingMailTransport"/> delivered: the message itself is
/// disposed once it is delivered, so the transport copies these fields while it still can.
/// </summary>
/// <param name="To">The addresses the mail was sent to, joined with a comma.</param>
/// <param name="Subject">The subject of the mail.</param>
/// <param name="TextBody">The plain-text body of the mail.</param>
// See ADR-1533.
public sealed record CapturedMail(string To, string Subject, string TextBody);
