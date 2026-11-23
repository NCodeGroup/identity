// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Mail.Mime.Tests;

internal sealed class FakeMailTransport(IMimeMessageFactory mimeMessageFactory) : IMailTransport
{
    public IMimeMessageFactory MimeMessageFactory { get; } = mimeMessageFactory;

    public ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
