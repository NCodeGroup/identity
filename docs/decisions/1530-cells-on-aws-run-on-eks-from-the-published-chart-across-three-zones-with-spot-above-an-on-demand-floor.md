# 1530. Cells on AWS run on EKS from the published chart, across three zones, with spot above an on-demand floor

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

AWS is the hosted platform's growth path, cell by cell
([ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)
decision 6), and the shape of a cell there is open ([#914](https://github.com/NCodeGroup/identity/issues/914)). Forces:

- **The platform runs only the artifacts it publishes for self-hosters**
  ([ADR-1133](1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)), so a cell's
  deployment is the published chart and images, whatever the vendor.
- **A cell moves between vendors by moving its database**, through the tenant export and import
  ([ADR-1514](1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md)) or PostgreSQL
  logical replication.
- **The shapes differ in cost and in what they ask the platform to operate.** The hosting analysis compares them at the
  entry size ([§11](../hosting-analysis.md#11-growth-path-aws)): EKS on demand costs about $200–250 a month, EKS with
  spot above an on-demand floor about $185–235 and ECS on Fargate about $150–200, all across three zones.
- **The ingress passes TLS through an L4 load balancer to Traefik**, so mutual TLS reaches the proxy
  ([ADR-1506](1506-the-ingress-controller-holds-a-client-certificate-the-pods-can-require-and-never-read-as-an-end-clients.md),
  [ADR-1507](1507-the-host-chart-routes-to-its-https-only-pods-through-gateway-api-a-traefik-ingress-or-the-operators-own-ingress.md)).
- **The keys sit in a key management service**
  ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md),
  [ADR-1471](1471-a-tenants-signing-and-encryption-keys-can-be-held-in-a-key-management-service.md)).

## Decision

**A cell on AWS runs the published chart on EKS in `us-east-1`, across three availability zones, with an on-demand floor
that carries its committed load and spot capacity for headroom.**

1. **Which cells and when.** A dedicated cell or dedicated cluster is placed on AWS from its creation when its customer
   asks for AWS, several availability zones, PrivateLink or Marketplace billing. The paid shared cell moves when the
   platform sells paid plans an SLA above 99.9%. The free shared cell stays on the cheapest vendor that serves it well.
2. **Compute.** EKS with Karpenter: an on-demand node pool in each zone sized to run the cell's committed replicas
   alone, plus Valkey's, Flux's and cert-manager's pods when they run in the cluster, and a spot node pool for the rest,
   over several Graviton families and sizes with the price-capacity-optimized strategy. Karpenter's interruption
   handling drains a reclaimed node within the two-minute notice, which the chart's pre-stop wait and grace period fit.
   Topology spread and pod disruption budgets keep each cell at its replica count.
3. **Data.** RDS for PostgreSQL Multi-AZ and ElastiCache Serverless for Valkey, both through the chart's connection
   parts
   ([ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md),
   [ADR-1510](1510-the-host-chart-passes-a-valkey-servers-address-and-password-as-parts-and-the-adapter-assembles-the-connection.md)).
   Nothing stateful runs on spot.
4. **Ingress.** A Network Load Balancer through the AWS Load Balancer Controller, with IP targets, PROXY protocol v2,
   TLS passed through to Traefik and a deregistration delay under two minutes.
5. **Keys and secrets.** KMS in the cell's region through EKS Pod Identity. Secrets reach the cluster through
   [ADR-1526](1526-the-platforms-cluster-secrets-reach-each-cluster-from-aws-parameter-store-through-external-secrets-operator.md)'s
   mechanism.
6. **Commitments.** A Compute Savings Plan on the on-demand floor and a Database Savings Plan on RDS and ElastiCache,
   each sized below the lowest month after two months of steady usage, for one year with no upfront payment.
7. **Region.** `us-east-1`, in the metro of `us-iad`, so a cell that moves keeps its customers' latency.

## Options considered

- **ECS on Fargate.** The cheapest three-zone start. Rejected: it is a second deployment shape outside the chart, with
  task definitions only the platform runs, against ADR-1133. An ALB terminates mutual TLS in its own way, a second
  ingress path to verify; certificates are per load balancer, which binds custom domains; and deployments are pushed
  instead of pulled by Flux.
- **EKS on demand only.** Rejected: spot halves the cost of headroom, and worker nodes come to dominate the bill.
- **EKS Auto Mode.** Rejected: it adds a management fee on every node, spot included, for the same Karpenter.
- **Aurora Serverless v2.** Rejected at this size: it costs more than RDS Multi-AZ, and its cold resume delays a first
  sign-in.
- **Lambda.** Rejected: the host is a long-lived process with background services and in-memory caches.
- **Chosen:** EKS from the published chart, with spot above an on-demand floor.

## Consequences

- The cell module of the fleet has an AWS stack (`stacks/aws`) beside Akamai's
  ([#845](https://github.com/NCodeGroup/identity/issues/845)).
- The chart and the images run unchanged on AWS, so a cell moves between Akamai and AWS by moving its database.
- An AWS cell costs about $185–235 a month at the entry size, less with the commitments.
- A cell on AWS supports an SLA above 99.9%, because it runs across three availability zones.
- The on-demand floor sets the cost that does not fall with load, and the spot pool sets the interruptions a cell
  absorbs: the replica count, the topology spread, the pod disruption budgets and the pre-stop wait keep a reclaimed
  node from taking a cell below its replicas.

## References

- [Hosting analysis §11](../hosting-analysis.md#11-growth-path-aws): the shapes compared at the entry size and the
  growth path.
- [ADR-1133](1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md): the published
  artifacts.
- [ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md): the
  root key in a key management service.
- [ADR-1471](1471-a-tenants-signing-and-encryption-keys-can-be-held-in-a-key-management-service.md): a tenant's keys in
  a key management service.
- [ADR-1506](1506-the-ingress-controller-holds-a-client-certificate-the-pods-can-require-and-never-read-as-an-end-clients.md):
  the ingress controller's client certificate.
- [ADR-1507](1507-the-host-chart-routes-to-its-https-only-pods-through-gateway-api-a-traefik-ingress-or-the-operators-own-ingress.md):
  the chart's routing to its HTTPS-only pods.
- [ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md):
  the PostgreSQL connection's parts.
- [ADR-1510](1510-the-host-chart-passes-a-valkey-servers-address-and-password-as-parts-and-the-adapter-assembles-the-connection.md):
  the Valkey connection's parts.
- [ADR-1514](1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md): moving a tenant
  between cells.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the production vendor and the growth path.
- [ADR-1526](1526-the-platforms-cluster-secrets-reach-each-cluster-from-aws-parameter-store-through-external-secrets-operator.md):
  the cluster secrets' delivery.
- Issues [#845](https://github.com/NCodeGroup/identity/issues/845) and
  [#914](https://github.com/NCodeGroup/identity/issues/914).
