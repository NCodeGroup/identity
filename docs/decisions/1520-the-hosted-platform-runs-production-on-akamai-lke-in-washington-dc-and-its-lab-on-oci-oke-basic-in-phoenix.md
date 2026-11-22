# 1520. The hosted platform runs production on Akamai LKE in Washington, DC, and its lab on OCI OKE Basic in Phoenix

- **Status:** Accepted
- **Date:** 2026-11-21
- **Deciders:** NCode Group

## Context

The hosted platform
([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md))
needs production, a lab and a place to develop. It is provisioned as OpenTofu from GitHub Actions
([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)) from the
published chart and images
([ADR-1133](1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)), under the operating
principles ([ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md)). Forces:

- **The must-haves.** Kubernetes on at least three nodes; Traefik behind one L4 load balancer that passes TLS through,
  so mutual TLS reaches the proxy; PostgreSQL as the system of record; Valkey for shared state
  ([ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md));
  a root key in a key management service
  ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md));
  and monthly billing.
- **The budget is about $100 a month all-in** while the platform offers only the free developer plan.
- **The database is the system of record**, so its availability bounds every tenant's.
- **Production is placed for customers, never for the operator.** It sits near customers' users and backends (most SaaS
  backends run in or near AWS `us-east-1`) and where the region has the capabilities the platform needs. The lab and the
  ephemeral runs are the operator's, near Denver, CO.
- **Prices were compared live on 2026-10-18 and Akamai's regions on 2026-11-14**
  ([hosting analysis](../hosting-analysis.md)).

## Decision

**Production runs on Akamai LKE in Washington, DC, with a NodeBalancer, a 3-node managed PostgreSQL and Valkey in the
cluster; the lab runs on OCI OKE Basic in Phoenix inside the free allowance; AWS is the growth path, cell by cell.**

1. **Production.** `us-iad`; LKE with its control plane managed by Akamai on 4 × Linode 2 GB (1 shared vCPU and 2 GB
   each); one NodeBalancer, L4 TCP with PROXY protocol v2, in front of Traefik; Akamai managed PostgreSQL, 3 × 1 GB
   nodes in the same datacenter, with daily backups kept 14 days and point-in-time recovery, beside the nightly
   encrypted off-vendor dump of ADR-1132; Valkey in the cluster (primary, replica, three Sentinels); the root key in AWS
   KMS in the platform's account, in a cloud region the cell's `Cell:CloudRegions` lists (ADR-1134 point 14), reached
   through web identity federation ([#177](https://github.com/NCodeGroup/identity/issues/177)). It costs $95 a month (4
   × $12 + $10 + $37).
2. **The control plane follows the plans.** The developer-plan cluster runs LKE's default single-replica control plane:
   free plans carry no SLA, and pods keep serving when it fails. Before the first paid plan a new cluster is created
   with the HA control plane (+$60 a month) and the cell moves to it: its release is deployed against the same managed
   database and its DNS records point at the new load balancer. HA is never enabled in place, which replaces every node
   irreversibly. Paid cells move to a cluster of their own when free usage shows in paid customers' latency or a
   contract asks for it; a dedicated cell runs on a dedicated-CPU node pool of that cluster or on a cluster of its own.
3. **The lab.** An OCI Pay-As-You-Go tenancy with home region `us-phoenix-1`; OKE Basic, whose control plane is free; 2
   × Ampere A1 (1 OCPU / 6 GB) in two availability domains, inside the free allowance ($0); the free network load
   balancer; CloudNativePG and Valkey in the cluster; arm64. It takes release candidates, runs the deployment posture
   and never hosts a customer tenant (ADR-1495).
4. **Development and ephemeral runs.** Daily development runs on the developer's machine (the Aspire stack,
   [ADR-1071](1071-the-aspire-apphost-runs-the-local-stack-in-health-gated-order-after-checking-the-frontend-toolchain.md))
   or a LAN-only homelab cluster, never under `ncodeid.net`, and may set `LocalDevelopment:Enabled` with the `local`
   root-key provider
   ([ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)).
   Runs that need a public OP or three or more nodes (the OpenID Foundation's hosted suite, load tests, topology
   rehearsals, Key Vault experiments) run on Azure through the Visual Studio credit, created and destroyed per run
   ([#132](https://github.com/NCodeGroup/identity/issues/132)).
5. **Shared foundations.** AWS `us-west-2` holds state, backups and vendor tokens (ADR-1132); Cloudflare hosts DNS
   (ADR-1495); GitHub holds source, CI and images (ADR-1133).
6. **Growth path.** A cell moves to AWS when a customer needs several availability zones, PrivateLink, Marketplace
   billing or a higher SLA, and the paid shared cell moves when the paid plans need it. Its shape, EKS or ECS on
   Fargate, is decided when the first cell is placed there; ECS runs outside the chart, so it needs its own record.

## Options considered

- **OCI Phoenix as production** (k3s HA on 3 × A1 at 2 OCPU / 12 GB, CloudNativePG, about $57–60). It has three
  availability domains and about 3× the capacity, but the platform operates the control plane and the database, A1
  capacity can be scarce and it is arm64 only. Rejected for production; the lab uses its free allowance.
- **Vultr VKE** (3 × 1 vCPU / 2 GB, 2-node managed database, $90). One fewer node and one fewer database replica, and
  the price of an HA control plane is unpublished. Rejected.
- **Civo NYC1 managed k3s** (3 × 4 GB, CloudNativePG, about $76, free egress). The platform operates PostgreSQL
  (managed starts at $43.45, about $119 in all), it has one US region and no HA control-plane tier. Rejected.
- **DigitalOcean DOKS** (3 × 2 GiB, 1 GiB managed database, about $63). No database HA within the budget; a standby
  brings it to about $111. Rejected.
- **Hetzner US** (k3s, about $123, all self-managed, plans shown unavailable) and **Hostinger** (Boston only, 24-month
  prepay, no load balancer or private network). Rejected.
- **Always-on Azure** (about $130). The Visual Studio credit forbids always-on use. Rejected.
- **AWS at launch** (EKS about $200–250, ECS on Fargate about $150–200 across three zones but outside the chart).
  Over the budget. Rejected for launch; it is the growth path.
- **Free database tiers** (Neon, Aiven). No HA, scale-to-zero or power-off, and no region choice. Rejected.
- **Akamai Dallas (`us-central`).** It lacks VPCs, Premium plans, Premium NodeBalancers and LKE Enterprise. Rejected.
- **Akamai Chicago (`us-ord`).** The same prices and capabilities, but about 15–20 ms from `us-east-1` against about
  1–2 ms. Rejected.
- **Chosen:** Akamai LKE in Washington, DC for production, OCI OKE Basic in Phoenix for the lab.
