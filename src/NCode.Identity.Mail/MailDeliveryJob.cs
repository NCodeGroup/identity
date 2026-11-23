// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Dispatch;

namespace NCode.Identity.Mail;

/// <summary>
/// The dispatch job that composes one mail message from its model at each delivery attempt and delivers it through the
/// registered <see cref="IMailTransport"/>. The job holds the model as <see cref="object"/>, so a dispatcher that
/// persists the job stores the model's type with it and reads the model back only as a type that a registered
/// <see cref="IMailComposer"/> names.
/// </summary>
/// <param name="Model">The serializable model that the registered <see cref="IMailComposer"/> of its type
/// composes.</param>
// See ADR-1532.
internal sealed record MailDeliveryJob(object Model) : IDispatchJob;
