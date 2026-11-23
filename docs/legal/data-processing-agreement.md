# Data Processing Agreement

- **Status:** Draft

This cover page incorporates the Common Paper
[DPA Standard Terms Version 1.1](https://commonpaper.com/standards/data-processing-agreement/1.1), free to use under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), and forms part of the [Terms of Service](terms-of-service.md)
([ADR-1529](../decisions/1529-the-hosted-platforms-terms-dpa-and-privacy-policy-are-public-documents-built-on-common-papers-standard-terms.md)).

## Key Terms

| Term | Value |
| --- | --- |
| Agreement | The Terms of Service. |
| Provider | NCode Group, the processor. |
| Customer | The controller. |
| Categories of Data Subjects | The end users who sign in to the Customer's tenants, and the Customer's administrators and team members. |
| Categories of Personal Data | Identifiers (subject identifier, username, email address, phone number), credentials (password hashes, passkey public keys, second-factor registrations), the profile claims the Customer configures, session and token metadata, IP addresses and user agents, and audit records. |
| Special Category Data | None; the Customer configures no claim that carries it. |
| Special Category Data Restrictions or Safeguards | Not applicable. |
| Frequency of Transfer | Continuous for the Subscription Period. |
| Nature and Purpose of Processing | Authenticating the Customer's end users, issuing and validating tokens, and managing identities for the Customer's applications under the Agreement. |
| Duration of Processing | The Subscription Period, then deletion of tenant data within 30 days, audit records expiring 90 days after they are written ([ADR-1527](../decisions/1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md)), and encrypted, immutable backups ageing out within 13 months. |
| Approved Subprocessors | See Annex 2. |
| Governing Member State | Ireland. |
| Provider Security Contact | `security@ncodeid.com` |
| Security Policy | Annex 1. |

## Annex 1: Security measures

- Every stored secret and key is encrypted under a per-owner master key wrapped by a root key in a key management
  service
  ([ADR-1235](../decisions/1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)).
- The host accepts TLS 1.2 and 1.3 with AEAD cipher suites only
  ([ADR-1499](../decisions/1499-the-host-accepts-only-tls-1-2-and-1-3-with-aead-cipher-suites.md)).
- Tenant isolation is enforced by query filters and PostgreSQL row-level security
  ([ADR-1018](../decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md)).
- Backups are nightly, encrypted, off-vendor and immutable
  ([ADR-1132](../decisions/1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)).
- Every vendor account uses passkeys or hardware keys, and tokens carry least privilege.
- Every security-relevant operation leaves an audit record
  ([ADR-1283](../decisions/1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md)).
- An incident-response runbook governs the response to an incident ([incident response](../incident-response.md)).
- A personal data breach is notified to the Customer without undue delay and within 72 hours of the Provider becoming
  aware of it.

## Annex 2: Subprocessors

| Subprocessor | Purpose | Location |
| --- | --- | --- |
| Akamai Technologies | Compute and load balancing | Washington, DC |
| Aiven | Managed PostgreSQL for Akamai | Washington, DC |
| Amazon Web Services | Key management, backups, secrets and transactional mail | `us-east-1` and `us-west-2` |
| Cloudflare | DNS | Global |
| Grafana Labs | Telemetry | United States |

The Provider gives 30 days' notice of a new subprocessor, and the Customer may object.
