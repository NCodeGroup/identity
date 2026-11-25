# 1417. Management authority is a role assignment at a node of the platform, team and tenant hierarchy

- **Status:** Accepted
- **Date:** 2026-11-05
- **Deciders:** NCode Group

## Context

The management API needs one answer to "who may do this to that resource". The resources form a hierarchy: the
platform, the teams beneath it (a team can nest beneath a parent team,
[ADR-1426](1426-a-team-can-nest-beneath-a-parent-team-and-its-subtree-is-walked-depth-first.md)), the tenants in a team
([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)), and
beneath a tenant its clients, resource servers and grants.

A platform runs as independent cells under a global directory, and the platform and the teams are held by the
control-plane cell while a tenant lives in one other cell or in that one. A tenant's cell must decide who may act on its
tenant, including a caller whose platform or team assignment sits in the control-plane cell, and it must decide from its
own data, whatever the state of the control-plane cell or the directory
([ADR-1536](1536-a-platforms-global-plane-is-a-standalone-directory-and-a-dedicated-control-plane-cell-and-every-other-cell-runs-and-is-managed-on-its-own.md)).
A cell that serves its tenants through a dynamic strategy also has administrators of its own, who manage every tenant of
that cell and nothing beyond it.

Two sources of authority could coexist: a role assignment at a node, inherited down that hierarchy, and a role claim in
the caller's token matched against the tenant a node carries. The management API authenticates every caller against the
control-plane tenant, so the `tid` of every token is that tenant's identifier, and the platform node and every team node
carry the same identifier as their address. A tenant role claim matched by `tid` therefore reaches the platform and every
team as well as the control-plane tenant's own tenant plane: a tenant role would administer the deployment. Such a claim
also appears in no listing of who holds authority, and no endpoint grants or revokes it.

Each of the platform, a team and a tenant needs an administrator role that confers its administration where it is
assigned. A resource's owner manages that one instance but never creates anything beneath it, so it cannot be a tenant's
administrator. The management API's own surface (settings, secrets, clients, resource servers, teams, tenants) needs
scopes on a resource server it can seed into every tenant.

## Decision

**Authority is a persisted role assignment `(principal, role, node)`, inherited down the authority chain, and no role
claim confers management authority except the development bootstrap client's `GlobalAdmin` stamp.** Roles and ownership are one mechanism.

1. **The hierarchy and the roles.** An assignment at a node holds at the node and at everything beneath it: the chain
   runs from the platform node through every team above a tenant to the tenant, and on to a client, resource server or
   grant node (`ResourceNodeTypes`). The node type `cell` sits beside the team chain: a tenant node's ancestors include
   the node `(cell, <Cell:Name>)` when this cell holds the tenant, the cell's management tenant included. The built-in
   roles (`BuiltInRoles`) are `GlobalAdmin`, `TeamAdmin`, `TenantAdmin`, `CellAdmin` and `Owner`. The administrator
   roles manage their node and everything beneath it. `GlobalAdmin`, `TeamAdmin` and `TenantAdmin` are creation roles
   (`Operations.Create`); `CellAdmin` is a management role that creates the clients, resource servers and other children
   of its cell's tenants, and creates a tenant only of a team whose `tenant_creation_cells` setting names its cell
   ([ADR-1536](1536-a-platforms-global-plane-is-a-standalone-directory-and-a-dedicated-control-plane-cell-and-every-other-cell-runs-and-is-managed-on-its-own.md),
   decision 10). `Owner` manages its instance, including its settings, secrets, scopes and owners, and never creates.
2. **A tenant administrator.** A `TenantAdmin` assignment at a tenant's node manages the tenant and creates its clients,
   resource servers and other children. It never reaches a team or the platform, even for the control-plane tenant,
   whose identifier those nodes carry only as their address. The administrators of every team above a tenant grant that
   tenant's administrators, and its owners cannot.
3. **The record.** `PersistedRoleAssignment` (`RoleAssignmentEntity` in the Entity Framework store) sits behind
   `IRoleAssignmentStore`. It has `TenantId`, a server-generated `AssignmentId`, `PrincipalId`, `RoleName` (a name, not a
   foreign key), `ResourceType`, `ResourceId` and an interceptor-managed `ConcurrencyToken`, with a lookup index on
   tenant, resource type and resource id, and a unique index on tenant, principal, role, resource type and resource id
   holds each assignment once, so granting a principal a role it already holds at a node returns the existing
   assignment. Assignments at team and platform nodes live in the control-plane cell, under its control-plane tenant;
   one at a tenant node lives in the tenant's cell, under that tenant's identifier.
4. **The handler.** `OwnershipHandler` resolves the caller's `PrincipalId` (`IPrincipalResolver`,
   [ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)) in the control-plane tenant's scope and succeeds
   when one of its assignments of an authorizing role sits at the node or at an ancestor that `IAuthorityChainResolver`
   derives from the resource. No parent pointers are stored. Platform, team and tenant nodes match on type and identifier
   across tenants; any other node matches only an assignment of the node's tenant.
5. **Platform and team authority is read through `IControlPlaneAuthority`, and every cell holds it as a replicated
   document.** The port answers a caller's role names at the platform and team nodes and whether it is a global
   administrator (`IsGlobalAdmin`). Its local adapter, in the control-plane cell, reads the platform and team
   assignments from `IRoleAssignmentStore`. The control-plane cell publishes them as the `platform-authority` document
   and one `team-authority` document per team, which the directory's change feed carries to every other cell, and the
   remote adapter, `ReplicatedControlPlaneAuthority`, answers from the replicas each cell applied into its own database,
   with the team chain from the teams it mirrors. A cell never calls another cell to authorize a request. A tenant's own
   assignments are read from the tenant's cell, and the two sources together answer the chain that
   `IAuthorityChainResolver` derives. A cell that has applied no authority document holds no platform or team
   assignment, so it grants none through them, and it never allows on a failure. A principal of a cell's management
   tenant is authorized only by the assignments stored in the cell and never asks `IControlPlaneAuthority`. A workload
   cell names only the principals of its own management tenant, and lists and grants a role holder of the control-plane
   tenant by identifier
   ([ADR-1465](1465-role-lists-share-one-audited-grant-and-revoke-flow-and-a-caller-discovers-its-own-assignments.md),
   [ADR-1536](1536-a-platforms-global-plane-is-a-standalone-directory-and-a-dedicated-control-plane-cell-and-every-other-cell-runs-and-is-managed-on-its-own.md)).
6. **No claim authority.** The one role claim that confers management authority is the development bootstrap client's
   `GlobalAdmin` stamp
   ([ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md)).
   Every other authority is an assignment that the `owners` and `admins` lists show, grant and revoke, and the root
   administrator that setup creates holds an assignment like every other administrator
   ([ADR-1512](1512-an-instance-is-set-up-once-through-a-one-time-setup-token-that-creates-a-root-global-administrator.md)).
7. **Creators and owners.** A resource's creator is assigned `Owner` in the same unit of work as the insert, unless it
   cannot hold it ([ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md)). A
   resource keeps at least one owner: removing the last answers `409`, and the administrators above it can always
   reassign.
8. **Tenant administrators over HTTP.** `/api/tenants/{tenantId}/admins` (`GET`, `POST`, `DELETE …/{principalId}`) lists,
   grants and revokes `TenantAdmin` as `/api/teams/{teamId}/admins` does `TeamAdmin`. Both need `Operations.Create` at
   the node, so an `Owner` cannot.
9. **The management scopes.** Scopes are `{verb}:{family}` (`ManagementScopes`) on the one reserved system resource
   server `urn:ncode:management`. `ManagementResourceServerSeedHandler` seeds it on the `SeedTenantCommand` fan-out into
   every tenant that lacks it, with `tenant_settings`, `tenant_secrets`, `encryption_keys`, `clients`, `client_secrets`,
   `resource_servers`, `resource_server_secrets`, `client_grants`, `local_accounts`, `read:grants` and `delete:grants`.
   Into the control-plane cell's root tenant (`TenantPlane.Root`) only, it also seeds `platform_settings`,
   `platform_encryption_keys`, `teams`, `team_settings` and `tenants`, so a workload tenant never holds them though it
   shares the audience.

## Options considered

- **Two coarse claim realms.** A claim per realm cannot express a node, an owner or an inheritance chain, and appears in
  no listing. Rejected.
- **Separate role and ownership subsystems or tables.** Two mechanisms answer the same question twice and drift. One
  assignment table serves both. Rejected.
- **An ancestor chain stored on each assignment.** The hierarchy is fixed and derivable from the resource, so a stored
  chain duplicates it and invites drift without a faster decision. Rejected.
- **A tenant role matched by the token's `tid`.** Every token carries the control-plane tenant's identifier, so the role
  reaches the platform and every team. Rejected.
- **`Owner` as a tenant's administrator.** An owner never creates a tenant's clients or resource servers, so the
  tenant's administration would still need a second role. Rejected.
- **Two management audiences.** A second audience splits the scopes and the seeding for no gain; scope families the
  control-plane cell's root tenant alone holds are seeded into it alone. Rejected.
- **A role assignment at a node, with the management scopes on one resource server.** One grantable, listable authority
  source, inherited down the chain. Chosen.
- **Ask the control-plane cell for authority on every decision.** A cell would stop managing its tenants whenever the
  control-plane cell is down, and every decision would cost a cross-cell call. Rejected.
- **A staleness cutoff on the replicated authority.** A cell would refuse management during a directory outage, which
  turns an outage into a denial. Rejected: a cell authorizes from what it last applied, and the emergency revocation of
  an administrator is the disabling of its account, which ends its access within the management token's lifetime.
- **Platform and team assignments authored in the control-plane cell, replicated whole into every cell and read through
  `IControlPlaneAuthority` (chosen).** One author per platform, one answer per decision from the cell's own data, and a
  tenant's own assignments stay beside the tenant.

## Consequences

- Every management authority except the development bootstrap client's stamp is an assignment that the `admins` and `owners` endpoints list,
  grant and revoke.
- Platform and team authority is revoked once, in the control-plane cell, and every cell sees the revocation within the
  publish latency plus one feed poll. Replicated authority is statically stable: a cell that cannot reach the directory
  or the control-plane cell keeps authorizing from what it last applied, and the emergency revocation of an
  administrator is disabling its account, which ends its access in every cell within the management token's lifetime.
- `CellAdmin` at a cell's node manages that cell's tenants as `TenantAdmin` does one tenant, never reaches a team or the
  platform, and is granted only to principals of the cell's management tenant.
- `TeamAdmin` and `TenantAdmin` confer the same operations where they are assigned, one tier apart.
- A team's administrators grant a tenant's administrators, and a tenant's owners cannot.
- The control-plane tenant's administrators never hold platform or team authority through the tenant they authenticate
  against.
- A resource always has an owner, and a team or platform administrator can always reassign it.
- Authorization reads the caller's assignments by principal across every tenant once per decision and matches them
  against the derived chain; the lookup index on tenant, resource type and resource id serves the owner and
  administrator listings.
- A workload tenant holds the management scopes of its own families only, so a token for it cannot name a platform or
  team family.
- A smell: a new role that needs a rule outside `OwnershipHandler` means the assignment model is missing a node type.

## References

- `src/NCode.Identity.OpenId.Management/BuiltInRoles.cs`, `ManagementScopes.cs`,
  `ResourceServers/ManagementResourceServerSeedHandler.cs`; `IControlPlaneAuthority`
- [ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md): principal resolution
- [ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md):
  management API authentication and the `GlobalAdmin` bootstrap
- [ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md):
  management tokens are issued by the control-plane cell and validated in every cell
- [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md): the
  platform is a global directory over cells, with the control plane in one cell
- [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md): a route authorizes
  at the node it names
- [ADR-1426](1426-a-team-can-nest-beneath-a-parent-team-and-its-subtree-is-walked-depth-first.md): nested teams
- [ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md): machine clients
- [ADR-1512](1512-an-instance-is-set-up-once-through-a-one-time-setup-token-that-creates-a-root-global-administrator.md): the
  root administrator that setup creates
