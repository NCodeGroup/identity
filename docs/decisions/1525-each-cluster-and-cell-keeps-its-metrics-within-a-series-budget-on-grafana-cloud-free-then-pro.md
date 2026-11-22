# 1525. Each cluster and cell keeps its metrics within a series budget, on Grafana Cloud Free and then Pro

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

The hosted platform's clusters and cells send their telemetry to Grafana Cloud
([hosting analysis](../hosting-analysis.md) §10). What a plan costs follows the number of active series, so the number
each cluster and cell sends needs a bound. Forces:

- **The plans' allowances.** Grafana Cloud Free allows 10,000 active series, 50 GB of logs and 50 GB of traces, kept 14
  days. Pro is $19 a month with those allowances included, then $6.50 per 1,000 series, and keeps metrics 13 months and
  logs and traces 30 days (grafana.com/pricing, captured 2026-11-22). A native histogram's buckets count 0.25 series
  each.
- **Every cluster emits infrastructure series** (kubelet, cAdvisor, kube-state-metrics, the node exporter, CoreDNS,
  Flux, cert-manager), **and every cell emits its host's** (`http.server.request.duration` per route, method and status,
  the runtime, Kestrel and the family's meters), its Valkey's and its Traefik's. A per-pod label multiplies them by the
  replicas.
- **Bounded attribute values are not a bounded series count.**
  [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md)
  bounds the values an attribute takes, not how many series a cell sends.
- **A paid plan's monthly SLA report needs more than 14 days of metrics.**
- **A cell's telemetry stays in its region**
  ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).

## Decision

**Each cluster keeps its metrics within 1,500 active series and each cell within 2,000; the platform runs on Grafana
Cloud Free while only the developer plan runs and moves to Pro before the first paid plan is sold.**

1. **The budgets.** A cluster's 1,500 series cover the node, kubelet, kube-state-metrics, CoreDNS, Flux, cert-manager
   and the shared Traefik. A cell's 2,000 cover its identity server, console, directory where it runs, Valkey and its
   own Traefik.
2. **The collector enforces them.** The platform's collector (Grafana Alloy through Grafana's Kubernetes monitoring
   chart) sends only an allow-list of metric names per budget unit, aggregates histograms across a cell's pods so `pod`
   and `instance` leave them, and drops any label outside the allow-list. A metric enters the allow-list once Grafana
   Cloud's cardinality management shows it fits its unit's budget.
3. **Alerts.** The account's active series at 80% of the plan's included 10,000, and each cell's series (by
   `cell.name`) at its budget.
4. **Free, then Pro.** Free carries the lab cluster and cell and the production cluster and shared cell: 7,000 series.
   Pro starts before the first paid plan, because monthly SLA reports need 13 months of metrics. On Pro each further
   cell costs at most $13 a month and each dedicated cluster at most $9.75, which their plans' prices cover.
5. **Logs and traces** stay within the included 50 GB each: info-level logs, and traces sampled at 10% in the
   collector, keeping every trace that holds an error.
6. **Regions.** A cell's telemetry goes to a Grafana Cloud stack in its region
   ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md));
   a region's stack has its own allowance.

## Options considered

- **Staying on Free with smaller budgets.** Rejected: 14 days of metrics can't support a monthly SLA report, and the
  allowance caps the fleet at a few cells.
- **Self-hosting Mimir, Loki and Tempo.** Rejected: they are stateful systems to run and back up beside the cells they
  watch, against
  [ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md).
- **Adaptive Metrics alone.** Rejected as the only control: it aggregates after ingestion, so the budget must hold at
  the collector.
- **Another vendor priced per host.** Rejected: it costs more than Pro at this size and is a further subprocessor.
- **Chosen:** a series budget per cluster and cell, enforced at the collector, on Free and then Pro.

## Consequences

- The developer plan's telemetry costs nothing.
- Pro adds $19 a month and per-cell usage from the first paid plan.
- Adding a metric is a cardinality decision.
- The collector configuration is part of the fleet's monitoring in `NCodeGroup/identity-platform`
  ([#845](https://github.com/NCodeGroup/identity/issues/845)).

## References

- [Hosting analysis](../hosting-analysis.md): §10, the pre-launch considerations.
- [ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md): the operating
  principles.
- [ADR-1197](1197-telemetry-has-one-owner-per-signal-bounded-attributes-and-spans-that-mirror-their-histograms.md): the
  telemetry owners and the bounded attributes.
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md):
  the platform's regions.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the platform's clusters.
- [ADR-1523](1523-a-replica-sheds-a-tenants-protocol-requests-once-its-in-flight-requests-fill-the-tenants-share-of-its-capacity.md):
  the load shedding whose shed counter is one of a cell's series.
- Issues [#845](https://github.com/NCodeGroup/identity/issues/845) and
  [#908](https://github.com/NCodeGroup/identity/issues/908).
