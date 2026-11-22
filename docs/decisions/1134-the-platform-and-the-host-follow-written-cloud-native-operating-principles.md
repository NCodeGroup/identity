# 1134. The platform and the host follow written cloud-native operating principles

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** NCode Group

## Context

The hosted platform ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)) is provisioned as public
code ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)) from
the same artifacts offered to self-hosters
([ADR-1133](1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)). Many choices (how a
node is patched, where a cache lives, how a schema changes, whether a feature may keep in-memory state) need one shared
answer rather than being argued case by case. An identity provider is also an availability dependency for every relying
party: when it is down, nobody signs in.

## Decision

**The platform and the host follow these principles; a design that breaks one needs an ADR that says why.** Reviews cite
a principle by number.

1. **Cattle, not pets.** Every node, pod and cluster is disposable and rebuildable from code. Nothing is configured by
   hand; machines are replaced rather than patched in place. Interactive access (SSH, `kubectl exec`) is break-glass
   only, audited, and never a change mechanism.
2. **Immutable artifacts.** One signed artifact (image digest, chart, module release) is built once and promoted through
   lab, the production `canary` tenant and production. A change is a new release, never an in-place edit; containers
   run with read-only root filesystems.
3. **Everything as code, reconciled continuously.** Desired state lives in Git: OpenTofu for cloud resources and Flux for
   the cluster. A scheduled plan finds drift, which is reconciled rather than hand-fixed. The only manual steps are the
   documented bootstrap ones.
4. **Stateless services.** Processes keep no state that matters beyond a request. Durable state lives in PostgreSQL,
   shared ephemeral state in the shared-state port (`NCode.Identity.SharedState`) backed by Valkey
   ([ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md)), and keys under the
   secret-protection hierarchy or a KMS. Any replica serves any request: no sticky sessions. Configuration comes from the
   environment, logs go to stdout, and processes start fast and shut down gracefully.
5. **Redundant by default.** Every service runs at least two replicas spread across nodes, with PodDisruptionBudgets and
   readiness gates. Calls use timeouts and bounded retries with jitter. Remaining single points of failure are
   accepted explicitly, not by accident.
6. **Recoverable and proven.** Backups are encrypted, immutable and off-vendor. Restores and full rebuilds are drilled
   against written RPO and RTO targets.
7. **Secure by default.** Least privilege everywhere, OIDC or workload identity instead of long-lived credentials, no
   secrets in Git, encryption in transit and at rest, only signed artifacts admitted, and default-deny network policy.
8. **Observable.** Metrics, logs and traces flow through OpenTelemetry, with service-level objectives and symptom-based
   alerts.
9. **Small, safe, automated changes.** Rollouts are progressive and halt on failed readiness. Schema changes expand and
   contract so the old and new releases run side by side
   ([ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md)).
10. **Portable by open standards.** Kubernetes, OCI, OpenTelemetry, PostgreSQL and S3 are the interfaces, so a vendor is
    replaced by swapping a component module.
11. **Cost-aware.** Sizes come from measurements, and every vendor has a budget alert.
12. **Each cell deploys independently and fails alone.** A cell is a host, its database, its Valkey and its KMS keys in
    one region ([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)).
    A cell is released, scaled, restored and drained on its own, and its outage leaves every other cell serving. No
    cell holds state of another cell's tenants, and a cell reaches another cell only for the control plane's authority,
    over an adapter that fails closed.
13. **Serving never calls the directory.** A cell serves a tenant's requests from its own tenant row. The directory is
    called when a tenant or a team is created, deleted or moved, and when a cell registers or follows team changes, so
    an unreachable directory stops those operations with `503` and never stops a tenant that is already served.
14. **Backups and telemetry stay in the region.** A cell's backups, logs, metrics and traces are written to a location
    in the cell's region (`backup.location` and `telemetry.otlpEndpoint` in the chart), because a tenant's data stays
    in its cell's region. The KMS adapters refuse a key outside the cell's cloud regions (`Cell:CloudRegions`).

## Options considered

- **No written principles; decide case by case.** Each decision stays locally reasonable while the whole drifts (a
  hand-patched node here, an in-memory store there). Rejected.
- **Principles as guidance only, without the ADR requirement for exceptions.** Exceptions accumulate silently. Rejected:
  an exception is fine, but it is explicit.
- **Written principles, with an ADR for every exception.** Chosen.

## Consequences

- The host's code is correct across interchangeable replicas; the production coding conventions carry that rule
  ([`csharp-production.instructions.md`](../../.github/instructions/csharp-production.instructions.md) §4).
- The in-process `IJobDispatcher` loses in-flight jobs when a replica is replaced
  ([#25](https://github.com/NCodeGroup/identity/issues/25)), so a durable adapter is a prerequisite before the platform
  offers CIBA push or back-channel logout.
- The accepted single points of failure are a cell's one datacenter and the developer-plan cluster's single-replica
  control plane
  ([ADR-1520](1520-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)).
- A reviewer cites a principle by number instead of re-arguing it.
- A cell is the unit of release, restore and failure, so an incident, a bad release or a restore touches one cell and
  one region's tenants.
- The directory is off the serving path, so its availability bounds only the creation, deletion and move of tenants and
  teams.

## References

- [The Twelve-Factor App](https://12factor.net/).
- [Hosting analysis](../hosting-analysis.md): §4 HA, §9 delivery, §10 pre-launch considerations.
- [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
  [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md),
  [ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md),
  [ADR-1133](1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md),
  [ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md),
  [ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md).
