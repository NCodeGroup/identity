# 1524. A dedicated cell's capacity rating is measured by the repository's k6 test on dedicated-CPU nodes

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

A dedicated cell sells committed throughput at a stated p95 latency ([hosting analysis](../hosting-analysis.md) §12).
Forces:

- **The capacity figures are estimates.** §3 of the hosting analysis derives them from the hardware and the code, and
  nothing measures them.
- **Shared vCPUs suffer steal.** A rating holds only for the hardware it was measured on, so a rating measured on shared
  CPUs says little about the dedicated CPUs a dedicated cell runs on.
- **A replica needs the same measurement.** `OpenId:Admission:ConcurrencyLimit`
  ([ADR-1523](1523-a-replica-sheds-a-tenants-protocol-requests-once-its-in-flight-requests-fill-the-tenants-share-of-its-capacity.md))
  is the in-flight count at which a replica is full, and only a load test shows it.

## Decision

**A dedicated cell's capacity rating is 70% of the highest machine-to-machine token rate its own node pool sustains with
every threshold of `benchmarks/load/token-requests.js` met.**

1. **The test.** k6 v2.4.0 runs three constant-arrival-rate scenarios against one tenant's token endpoint: a generated
   client secret with `client_secret_basic` (a SHA-256 verifier) and `private_key_jwt` with an ES256 key, each half of
   `RATE`, and a supplied client secret (a PBKDF2 verifier,
   [ADR-1391](1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md)) at its own
   `SUPPLIED_RATE`. The thresholds are failed requests below 0.1%, p95 at most 200 ms for each machine-to-machine
   scenario and at most 1 s for the supplied secret, and no dropped iterations. The test lives in `benchmarks/load/`
   beside the micro-benchmarks: a measurement that no build gate runs
   ([ADR-1386](1386-primitives-are-benchmarked-in-a-project-the-build-compiles-and-nothing-runs.md)).
2. **The cell under test.** It has the dedicated cell's shape: a tainted node pool of 3 × Akamai Dedicated 4 GB (2
   dedicated vCPUs and 4 GB each) in `us-iad`, three identity replicas whose requests equal their limits, the cell's own
   Valkey and its 3-node managed PostgreSQL, whose plan the result names. The load generator is a separate Dedicated 4
   GB Linode in the same region. The tenant sets no rate limit, quota or `admission_capacity_percent`, and
   `OpenId:Admission:ConcurrencyLimit` is unset.
3. **The run.** It runs 10-minute stages at `RATE` 25, 50, 75, … until a threshold fails. The highest passing rate is
   the cell's capacity, and the rating is 70% of it, rounded down to a multiple of 5. `SUPPLIED_RATE` is raised by 1 per
   second per stage the same way, and its rating is 70% of its highest passing rate, rounded down.
4. **The concurrency limit.** A replica's `OpenId:Admission:ConcurrencyLimit` is the highest
   `http.server.active_requests` one replica showed at the highest passing rate, rounded up to a multiple of 10.
5. **The record.** The result is recorded in §3 of the hosting analysis: the date, the release, the node and database
   plans, the k6 version, the highest passing rates with their p95s, the ratings and the concurrency limit. A different
   node type or database plan, or a release that changes the token path's cost, is rated again.
6. **The commitment.** A dedicated cell commits to its rating at p95 ≤ 200 ms for machine-to-machine token requests, to
   its supplied-secret rating at p95 ≤ 1 s, and to 99.9% availability.

## Options considered

- **Selling the §3 estimate.** Rejected: it is unmeasured, and the figures it rests on come from shared CPUs.
- **Running the rating on Azure,** where
  [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)
  runs load tests. Rejected for the rating: it describes the Akamai dedicated CPUs a dedicated cell runs on. Azure keeps
  the topology runs.
- **Selling 100% of the measured rate.** Rejected: it leaves no headroom for a node lost or recycled.
- **p99 as the threshold.** Rejected: on three replicas the tail is dominated by pauses outside the request path.
- **Interactive sign-in in the test.** Rejected: its cost is the PBKDF2 verification the supplied-secret scenario
  measures, and it adds the auth bundle's round trips.
- **Chosen:** the repository's k6 test on the dedicated cell's own node pool, rated at 70% of the highest passing rate.

## Consequences

- Each change of hardware or cost needs one run, a few dollars of hourly-billed nodes.
- A dedicated cell's contract states the rating of the latest recorded run.
- The run sets the cell's concurrency limit, which ties the load shedding of ADR-1523 to measured capacity.

## References

- [Hosting analysis](../hosting-analysis.md): §3, the capacity figures, and §12, the dedicated cell's promise.
- [ADR-1386](1386-primitives-are-benchmarked-in-a-project-the-build-compiles-and-nothing-runs.md): the benchmarks that
  no build gate runs.
- [ADR-1391](1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md): the client
  secret verifiers, including the PBKDF2 one of a supplied secret.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the platform's clusters and the Azure load tests.
- [ADR-1523](1523-a-replica-sheds-a-tenants-protocol-requests-once-its-in-flight-requests-fill-the-tenants-share-of-its-capacity.md):
  the load shedding whose concurrency limit the run sets.
- Issue [#907](https://github.com/NCodeGroup/identity/issues/907).
