# Incident response runbook

- **Status:** Living runbook — update it in the same change as anything it describes.
- **Audience:** the hosted platform's operator on call.
- **Scope:** how an incident is paged, triaged, contained, communicated and reviewed; the organizational security
  policy belongs to the SOC 2 work ([#844](https://github.com/NCodeGroup/identity/issues/844)).

This runbook names roles, never people or phone numbers.

## Severities

| Severity | Meaning | Acknowledge | Status page |
| -------- | ------- | ----------- | ----------- |
| SEV1 | A cell's protocol endpoints are unavailable or fail more than 5% of requests for 5 minutes, or personal data, keys or credentials are suspected or confirmed to be exposed | 15 minutes | Within 30 minutes, then hourly |
| SEV2 | A cell's p95 latency or error-rate alert is sustained for 15 minutes, one paid tenant is unavailable, or deploys and recovery are blocked by a control-plane outage | 30 minutes | Within 60 minutes when customers see it |
| SEV3 | No customer impact: the lab, one failed backup, a certificate within 14 days of expiry | Next business day | None |

## Paging

Grafana Cloud's alert rules (hosting analysis §10) route to Grafana IRM. SEV1 and SEV2 page the primary on-call through
the IRM app and SMS, and escalate to the second trusted administrator after 15 minutes unacknowledged. SEV3 opens a
GitHub issue instead of paging. The schedule and escalation chain are configured as code in
`NCodeGroup/identity-platform`.

## Triage

1. Declare the incident in Grafana IRM and name the incident commander: the person on call until they hand over.
2. Scope it: which cells (`cell.name`), which tenants, since when.
3. Check the vendors' status pages: Akamai, AWS (`us-east-1`, `us-west-2`), Cloudflare, Grafana Cloud.
4. Check what changed: Flux's last reconciliations and the last release in `identity-platform`.
5. Set the severity.

## Contain

### A bad release

Revert the release's commit in `identity-platform`. Flux rolls the previous release out, which runs against the current
schema because migrations only expand it
([ADR-1180](decisions/1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md)).

### A node or the database fails

Pod disruption budgets and spread keep the cell serving through a node, and the managed PostgreSQL fails over by itself.
For data loss, restore to a new database cluster by point-in-time recovery, or from the nightly encrypted dump in the
AWS backup bucket, and point the cell's database Secret at it through Parameter Store
([ADR-1526](decisions/1526-the-platforms-cluster-secrets-reach-each-cluster-from-aws-parameter-store-through-external-secrets-operator.md)).

### Overload

Read `ncode.identity.admission.shed` and `http.server.active_requests`. Free traffic is shed first
([ADR-1523](decisions/1523-a-replica-sheds-a-tenants-protocol-requests-once-its-in-flight-requests-fill-the-tenants-share-of-its-capacity.md)).
Lower free teams' `admission_capacity_percent` or a noisy tenant's rate limits through the management API, and add
replicas where nodes have room.

### Abuse or credential stuffing

Run the queries of the audit records' incident runbook ([audit-records.md](audit-records.md#incident-runbook)). Set the
tenant's or address rate limits, or disable the tenant.

### Stolen tokens

Follow the refresh-token replay entry of the same runbook. Revoke the subject's sessions and grants through the
management API.

### A compromised signing key

Roll the tenant's keys and retire the compromised one at once, which removes it from the JSON Web Key Set
([key rollover](capabilities/key-rollover.md)). Revoke the grants issued under it.

### The root key or KMS

Follow the chart README's root-key recovery and master-key rewrap
([Secret protection](../charts/ncode-identity-host/README.md#secret-protection)).

### A leaked credential

Revoke the vendor token or credential and write its replacement to Parameter Store. For a CI or GitHub compromise,
remove the apply role's trust. Never rewrite Git history to hide a leak. Read CloudTrail for the credential's use.

## Communicate

Post SEV1 and SEV2 incidents that customers see on the status page `status.ncodeid.com`, on the cadence of the severity
table, and post a resolved notice at the end.

A personal data breach is notified to each affected customer's account owner without undue delay and within 72 hours of
becoming aware of it, as the [data processing agreement](legal/data-processing-agreement.md) commits. The notice gives
the breach's nature, the categories and approximate numbers of data subjects and records, its likely consequences and
the measures taken. The customer, as controller, notifies its authority and data subjects.

A vulnerability reported through [SECURITY.md](../SECURITY.md) that is being exploited is an incident.

## Preserve evidence

Before retention removes it, export the affected tenants' audit records
([ADR-1527](decisions/1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md))
and the logs of the window: Grafana Cloud keeps 14 days on Free and 30 on Pro. The backups are immutable. Keep
CloudTrail's event history.

## Review

Hold a blameless review within five business days of every SEV1 and SEV2: the timeline, the cause, what worked, and
actions filed as GitHub issues in `NCodeGroup/identity` (a security-sensitive one as a draft security advisory). Update
this runbook in the same change as anything it got wrong. Rehearse one playbook each quarter with the restore drill
([#845](https://github.com/NCodeGroup/identity/issues/845)).
