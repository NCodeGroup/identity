# 1532. Outbound mail is a port that queues each message for a replaceable transport, and SMTP is its adapter

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

The product sends no mail, while an end user's email verification, password reset and email second factor need it.
Forces:

- **A host's mail provider varies.** The hosted platform sends through Amazon SES
  ([ADR-1528](1528-the-hosted-platform-sends-its-mail-through-amazon-ses-in-each-cells-region-from-mail-ncodeid-net.md)),
  and a self-hoster uses a relay of their own. Every provider accepts message submission
  ([RFC 6409](https://www.rfc-editor.org/rfc/rfc6409)) over TLS, either implicit
  ([RFC 8314](https://www.rfc-editor.org/rfc/rfc8314)) or upgraded with STARTTLS
  ([RFC 3207](https://www.rfc-editor.org/rfc/rfc3207)).
- **A send made inside a request would slow or fail the request.** Its timing would also tell a caller whether an
  account exists.
- **A code is useless once it expires,** so a retry is short, and a message that holds a code is personal data, so no
  log writes a recipient or a body.
- **Deferred work runs through the replaceable job dispatcher**
  ([ADR-1242](1242-deferred-work-runs-through-a-replaceable-job-dispatch-port-with-a-concurrent-in-process-default.md)).

## Decision

**A producer hands a message to `IMailSender`, which queues its delivery through the job dispatcher; the registered
`IMailTransport` delivers it; and `NCode.Identity.Mail.Smtp` is the SMTP transport a host opts into with the
`Mail:Smtp` section.**

1. **Packages.** Each package names one capability as an abstractions and implementation pair
   ([ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md)).
   `NCode.Identity.Mail.Abstractions` holds `MailLibrary`, `MailMessage`, `IMailSender` and `IMailTransport`.
   `NCode.Identity.Mail` holds `AddMailServices()`, the default sender, the delivery job and its handler, the metrics,
   and a default transport that refuses every message until a host registers one. `NCode.Identity.Mail.Smtp` holds
   `AddSmtpMailTransport` over MailKit, an opt-in adapter as the Valkey adapter is
   ([ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md)).
2. **The message.** A message has one recipient, a subject, a plain-text body, an optional HTML body and an optional
   sender display name. The sender address is the transport's, because a provider authorizes the sending identity.
   Every message carries `Auto-Submitted: auto-generated`
   ([RFC 3834](https://www.rfc-editor.org/rfc/rfc3834)).
3. **Queueing.** `SendAsync` returns once the dispatcher accepts the job. A delivery is tried four times, backing off
   from five seconds to one minute, so a code still arrives within its lifetime. The in-process dispatcher holds the
   job in memory, so a replica that stops loses the messages it holds and the end user asks for another code. A durable
   dispatcher keeps the message, code included, until it is delivered
   ([#25](https://github.com/NCodeGroup/identity/issues/25)).
4. **SMTP.** `Mail:Smtp` holds `Host`, `Port` (587), `Tls` (`StartTls` by default, `ImplicitTls` or `None`), `UserName`
   and `Password` (both or neither), `From`, `FromName` and `Timeout` (30 seconds). It is bound strictly and validated
   at start
   ([ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md)).
   `StartTls` refuses a server that offers no STARTTLS. `None` sends in clear, so the SMTP options validator refuses it,
   in any spelling that binds to it, unless `LocalDevelopment:Enabled` is set, for the local stack's mail catcher
   ([ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)).
   Without the section nothing is registered, and every delivery fails and is logged. Each message opens one
   connection and closes it.
5. **Telemetry.** `ncode.identity.mail.messages` counts messages by `ncode.identity.mail.result` (`delivered`,
   `failed`) on the meter `NCode.Identity.Mail`
   ([ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md)).
   A failed delivery logs its attempt and the exception, never the recipient or the body. The SMTP adapter throws the
   server's numeric status and never its reply text, which can echo the recipient.
6. **The hosts.** The host chart's `mail.smtp` values set the section, with its credentials from a Secret, and refuse
   `None`. The local stack sends to Mailpit.

## Options considered

- **A provider's HTTP API, such as SES's `SendEmail`.** Rejected as the default: it ties the host to one vendor, and
  the transport seam takes one as a package of its own.
- **`System.Net.Mail.SmtpClient`.** Rejected: Microsoft does not recommend it for new development, and it lacks
  MailKit's TLS controls.
- **Sending inside the request.** Rejected: a slow provider stalls the page, and the timing tells whether an account
  exists.
- **A mail outbox table.** Rejected: the durable dispatcher is the one durable queue for every job
  ([#25](https://github.com/NCodeGroup/identity/issues/25)).
- **Chosen:** the queued port and the SMTP adapter.

## Consequences

- A host without `Mail:Smtp` sends no mail and logs each failed delivery, so its tenants keep the email-code flows off.
- The hosted platform points `Mail:Smtp` at SES's SMTP endpoint in the cell's region with the send-only user
  ([ADR-1528](1528-the-hosted-platform-sends-its-mail-through-amazon-ses-in-each-cells-region-from-mail-ncodeid-net.md)).
- A durable dispatcher stores a code in its queue until the code is delivered, so it protects job payloads at rest
  ([#25](https://github.com/NCodeGroup/identity/issues/25)).

## References

- [ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md) —
  options are validated at start
- [ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md) —
  ports with in-process and Valkey adapters
- [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md) —
  telemetry attributes
- [ADR-1242](1242-deferred-work-runs-through-a-replaceable-job-dispatch-port-with-a-concurrent-in-process-default.md) —
  the job dispatcher
- [ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md) —
  the deployment posture and local development
- [ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md) —
  package pairs
- [ADR-1528](1528-the-hosted-platform-sends-its-mail-through-amazon-ses-in-each-cells-region-from-mail-ncodeid-net.md) —
  the hosted platform's mail provider
- [RFC 3207](https://www.rfc-editor.org/rfc/rfc3207) — SMTP STARTTLS
- [RFC 3834](https://www.rfc-editor.org/rfc/rfc3834) — `Auto-Submitted`
- [RFC 6409](https://www.rfc-editor.org/rfc/rfc6409) — message submission
- [RFC 8314](https://www.rfc-editor.org/rfc/rfc8314) — implicit TLS for submission
- [#25](https://github.com/NCodeGroup/identity/issues/25)
- [#911](https://github.com/NCodeGroup/identity/issues/911)
