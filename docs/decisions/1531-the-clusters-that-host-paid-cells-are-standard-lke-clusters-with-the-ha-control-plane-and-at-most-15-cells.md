# 1531. The clusters that host paid cells are standard LKE clusters with the HA control plane and at most 15 cells

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

Paid cells move to a cluster of their own as the paying base grows
([ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)
decision 2). Forces:

- **Every cell on a cluster shares its control plane, add-ons, Kubernetes upgrades and cluster-admin credentials**, so a
  mistake or a compromise at any of them reaches every cell on the cluster.
- **A standard LKE cluster holds at most 250 nodes and 1,000 pods; LKE Enterprise holds 500 nodes and 5,000 pods.** A
  dedicated cell uses 3 nodes and about 22 pods, so the pod limit binds first, at roughly 35–40 cells.
- **The HA control plane costs $60 a month and can't be enabled in place** without replacing every node.
- **LKE Enterprise is sold through Akamai sales in limited regions at an unpublished price**
  ([hosting analysis §12](../hosting-analysis.md#the-cluster-that-hosts-paid-cells), checked 2026-11-14).

## Decision

**A cluster that hosts paid cells is a standard LKE cluster created with the HA control plane and holds at most 15
cells; the next cluster is created when one reaches the cap.**

1. **The cap counts cells of every kind on the cluster**, the paid-only shared cell included, and is set by blast
   radius, not capacity: 15 dedicated cells use about 330 pods and 45 nodes, about a third of the pod limit, so pod
   churn and node replacement during an upgrade stay far from it.
2. **Clusters B1, B2, … are each created with the HA control plane.** A cell is placed on the newest cluster with room;
   the fleet inventory records each cell's cluster and its checks refuse a sixteenth cell
   ([#845](https://github.com/NCodeGroup/identity/issues/845)).
3. **Kubernetes upgrades roll cluster by cluster:** the lab, cluster A, B1, B2, …, then each dedicated cluster in its
   maintenance window, each verified on its cells' `canary` tenants.
4. **LKE Enterprise doesn't replace the fleet.** Its larger limits don't move a cap set by blast radius, and its own
   control plane and upgrade control are what a dedicated cluster sells, which a customer who needs them buys.

## Options considered

- **No cap below the limits.** One mistake or compromise reaches every paying customer. Rejected.
- **A cap of 10.** More clusters, each $60 a month and one more to upgrade, for little less exposure. Rejected.
- **A cap at capacity, 35–40.** The blast radius is the reason for the cap. Rejected.
- **A fleet of LKE Enterprise clusters.** Point 4, and its price can't be put in a plan's price. Rejected.
- **A cluster per dedicated cell.** That is the dedicated cluster, sold as such.
- **Chosen:** standard LKE clusters with the HA control plane, at most 15 cells each.

## Consequences

- One more paid cluster is created every 15 cells, at $60 a month for its HA control plane plus its load balancer and
  add-ons.
- Adding a cluster needs no product change, because a cell moves between clusters without changing its tenants
  ([hosting analysis §12](../hosting-analysis.md#the-cluster-that-hosts-paid-cells)).
- The fleet inventory enforces the cap.

## References

- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md):
  the environment model and the platform's domains.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the platform's clusters and the control plane that follows the plans.
- [Hosting analysis §12](../hosting-analysis.md#the-cluster-that-hosts-paid-cells): the cluster that hosts paid cells.
- Issues [#845](https://github.com/NCodeGroup/identity/issues/845) and
  [#915](https://github.com/NCodeGroup/identity/issues/915).
