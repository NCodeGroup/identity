// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Dispatch;

namespace NCode.Identity.Mail;

/// <summary>
/// The dispatch job that delivers one mail message through the registered <see cref="IMailTransport"/>.
/// </summary>
/// <param name="Message">The message to deliver.</param>
// See ADR-1532.
internal sealed record MailDeliveryJob(MailMessage Message) : IDispatchJob;
