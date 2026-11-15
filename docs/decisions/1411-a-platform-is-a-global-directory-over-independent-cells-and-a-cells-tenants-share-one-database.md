# 1411. A platform is a global directory over independent cells, and a cell's tenants share one database

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** NCode Group

## Context

Tenancy exists so that one running server hosts many tenants without their data bleeding across. The question is how
strong that isolation must be, where logical isolation (one deployment, one database, tenants separated by scoping)
ends and physical isolation (separate storage and infrastructure) begins, and how a platform grows past one deployment
without giving up one namespace of identifiers. Inside one deployment everything lives in one `OpenIdDbContext` behind a
single connection: the tenants and every tenant's clients, resource servers, scopes and grants, joined by foreign keys.

A hosted platform runs in more than one region, and a customer's data stays in the region its tenants are placed in
([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).
A platform therefore has two kinds of state. Some of it must be global: a tenant or team identifier is unique across
every deployment and is never reissued, a tenant is placed in one deployment, and a team's document must reach every
deployment that hosts one of its tenants. The rest is personal data and tenant configuration, which must stay in one
region, in one deployment, and which the global state must never hold.

A customer of the platform runs related tenants, such as a sandbox, a testing tenant and a production tenant, that share
a bill, a set of administrators and a policy. Nothing but a team names the customer: a policy that should bind all of a
customer's tenants has to be written into each one, the administrators of one tenant cannot create another, a plan's
tenant quota has no scope to apply to, and usage cannot be summed per customer.

Tenant isolation inside a deployment rests on the ambient-tenant query filter
([ADR-1018](1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md)). A filter
that matches every tenant when no scope is set fails open, so an unscoped query on a path nobody reviewed leaks rows
across tenants.

The family already has the pieces a grouping needs: a cascade of settings scopes, each setting declaring the scopes it
applies to ([ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md)),
security profiles that accumulate down the cascade
([ADR-1148](1148-a-scope-selects-security-profiles-that-accumulate-down-the-cascade-as-ceilings-defaults-and-requirements.md)),
tenants named by their creator
([ADR-1160](1160-a-tenant-is-named-by-its-creator-validated-by-its-strategy-and-never-reissued.md)), and a role model
whose assignments are owned by a scope
([ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md)). The
open questions are what a deployment is, what is global, how far tenant isolation goes inside a deployment, what groups
tenants, and how authority, settings, lifecycle and the console address them across deployments.

## Decision

**A platform is a global directory over independent cells. A cell is a host, its database, its Valkey and its KMS keys,
in one region; a cell's tenants share that one database and are separated by fail-closed scoping. A team groups a
customer's tenants and spans cells, and a tenant lives in exactly one cell.**

1. **A cell is the unit of deployment and of residency.** A cell is one host with one physical database and one
   `OpenIdDbContext`, one Valkey and its own KMS keys, in one region. The tenants of a cell, their child resources and
   the cell's copy of its teams form one connected graph with ordinary foreign keys. A cell has a name, a region, a kind
   (`shared` or `dedicated`, an open vocabulary
   ([ADR-1150](1150-vocabularies-are-open-unless-provably-closed.md))), the cloud regions its keys may live in and a
   management URL, bound from `Cell`. A tenant lives in exactly one cell, and its data stays in that cell's region.
   Serving a tenant's requests never calls the directory, because the cell's own tenant row is authoritative for
   serving.
2. **The global directory reserves, places and publishes, and holds no personal data.** It reserves tenant and team
   identifiers platform-wide and retires them when their owners are deleted, places each tenant in one cell, records the
   cells, and publishes team documents (name, settings and per-region root-key references). It holds identifiers,
   placements, cells and team documents, and nothing about a person. Creating a tenant reserves its identifier,
   inserts the tenant and then places it, and a failure releases the reservation; creation fails closed with `503` when
   the directory is unreachable.
3. **One cell runs on its own.** The directory is three ports (tenants, teams, cells). A local adapter over the cell's
   own retired-identifier tables is the default and needs no extra service, so a single deployment is a platform of one
   cell. A remote adapter, selected by `Directory:Url`, calls the directory service over HTTP with a client-credentials
   token and fails closed.
4. **The control plane lives in one control-plane cell.** The platform and team authority, platform and team settings,
   setup and the team root keys are held by the cell that sets `Cell:IsControlPlane`. Every other cell reaches the
   control plane through remote adapters that validate management tokens against its issuer and ask its authority
   endpoint who a caller is, and they fail closed. A tenant's own role assignments stay in the tenant's cell. Setup runs
   once per platform, in the control-plane cell only.
5. **A team spans cells.** The control-plane cell owns teams and publishes each team document to the directory. Every
   other cell mirrors team documents into its team rows from the directory's change feed and evicts the caches the
   change touches, so the settings cascade resolves the same way in every cell. A tenant never moves between teams.
6. **Tenant scoping fails closed inside a cell.** The ambient tenant has three states: unset, a tenant, and unscoped
   with a stated reason. The global query filter matches a tenant-bound row only when the scope is unscoped or names the
   row's tenant, so a query with no scope matches nothing, and every caller that reads across tenants opens an explicit,
   reasoned unscoped scope. Management control-plane routes open one for their route group.
7. **The database enforces the same rule.** On Postgres, every tenant-bound table has row-level security forced with one
   tenant-isolation policy, and the host runs under a runtime role that cannot bypass it, separate from the role that
   migrates the schema. The session carries the ambient state to the database, so a query that escapes the filter still
   returns no other tenant's rows.
8. **An analyzer refuses an unexplained cross-tenant query.** A call that ignores the query filters must carry a
   `// Cross-tenant: <why>` comment, so every read across tenants is a reviewed, reasoned exception.
9. **The platform is the one root of the settings cascade.** It is the host configuration (`OpenId:Platform:Settings`),
   one persisted settings document at `/api/platform/settings`, the master key of the Data Protection key ring
   ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)) at
   `/api/platform/encryption-keys`
   ([ADR-1247](1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md))
   and the authority node `(platform, platform)`. It has no identifier, list, create, delete or secrets, because there
   is exactly one, and the control-plane cell serves its management routes.
10. **A team is a persisted grouping of tenants.** Its creator names it in the tenant-identifier form
    ([ADR-1160](1160-a-tenant-is-named-by-its-creator-validated-by-its-strategy-and-never-reissued.md)); it has a display
    name, an `isDisabled` flag and a settings document. Every tenant belongs to exactly one team. A team may sit beneath
    a parent team
    ([ADR-1426](1426-a-team-can-nest-beneath-a-parent-team-and-its-subtree-is-walked-depth-first.md)).
11. **The platform's own team.** `TenantResolution:Root:TeamId` (default `platform`) names the team that holds the root
    tenant and every tenant a tenant-resolution strategy creates. It is top-level, holds no teams, and can be neither
    disabled nor deleted.
12. **The cascade.** Settings resolve through host configuration and the platform, the team or teams, the tenant, and the
    client or resource server, with the registered setting scopes `platform`, `team`, `tenant`, `client` and
    `resource_server`. A team document carries every setting that applies beneath it, so the security profiles and
    ceilings a team selects bind its tenants in every cell. A package extends the cascade by registering a setting
    ([ADR-1150](1150-vocabularies-are-open-unless-provably-closed.md)).
13. **The tenant limit.** `max_tenants` applies to the team scope, is not discoverable, and merges with `Min`. The
    directory enforces it when it reserves a tenant identifier, and a creation past it answers `409`. The ceiling is
    never serialized on the team row, so two creations that race may overshoot it by one.
14. **Authority runs platform, team, tenant, leaf.** `TeamAdmin` at a team creates its tenants and manages everything
    beneath it; `Owner` never creates. Granting or revoking `TeamAdmin` is `Operations.Create` at the team node, so an
    `Owner` can neither mint nor revoke it. `GlobalAdmin` acts across the platform. Platform, team and tenant nodes
    have globally unique, retired identifiers, so an assignment matches them on type and identifier; any other node
    counts an assignment only within its tenant, because client and resource-server identifiers are unique only within a
    tenant.
15. **Tenant routes.** Tenants are created and listed under `/api/teams/{teamId}/tenants`. The root tenant holds the
    `platform_settings`, `platform_encryption_keys`, `teams`, `team_settings` and `tenants` scope families, and team and
    platform management routes are mapped only in the control-plane cell.
16. **Lifecycle.** A team's lifecycle (create, disable, restore, delete) is authority at the node above it, which is the
    platform for a top-level team. Platform authority or `GlobalAdmin` lists teams and tenants across teams. A disabled
    team's tenants answer `404` on the protocol surface and on the management routes that act through a tenant's runtime
    (sessions, principals, local accounts), as a disabled tenant does, while their stored configuration stays
    manageable. A team with tenants, child teams or an owned machine client
    ([ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md)) cannot be
    deleted. A deleted tenant's or team's identifiers are retired in the directory.
17. **Audit and usage carry the team and the cell.** Usage rows and tenant audit events carry the team and the cell's
    name. Team and platform audit events are recorded in the control-plane tenant with the resource types `team` and
    `platform`. The platform and a team own settings but no secrets.
18. **The console names the scope first.** Platform surfaces sit at the root, team surfaces under `/teams/{teamId}/…`
    and tenant surfaces under `/tenants/{tenantId}/…`
    ([ADR-1255](1255-the-management-consoles-url-is-its-navigation-state-and-every-scopes-surface-has-its-own-address.md));
    it opens on the caller's `/home`
    ([ADR-1465](1465-role-lists-share-one-audited-grant-and-revoke-flow-and-a-caller-discovers-its-own-assignments.md)).
19. **The words.** "Platform" is the directory and its cells together; the platform environments of ADR-1495
    (`production`, `lab`, `ci`) are where a platform runs. "Cell" is the deployment, "directory" is the global registry,
    and "team" is the grouping. "Organization" stays free for the in-tenant B2B feature.

## Options considered

- **One deployment per platform, with a second deployment for physical isolation and nothing global.** Rejected: it
  gives no platform-wide identifier namespace, no placement, no region for a tenant's data, and no way for a team to span
  deployments.
- **Per-tenant databases with a control and tenant split and connection routing inside one deployment.** Rejected: it
  forces a bootstrap and registry split, cross-database reads when materializing a tenant, and a connection-routing
  seam, to put database-level isolation inside a deployment when a separate cell gives the same isolation far more
  simply.
- **A global database that holds tenants, their configuration and their data.** Rejected: it makes personal data global
  and ignores a tenant's region.
- **A directory that every request consults.** Rejected: it puts the directory in the serving path and makes its outage
  an outage of every tenant; the cell's own tenant row is authoritative for serving.
- **A query filter that matches every tenant when no scope is set.** Rejected: it fails open on every path nobody
  reviewed. A filter that matches nothing, plus explicit reasoned unscoped scopes, fails closed.
- **The query filter alone.** Rejected: a query that escapes the filter reads other tenants' rows. Row-level security
  under a runtime role that cannot bypass it makes the database enforce the same rule.
- **Name the grouping `organization`.** Rejected: it collides with the in-tenant B2B vocabulary and with the
  ecosystem's use of the word.
- **Name it `customer`.** Rejected: it is ambiguous for a consumer whose own customers are organizations.
- **Name it `tenant group`.** Rejected: it names the container by its contents, implies a many-to-many relationship,
  and collides with user groups.
- **A global directory over independent cells, with fail-closed scoping inside each cell (chosen).** A platform of one
  cell needs no extra service, a platform of many cells shares one identifier namespace and one team document, personal
  data stays in the cell and region of its tenant, and the graph inside a cell stays plain foreign keys.

## Consequences

- The data model inside a cell stays one foreign-key-connected graph in one `OpenIdDbContext`; nothing depends on a
  per-tenant database or a control and tenant connection split.
- A tenant's data, keys and sessions stay in its cell's region, and the directory holds no personal data, so erasing a
  tenant erases it from its cell alone.
- Capacity and blast radius are managed by how many tenants a cell hosts and how many cells run. An operator who needs
  compliance isolation runs a dedicated cell.
- The directory is a dependency of tenant and team creation and deletion, and of the change feed that delivers team
  documents. Serving never depends on it, and an unreachable directory blocks creation with `503`.
- The directory runs one replica with the `Recreate` strategy and keeps its expiring keys in process, an accepted
  exception to points 4 and 5 of
  [ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md). Serving never calls
  the directory (its point 13), so while it restarts only creating, deleting and moving tenants and teams answer `503`.
  A second replica needs the Valkey shared-state adapter first.
- Every cell outside the control plane depends on the control-plane cell for management authority and for team
  documents, and fails closed when it is unreachable.
- A path that reads across tenants must say so with an unscoped scope and a reason. An unscoped path that forgets one
  returns nothing instead of leaking rows, and a missing `// Cross-tenant:` comment fails the build.
- The runtime role cannot migrate or bypass row-level security, so the migration and runtime credentials are two
  secrets, and the host refuses to start on a runtime role that could bypass the policy.
- A customer's policy is written once, at its team, and binds every tenant it creates in every cell; a plan's tenant
  quota has a scope to apply to; usage sums per team.
- The administrators of a team create tenants without holding authority over the platform, and the platform
  administrator never needs to be a member of a customer's tenant.
- A tenant never moves between teams, and a customer that reorganizes recreates tenants.
- The `max_tenants` check can overshoot by one under a race, which is accepted because the row carries no serialized
  counter.

## References

- `OpenIdDbContext`, `IAmbientTenantAccessor`, `TenantPlane.Root`, `CellOptions`, the `Cell`, `Directory:Url`,
  `TenantResolution:Root:TeamId` and `OpenId:Platform:Settings` options
- [ADR-1018](1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md) — the
  ambient-tenant query filter and fail-closed scoping
- [ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md) — roles and ownership
- [ADR-1148](1148-a-scope-selects-security-profiles-that-accumulate-down-the-cascade-as-ceilings-defaults-and-requirements.md)
  — security profiles down the cascade
- [ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md) — the registered
  setting scopes
- [ADR-1150](1150-vocabularies-are-open-unless-provably-closed.md) — vocabularies are open
- [ADR-1160](1160-a-tenant-is-named-by-its-creator-validated-by-its-strategy-and-never-reissued.md) — creator-named,
  retired identifiers
- [ADR-1166](1166-a-tenant-is-created-and-seeded-through-one-provisioning-pipeline-under-every-strategy.md) — tenant
  provisioning and seeding
- [ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md) — the key hierarchy
- [ADR-1247](1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md) —
  the platform master key
- [ADR-1255](1255-the-management-consoles-url-is-its-navigation-state-and-every-scopes-surface-has-its-own-address.md) — console addresses
- [ADR-1426](1426-a-team-can-nest-beneath-a-parent-team-and-its-subtree-is-walked-depth-first.md) — nested teams
- [ADR-1465](1465-role-lists-share-one-audited-grant-and-revoke-flow-and-a-caller-discovers-its-own-assignments.md) — the caller's home
- [ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md) — machine clients
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)
  — platform environments and regional hostnames
