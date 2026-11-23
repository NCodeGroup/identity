# 1532. Outbound mail is a port that composes each queued message at delivery for a replaceable transport, with SMTP and Amazon SES adapters

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
  ([ADR-1242](1242-deferred-work-runs-through-a-replaceable-job-dispatch-port-with-a-concurrent-in-process-default.md)),
  and a dispatch job is a serializable data contract.

## Decision

**A producer hands a serializable model to `IMailSender`, which queues it through the job dispatcher; at each delivery
attempt the `IMailComposer` registered for the model's type builds the `MailMessage` and the host's one `IMailTransport`
delivers it; and `NCode.Identity.Mail.Smtp` and `NCode.Identity.Mail.AmazonSes` are the transports a host opts into with
the `Mail:Smtp` and `Mail:AmazonSes` sections.**

1. **Packages.** Each package names one capability as an abstractions and implementation pair
   ([ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md)).
   `NCode.Identity.Mail.Abstractions` holds `MailLibrary`, `MailMessage`, `MailAddress`, `MailHeader`, `MailContent`,
   `StreamMailContent`, `MailAttachment`, `MailTransferEncodings`, `IMailSender`, `IMailComposer`,
   `MailComposer<TModel>`, `IMailTransport` and `MailTransportRegistration`. `NCode.Identity.Mail` holds
   `AddMailServices()`, the default sender, the composer registry, the delivery job and its handler, the metrics, and a
   default transport that refuses every message until a host registers one. `NCode.Identity.Mail.Mime` holds
   `IMimeMessageFactory`, which builds the MIME form of a message over MimeKit for every transport that submits MIME,
   its default, and `AddMimeMailTransport<TTransport>`, which registers such a transport as the host's one transport
   with the factory and the `TimeProvider` that dates its messages; each transport takes the factory by constructor
   injection. `NCode.Identity.Mail.Smtp` holds `AddSmtpMailTransport` over MailKit, and `NCode.Identity.Mail.AmazonSes`
   holds `AddAmazonSesMailTransport` over the AWS SDK's Amazon SES v2 client. Each transport is an opt-in adapter, as
   the Valkey adapter is
   ([ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md)).
2. **The message.** A `MailMessage` is a send-only request that owns its content, and whoever delivers it disposes it.
   Its addresses are `From`, `FromDisplayName`, `Sender`, `ReplyTo`, `To`, `Cc` and `Bcc`. Its headers are `Subject`,
   `Date`, `MessageId`, `InReplyTo`, `References`, `Priority` and `Importance`, which are closed enums fixed by
   [RFC 2156](https://www.rfc-editor.org/rfc/rfc2156), and custom `Headers`. Its content is `TextBody`, `HtmlBody`,
   `Attachments` and `LinkedResources`, each attachment holding a `Content`, a `FileName`, a `ContentId` and a
   `Description`. These members match MimeKit's without referencing MimeKit. A body is a `MailContent`, which works like
   `HttpContent`: a media type, a charset, an optional transfer encoding from the open `MailTransferEncodings`
   constants, a stream from `ReadAsStreamAsync`, and `LoadIntoBufferAsync`, which copies a non-seekable stream once into
   a sensitive pooled stream. `StreamMailContent` owns a stream. There are no byte arrays and no string bodies, and an
   address or a header refuses a control character. A message that names no `From` is sent from the transport's
   configured sender, because a provider authorizes the sending identity. Every message carries
   `Auto-Submitted: auto-generated` ([RFC 3834](https://www.rfc-editor.org/rfc/rfc3834)).
3. **Composition at delivery.** A stream can't cross the dispatcher, so a producer calls
   `IMailSender.SendAsync<TModel>(model)` with a serializable model, and the sender refuses a model type that no
   registered `IMailComposer` handles before it queues anything. The job carries the model. On each attempt the
   composer, a `MailComposer<TModel>` that `MailComposerRegistry` picks by model type, builds the message, the transport
   delivers it, and the handler disposes it. The email-code model carries the code as an `int`, so the code is never
   text before it is written into a sensitive buffer. `SendAsync` returns once the dispatcher accepts the job. A
   delivery is tried four times, backing off from five seconds to one minute, so a code still arrives within its
   lifetime. The in-process dispatcher holds the job in memory, so a replica that stops loses the messages it holds and
   the end user asks for another code.
4. **MIME.** A message's parts are `multipart/alternative` for the text and HTML bodies, `multipart/related` when the
   HTML has linked resources, and `multipart/mixed` when the message has attachments. Each content's transfer encoding
   is explicit: the one the content names, else quoted-printable for text and base64 for everything else. Content that
   can't seek is buffered once into a sensitive pooled stream, so each attempt and each transport reads the same bytes
   and no buffer outlives its message unzeroed. `Bcc` is hidden from the headers a transport writes, and every recipient
   is in the envelope. A message that names no `Date` is dated by the host's `TimeProvider`.
5. **SMTP.** `Mail:Smtp` holds `Host`, `Port` (587), `Tls` (`StartTls` by default, `ImplicitTls` or `None`), `UserName`
   and `Password` (both or neither), `From`, `FromName` and `Timeout` (30 seconds). `From` is one bare address, such as
   `noreply@example.com`, and the sender's display name comes from `FromName`, so the options validator refuses a `From`
   that holds a display name, angle brackets or a comment. The section is bound strictly and validated at start
   ([ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md)).
   `StartTls` refuses a server that offers no STARTTLS. `None` sends in clear, so the SMTP options validator refuses it,
   in any spelling that binds to it, unless `LocalDevelopment:Enabled` is set, for the local stack's mail catcher
   ([ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)).
   Each message opens one connection and closes it.
6. **Amazon SES.** `Mail:AmazonSes` holds `Region`, `From`, `FromName`, `FromIdentityArn`, `ConfigurationSetName`,
   `Tags` and `Timeout` (30 seconds). Its `From` is one bare address as well, and its options validator refuses any
   other. The transport writes the raw MIME once into a sensitive pooled stream, and the SDK reads a read-only
   `MemoryStream` over that rented array. The credentials come from the default AWS chain, and the SDK retries twice
   under its standard policy. A refusal throws SES's error code and HTTP status, never the message text.
7. **One transport per host.** Each adapter registers only when its section exists, through `AddMimeMailTransport`, the
   one place that adds the `MailTransportRegistration` marker, so a second adapter throws `InvalidOperationException`
   while the host is composed. Without either section nothing is registered, and every delivery fails and is logged.
8. **Telemetry.** `ncode.identity.mail.messages` counts messages by `ncode.identity.mail.result` (`delivered`, `failed`)
   on the meter `NCode.Identity.Mail`
   ([ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md)). A
   failed composition counts and logs as a failed delivery. A failed delivery logs its attempt and the exception, never
   the recipient or the body. The SMTP adapter throws the server's numeric status and never its reply text, which can
   echo the recipient. It treats a message as delivered once the server accepts it, so a connection that fails to close
   afterwards is logged by its exception type and the delivery is neither failed nor repeated.
9. **The hosts.** The host chart's `mail.smtp` values set the `Mail:Smtp` section, with its credentials from a Secret,
   and refuse `None`; its `mail.amazonSes` values set the `Mail:AmazonSes` section; and it refuses both set together.
   `aws.webIdentity` gives the AWS role that AWS KMS and Amazon SES use to the pods that use AWS KMS and to the host's
   Deployment, the one pod that sends mail; without a role, the transport signs in with the pod's ambient identity,
   such as EKS Pod Identity. The local stack sends to Mailpit.

## Options considered

- **Queueing the composed message.** Rejected: a stream can't be serialized, and a dropped job would leave pooled
  buffers that are never zeroed.
- **MimeKit types in the abstractions.** Rejected: they couple every consumer of the port to MimeKit.
- **SES's simple content.** Rejected: its bodies are strings, and it drops `Sender`, `Date` and `Message-Id`, which the
  raw message keeps.
- **SES's SMTP interface.** Rejected: it needs a long-lived credential where the pod already has an AWS role.
- **`System.Net.Mail.SmtpClient`.** Rejected: Microsoft does not recommend it for new development, and it lacks
  MailKit's TLS controls.
- **Sending inside the request.** Rejected: a slow provider stalls the page, and the timing tells whether an account
  exists.
- **A mail outbox table.** Rejected: the durable dispatcher is the one durable queue for every job
  ([#25](https://github.com/NCodeGroup/identity/issues/25)).
- **A static MIME builder.** Rejected: a transport can't take it by injection, a host can't replace it, and it reads
  the wall clock for a message's date.
- **A delivery job typed by its model.** Rejected: the dispatcher finds a job's handlers by the job's type, so each
  model would need a handler registered for it, which a capability that references only
  `NCode.Identity.Mail.Abstractions` can't register without the implementation reference that
  [ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md)
  rules out.
- **Chosen:** the queued port, composition at delivery, and the SMTP and Amazon SES adapters over a shared MIME package.

## Consequences

- A host without a `Mail` section sends no mail and logs each failed delivery, so its tenants keep the email-code flows
  off.
- The hosted platform sends through the Amazon SES transport in the cell's region as the cell's role, so it stores no
  mail credential
  ([ADR-1528](1528-the-hosted-platform-sends-its-mail-through-amazon-ses-in-each-cells-region-from-mail-ncodeid-net.md)).
- A producer adds a serializable model and a composer for it, and a new transport that submits MIME references
  `NCode.Identity.Mail.Mime`, registers through `AddMimeMailTransport` and takes `IMimeMessageFactory` by constructor
  injection.
- A durable dispatcher stores the model, code included, until the message is delivered, so it protects job payloads at
  rest ([#25](https://github.com/NCodeGroup/identity/issues/25)). The job holds its model as `object`, so such a
  dispatcher stores the model's type with the job and reads the model back only as a type that a registered
  `IMailComposer` names as its `ModelType`; read back by the job's declared member types alone, the model would reach
  the handler as data that no composer handles.

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
  package pairs and the layered references
- [ADR-1528](1528-the-hosted-platform-sends-its-mail-through-amazon-ses-in-each-cells-region-from-mail-ncodeid-net.md) —
  the hosted platform's mail provider
- [RFC 2156](https://www.rfc-editor.org/rfc/rfc2156) — `Priority` and `Importance`
- [RFC 3207](https://www.rfc-editor.org/rfc/rfc3207) — SMTP STARTTLS
- [RFC 3834](https://www.rfc-editor.org/rfc/rfc3834) — `Auto-Submitted`
- [RFC 6409](https://www.rfc-editor.org/rfc/rfc6409) — message submission
- [RFC 8314](https://www.rfc-editor.org/rfc/rfc8314) — implicit TLS for submission
- [#25](https://github.com/NCodeGroup/identity/issues/25)
- [#911](https://github.com/NCodeGroup/identity/issues/911)
