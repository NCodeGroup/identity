// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Registration;

namespace NCode.Identity.Mail;

/// <summary>
/// The marker of the mail library's builder, which the mail transports require.
/// </summary>
// See ADR-1532.
[PublicAPI]
public readonly struct MailLibrary : IRegistrationMarker<MailLibrary>;
