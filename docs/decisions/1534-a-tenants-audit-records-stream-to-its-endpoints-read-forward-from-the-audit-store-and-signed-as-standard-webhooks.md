# 1534. A tenant's audit records stream to its endpoints, read forward from the audit store and signed as Standard Webhooks

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

The management API's paged lookup is a customer's export
([ADR-1527](1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md)),
while a SIEM wants records pushed as they happen, as Auth0's log streams push them. A record is the CloudTrail-shaped
JSON of [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md) that the observation
seam of
[ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md)
publishes.

Every replica writes records to the audit store in batches
([ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md)), so a reader that
follows the store forward needs no durable job dispatcher ([#25](https://github.com/NCodeGroup/identity/issues/25)).
Replicas commit their batches independently, so a reader must not pass a point that a slower commit can still fill.

A call to a customer's URL goes through the guarded client
([ADR-1315](1315-calls-to-caller-supplied-urls-share-one-guarded-bounded-client-and-one-single-flight-helper.md)). A
secret the server reads back is protected under its owner's master key
([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)) and
rewrapped when the key rotates
([ADR-1407](1407-a-master-key-rotates-its-data-keys-are-rewrapped-and-it-is-destroyed-once-unnamed.md)). A management
route authorizes before it reads
([ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)).

## Decision

**A tenant's administrator registers audit streams through the management API, and every replica claims the streams that
are due under a lease in the database, reads each one's records forward from the audit store after its saved position,
posts them in batches to the stream's HTTPS endpoint signed as Standard Webhooks, and saves the position it reached only
once the endpoint accepts them.**

1. **The resource.** `api/tenants/{tenantId}/audit-streams` lists, creates, reads, updates (display name, endpoint,
   enabled) and deletes streams, and `POST …/{streamId}/signing-secrets` rotates a stream's secret. Each route
   authorizes at the tenant's scope with the read, list, create, update or delete operation, and each change is audited
   as an `audit_stream` change. A tenant has at most `max_audit_streams` streams: a tenant limit, 0 to 10, 2 by default,
   merged with `Min`, which a tier writes
   ([ADR-1418](1418-limits-are-set-by-the-authority-above-their-scope-and-readers-learn-which-they-may-change.md)). A
   tenant's creates are serialized through an atomic add-if-absent in shared state, held for at most 30 seconds, so the
   limit holds when creates race and a create that meets another running answers `409`.
2. **The endpoint.** It is an absolute `https` URL of at most 1,000 characters without user information or a fragment,
   called only through the guarded client: no private address, no redirect, bounded time.
3. **The position.** The store stamps each record with the time it stores it (`StoredTime`). A stream reads its tenant's
   records ordered by stored time and event identifier after its position and stored before the current time less
   `OpenId:AuditRecords:StreamSettleDelay` (one minute), so a batch that another replica is still committing is read on
   a later pass. A stream starts at its creation. The position is saved only after a `2xx`, so a replica that stops
   sends the last batch again and a receiver deduplicates by `eventId`. The settle delay assumes that the replicas'
   clocks agree within it.
4. **Batches.** A batch holds up to `StreamBatchSize` (100) records, and a replica keeps sending full batches while half
   its lease remains. The request is `POST` with `Content-Type: application/json` and the body
   `{"type":"audit_records","timestamp":…,"data":{"tenantId":…,"records":[…]}}`, each record in the lookup's item shape.
5. **The signature.** It follows Standard Webhooks: `webhook-id` is the first record's `eventId`, `webhook-timestamp` is
   the Unix seconds of the send, and `webhook-signature` is one `v1,` + base64 HMAC-SHA256 over
   `{id}.{timestamp}.{body}` per active secret, separated by spaces. A secret is 32 random bytes shown once, as `whsec_`
   + base64, when the stream is created and when its secret rotates. A rotated secret keeps signing beside its successor
   for 24 hours. Each secret is stored protected under the tenant's master key and rewrapped with it.
6. **Failures.** Any answer but `2xx`, a timeout or a refused connection keeps the position, and so does a failure of
   any other kind: a signing secret that cannot be unprotected is recorded as `signing_secret_unavailable`, and any
   other failure of the delivery as `delivery_error`. The next attempt waits `StreamRetryDelay` × 2^(failures − 1), at
   most `StreamMaxRetryDelay` (10 seconds, 1 hour). The resource reports the last delivery, the last failure and its
   code, the consecutive failures and the next attempt. A stream that falls behind by more than the retention loses what
   the prune removes. A failure is that stream's alone: it is logged with the stream and the tenant, and the next due
   stream is still delivered.
7. **The lease.** A stream's state row holds its lease holder and expiry (`StreamLeaseDuration`, 2 minutes), claimed
   with optimistic concurrency, so one replica sends a stream at a time and its order holds. A lease whose replica stops
   expires, and a replica that stops releases the leases it holds, on a token of its own bounded by 10 seconds. A poll
   reads only the streams whose tenant has a record stored after their position and before the settle cutoff, so an
   idle stream costs no lease write, no key unwrap and no poll slot.
8. **Telemetry.** As
   [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md)
   requires: `ncode.identity.audit_stream.deliveries` by `ncode.identity.audit_stream.result` (`delivered`, `failed`)
   and `ncode.identity.audit_stream.records`. A log names the tenant and the stream, never a response body.
9. **Every record.** A stream carries every record of its tenant, as the lookup does; the receiver filters.

## Options considered

- **A dispatcher job per record.** Rejected: durability waits on [#25](https://github.com/NCodeGroup/identity/issues/25)
  and order is lost.
- **A static bearer token the receiver checks, as Auth0's custom webhook sends.** Rejected: it proves nothing about the
  body, while an HMAC signs the body and the time.
- **A detached JWS under the tenant's signing key.** Rejected: every receiver would fetch and cache the tenant's key
  set, while Standard Webhooks has a verifier in every language.
- **A database sequence as the position.** Rejected: a sequence value is taken at insert, not at commit, so it has the
  same gap, and it differs between providers.
- **Push delivery of security event tokens, RFC 8935.** Rejected: one event per request, built for security events, not
  an audit trail.
- **Forward reads with a saved position and Standard Webhooks signatures.** Chosen.

## Consequences

- A customer keeps a history longer than the retention by streaming it.
- The audit store gains the stored time and an index on tenant, stored time and event identifier.
- Each poll reads the due streams' state rows.
- The hosted platform's tiers write `max_audit_streams`.

## References

- [ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md) —
  the observation seam
- [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md) —
  telemetry
- [ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md) —
  the protection of a stored secret
- [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md) — the audit record
- [ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md) — the audit store
- [ADR-1315](1315-calls-to-caller-supplied-urls-share-one-guarded-bounded-client-and-one-single-flight-helper.md) — the
  guarded client
- [ADR-1407](1407-a-master-key-rotates-its-data-keys-are-rewrapped-and-it-is-destroyed-once-unnamed.md) — the rewrap of
  a master key's data
- [ADR-1418](1418-limits-are-set-by-the-authority-above-their-scope-and-readers-learn-which-they-may-change.md) — limits
  and their tiers
- [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)
  — management route authorization
- [ADR-1527](1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md)
  — audit retention and export
- [Standard Webhooks specification](https://github.com/standard-webhooks/standard-webhooks/blob/main/spec/standard-webhooks.md)
  — the signature
- RFC 8935 — push-based security event token delivery
- Issue [#25](https://github.com/NCodeGroup/identity/issues/25) — the durable job dispatcher
- Issue [#910](https://github.com/NCodeGroup/identity/issues/910) — audit retention and export
