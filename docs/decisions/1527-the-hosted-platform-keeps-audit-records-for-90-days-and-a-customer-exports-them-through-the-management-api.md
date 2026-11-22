# 1527. The hosted platform keeps audit records for 90 days, and a customer exports them through the management API

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

Audit records carry subject and client identifiers and source IP addresses, so they are personal data of a customer's
end users. The hosted platform must tell its customers how long it keeps them, and a customer must be able to keep its
own history. Forces:

- **Audit records are CloudTrail-shaped JSON with an indexable envelope**
  ([ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md),
  [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md)), and each carries an
  `eventId`.
- **Each cell's audit store keeps a record for `OpenId:AuditRecords:Retention`, 90 days by default,** and prunes it in
  batches ([ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md)).
- **The records also reach the cell's logs,** whose retention is the Grafana Cloud plan's.
- **The nightly encrypted dump is kept as 14 daily and 12 monthly copies under Object Lock in compliance mode**
  ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)).
- **`GET api/tenants/{tenantId}/audit-records` pages a tenant's records by time range, newest first**
  ([ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md)).
- **A cell's operational data stays in its region**
  ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).

## Decision

**The hosted platform keeps each cell's audit records for 90 days in its audit store, for the Grafana Cloud plan's log
retention in its logs, and for up to 13 months in its encrypted backups; a customer exports its tenants' records through
the management API.**

1. **One retention.** `OpenId:AuditRecords:Retention` stays at its 90-day default in every cell and for every plan. One
   retention is one statement in the DPA, and a customer that needs longer keeps its export.
2. **Logs.** Logs carrying records go to the Grafana Cloud stack in the cell's region and are kept 14 days on Free and
   30 days on Pro
   ([ADR-1525](1525-each-cluster-and-cell-keeps-its-metrics-within-a-series-budget-on-grafana-cloud-free-then-pro.md)).
   They serve the operator, never the customer.
3. **Backups.** A backup holds the store as it was that night. Object Lock keeps a copy for up to 13 months and nothing
   can shorten that, so the DPA states it. A restored store prunes what is past retention at its next prune.
4. **Export.** A client the customer authorizes for the tenant pages `GET api/tenants/{tenantId}/audit-records` with
   `startTime` and `endTime`, passing each page's `continuationToken` back as `cursor` until a page has none. It runs on
   a schedule shorter than 90 days and deduplicates by `eventId`. The console reads the same lookup.
5. **No erasure per data subject.** Records expire with the retention, because an audit trail a subject could erase
   stops being one. A deleted tenant's records stay until they expire
   ([ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md)).
6. **Streams.** A customer can also have its tenants' records pushed to its own endpoints as they are stored
   ([ADR-1534](1534-a-tenants-audit-records-stream-to-its-endpoints-read-forward-from-the-audit-store-and-signed-as-standard-webhooks.md));
   the lookup and the streams are the export.

## Options considered

- **A retention per plan.** Rejected: the store prunes per cell, a per-tenant window would make every prune a per-tenant
  query, and one stated retention is simpler.
- **30 days.** Rejected: shorter than many investigations; CloudTrail's event history is 90 days.
- **A year.** Rejected: it holds more personal data than the operator needs, and customers that want more keep their
  export.
- **Erasure per subject.** Rejected, as point 5 explains.
- **Chosen:** 90 days in the store for every plan, the logs' and the backups' retentions stated, and the management
  API's lookup and audit streams as the export.

## Consequences

- The DPA states 90 days, the log retention and the backups' 13 months.
- A customer that needs a longer or searchable history runs the export.
- A deleted tenant's records expire on schedule.

## References

- [ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md):
  the audit seam.
- [ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md): the
  encrypted backups.
- [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md): the record's shape.
- [ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md): the audit store,
  its retention and the lookup.
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md):
  the platform's regions.
- [ADR-1525](1525-each-cluster-and-cell-keeps-its-metrics-within-a-series-budget-on-grafana-cloud-free-then-pro.md): the
  log retention of the Grafana Cloud plans.
- [ADR-1534](1534-a-tenants-audit-records-stream-to-its-endpoints-read-forward-from-the-audit-store-and-signed-as-standard-webhooks.md):
  audit streams.
- [Audit records](../audit-records.md): how to read and operate them.
- Issues [#25](https://github.com/NCodeGroup/identity/issues/25) and
  [#910](https://github.com/NCodeGroup/identity/issues/910).
