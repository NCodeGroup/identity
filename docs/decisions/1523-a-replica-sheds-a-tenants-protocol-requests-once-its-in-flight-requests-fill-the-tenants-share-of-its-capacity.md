# 1523. A replica sheds a tenant's protocol requests once its in-flight requests fill the tenant's share of its capacity

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

A shared cell serves many tenants from the same replicas. A tenant's rate limits and quotas
([ADR-1246](1246-rate-limits-are-registered-descriptors-counted-in-a-shared-expiring-counter-set.md),
[ADR-1198](1198-a-daily-quota-counts-a-utc-day-and-a-tier-may-admit-a-bounded-billed-overage.md)) are ceilings, not
reservations: they cap what one tenant may send, and promise no tenant a place when the replica is busy (hosting
analysis §12).

A burst of free usage that stays within every free limit can still fill a replica's threads, its database pool and its
password-hashing gate, so the requests of paid tenants wait behind it. In-flight requests climb when a replica
saturates, and a replica sees that count without asking anyone.

A plan tier is a set of limits the platform writes on a team's document
([ADR-1198](1198-a-daily-quota-counts-a-utc-day-and-a-tier-may-admit-a-bounded-billed-overage.md) decision 7,
[ADR-1418](1418-limits-are-set-by-the-authority-above-their-scope-and-readers-learn-which-they-may-change.md)), so the
share of a replica that a tier may fill is one more limit. A self-hoster configures nothing and keeps every request.

## Decision

**Each replica counts the metered protocol requests it is serving and refuses a tenant's request with `503` once that
count reaches the tenant's share of the replica's concurrency limit, so a tier with a smaller share is shed first.**

1. **The host sets the replica's limit.** `OpenId:Admission:ConcurrencyLimit` (`AdmissionOptions`) is the most metered
   protocol requests one replica serves at once. It is unset by default, which sheds nothing, and at least 1, validated
   at start
   ([ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md)).
   The chart's `admission.concurrencyLimit` sets it.
2. **The tenant setting sets the share.** `admission_capacity_percent` is a tenant setting: an integer from 1 to 100, a
   limit merged with `Min`, with no default, where unset means 100, and not discoverable. The platform writes it on a
   team's document as part of a tier, and a team administrator can only lower it for a tenant.
3. **The rule.** A request is admitted while the replica's in-flight count, including the request, is at most
   `max(1, floor(limit × percent / 100))`. With a limit of 100, a tenant at 60 is refused once 60 requests are in
   flight, and tenants at 100 keep the other 40 places.
4. **The count is the replica's own.** The replaceable `IRequestAdmissionGate` service
   ([ADR-1001](1001-behavior-is-a-service-a-strategy-collection-or-a-mediator-seam-chosen-by-one-ladder.md)) holds it,
   and `DefaultRequestAdmissionGate` counts with `Interlocked`, as the secret verification gate bounds its own process
   ([ADR-1391](1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md)). Nothing
   goes through Valkey, because a shed decision must cost nothing on an overloaded replica.
5. **Only metered requests count.** The discovery document and the JSON Web Key Set (`IOpenIdEndpointMeteredMetadata`
   false) are never counted or shed, because they are cheap and every relying party's token validation needs them.
6. **Where it runs.** `AdmissionEndpointFilter` runs inside the environment filter, which resolves the tenant, and
   outside the rate-limit filter, so a shed request spends no rate-limit count, no quota and no counter-store round
   trip, and is neither handled, audited nor recorded as usage.
7. **The answer.** A shed request is answered `503 Service Unavailable`
   ([RFC 9110 §15.6.4](https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4)) with the `temporarily_unavailable` error
   code, `Retry-After` in whole seconds from `OpenId:Admission:RetryAfter` (one second by default, from one second to
   one hour) and `Cache-Control: no-store`.
8. **The metric.** `ncode.identity.admission.shed` counts shed requests by `ncode.identity.tenant.environment_type`
   ([ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md)).
9. **The hosted platform's tiers.** The free tier carries `admission_capacity_percent: 60`; paid tiers set none. Each
   cell's concurrency limit comes from the cell's load test.

## Options considered

- **Queuing lower tiers.** Rejected: a queue adds latency and memory exactly when the replica is overloaded, while
  shedding fails fast and the client retries.
- **Named priority classes.** Rejected: a name cannot merge as a ceiling, so a team administrator could raise its own
  tenant's class; a percentage merges with `Min`.
- **An adaptive signal such as CPU or latency.** Rejected as the default: CPU steal on shared vCPUs makes it noisy. The
  gate is a service, so an adaptive gate can be registered.
- **A cluster-wide count in Valkey.** Rejected: it costs a round trip per request on an overloaded replica, while
  capacity is per replica.
- **A reservation per tenant.** Rejected: guaranteed capacity is what a dedicated cell sells (hosting analysis §12).
- **`429 Too Many Requests`.** Rejected: the server is overloaded, and the caller exceeded nothing of its own.
- **Chosen:** the per-replica share.

## Consequences

- Under a free burst, paid tenants keep 40% of every replica's places.
- A limit set too low refuses requests a replica could serve, which the shed counter shows; a host without a limit
  sheds nothing.
- The platform's alerting watches the counter.

## References

- [ADR-1001](1001-behavior-is-a-service-a-strategy-collection-or-a-mediator-seam-chosen-by-one-ladder.md) — behavior is
  a service
- [ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md)
  — options are validated at start
- [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md) —
  telemetry attributes
- [ADR-1198](1198-a-daily-quota-counts-a-utc-day-and-a-tier-may-admit-a-bounded-billed-overage.md) — quotas and a plan
  tier's limits
- [ADR-1246](1246-rate-limits-are-registered-descriptors-counted-in-a-shared-expiring-counter-set.md) — rate limits
- [ADR-1391](1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md) — the secret
  verification gate bounds its own process
- [ADR-1418](1418-limits-are-set-by-the-authority-above-their-scope-and-readers-learn-which-they-may-change.md) — who
  writes a limit
- [Hosting analysis §12](../hosting-analysis.md#12-shared-and-dedicated-cells) — shared and dedicated cells
- [#906](https://github.com/NCodeGroup/identity/issues/906)
- [RFC 9110 §15.6.4](https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4) — `503 Service Unavailable`
