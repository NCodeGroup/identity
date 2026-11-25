# 1465. Role lists share one audited grant and revoke flow, and a caller discovers its own assignments

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** NCode Group

## Context

Management authority is a role assignment at a node of the platform, team and tenant hierarchy
([ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md)).
The management console lists who holds a role at a node and offers to grant and revoke it, and it must show a caller
the places where the caller holds a role. The forces:

- Four kinds of node keep an owners list and two keep an administrators list. Handlers that each repeat the grant and
  revoke logic diverge in their statuses, their audit records and their refusals.
- Whether the caller may grant or revoke depends on the caller. Owners of a node are changed with `Update` at the
  node, administrators with `Create`, and the owners of a client are also refused for a client the host manages and for
  a caller without custody of a machine client. A console that offers grant and revoke to every caller learns that the
  caller may not use them only from the `403`.
- Authority is granted and removed by assignments, so the changes belong in the audit trail, whichever route or
  handler makes them.
- A caller holds roles at many nodes, and the token names no team or tenant. The console needs a way to ask which
  teams and tenants the caller reaches.
- Computing any of this in the browser would duplicate the server's rules and drift from them.
- A role list names its principals by opaque identifier. The principals that hold management roles are control-plane
  principals, whose local accounts and machine clients live in the control-plane cell, while a tenant's list may be
  served by another cell. An administrator grants and revokes by name, so every holder needs a name that the serving
  cell cannot read from its own tables.

## Decision

**Every role list uses the same role-neutral contracts and one grant and revoke flow; each grant and revoke is
audited; and a caller reads its own assignments, teams and tenants from `GET /api/me`.**

### Routes and contracts

1. **The role lists are `…/owners` on a team, tenant, client and resource server and `…/admins` on a team, a tenant
   and a cell** (the cell's is `/api/cell/admins`, see ADR-1536). `GET` reads the list, `POST` grants and `DELETE
   …/{principalId}` revokes.
2. **The contracts name no role and no owner.** `GET` answers `RoleListResource { items, canChange }`. An item, and the
   answer to a grant, is `RoleHolderResource { assignmentId, principalId, roleName, principal }`, where `principal` is a
   `PrincipalSummaryResource { principalId, principalType, displayName, userName }`, or null for an assignment whose
   principal no longer exists or that the serving cell's directory cannot describe (decision 12). A grant's body is
   `AddRoleHolderRequest`, which names exactly one of `principalId`, the one on the resource of the local account or
   client to grant (`LocalAccountResource`, `ClientResource`), and `userName`. `RoleAssignmentResource`, which names its
   node, serves the assignments that are read across nodes.
3. **`canChange` says whether the caller's grant or revoke of the role at the node passes every refusal that does not
   depend on the principal named.** Those refusals are `Update` at the node for owners, `Create` for the administrators
   of a team and a tenant, `Update` at the cell's node for a cell's administrators (a `CellAdmin` creates nothing, so
   its holders manage their own list as team and tenant administrators do with `Create`), and, for a client's owners, a
   client the host does not manage and custody of a machine client
   ([ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md)). The
   last-owner rule depends on the principal, so it stays a `409` on the revoke.
4. **A list route authorizes `Read` at the node before it reads** (see
   [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md))
   and answers `404` for a missing resource. It carries no ETag and no continuation token, because the answer is
   specific to the caller and unpaged.

### The grant and revoke flow

5. **One flow grants every role.** A `RoleList` drives it: the node, the role, the operation that authorizes, the list
   path and the check that the resource exists. The flow authorizes, then answers:
   - `400` when the request does not name exactly one principal, names a `userName` that no principal has, or names a
     `principalId` that the control plane does not know, which creates no assignment;
   - `404` for a missing resource;
   - `409` when the ownership service refuses, saving and recording nothing;
   - `200` with the existing assignment when the principal already holds the role, recording nothing;
   - `201` at `<list path>/<principalId>` otherwise, recording the grant.
6. **One flow revokes every role but `Owner`.** It authorizes, answers `404` when the principal does not hold the role,
   and otherwise commits, records each removed assignment and answers `204`. Owners have a flow of their own for the
   `409` that keeps one owner.
7. **A resource's extra refusals run before the shared flow.** A client's owners refuse a host-managed client (`409`)
   and a caller without custody of a machine client (`403`) first.
8. **`IResourceOwnershipService` reports what it changed.** It returns `RoleAssignmentGrant` (`IsAdded`, `IsRefused`),
   the removed assignments and `OwnerRemoval`, so the flow records exactly what happened.

### The holders' names

9. **Names are read through one port.** The port `IControlPlanePrincipalDirectory` answers a
   `ControlPlanePrincipalQuery` of principal ids and user names with `PrincipalSummaryResource` items. Its adapter
   describes the principals from the tables of the cell's own management tenant, the control-plane tenant in the
   control-plane cell, and calls no other cell. A list handler never reads names from another cell's tables. Names match
   as the stores key them, ignoring case: a principal identifier or username that differs from the stored one only in
   case names that principal, and its summary carries the stored casing, which a grant records.
10. **A query is bounded.** `ControlPlanePrincipalQuery` answers `400` when `principalIds` or `userNames` is null or
    holds a null or blank name, and for a query of more than 100 names (`ControlPlanePrincipalQuery.MaxNames`, principal
    ids plus user names).
11. **`IPrincipalDescriber` is a strategy collection** (see
    [ADR-1001](1001-behavior-is-a-service-a-strategy-collection-or-a-mediator-seam-chosen-by-one-ladder.md)): every
    registered describer describes the principals it knows, and the local adapter merges their answers. The
    `local_account` describer names an account through its identity in a local connection, reading the store that the
    connection's options select, and a grant's username names an account of the control-plane tenant's default
    connection; it gives an account's display name and username (which an account may lack), and the `client` describer
    a client's identifier as its display name. Each describer reads only the columns that name a principal, through the
    stores' name projections (`ILocalAccountStore.GetNamesByIdsAsync` and `GetNamesByUserNamesAsync`,
    `IClientStore.GetNamesByPrincipalsAsync`, `IFederatedIdentityStore.GetNamesByPrincipalsAsync`, which the
    `local_account` describer reads), never an account's claims or a client's settings or secrets
    ([ADR-1080](1080-filtered-reads-are-intent-named-store-methods-and-growing-criteria-are-init-only-dtos.md)). A
    package that registers another describer adds a principal type, so `PrincipalTypes` holds an open vocabulary. The
    directory describes every control-plane principal: the registered describers first, then each remaining principal
    that the federated principal store holds as `federated_user`, named by its first federated identity (ordered by
    connection, then subject) as `<subject> (<issuer>)`, or as `<subject>` when the identity has no issuer, or by its
    principal identifier when it has no identity. The fallback reads only which principals exist
    (`IFederatedPrincipalStore.GetExistingIdsAsync`) and the names of their identities
    (`IFederatedIdentityStore.GetNamesByPrincipalsAsync`). Only an assignment whose principal no longer exists, or that
    the serving cell's directory cannot describe (decision 12), has `principal: null` on its item and on the answer to
    its grant.
12. **A grant fails closed.** A `userName` resolves through the directory after the flow authorizes and the resource
    exists, and a name or a `principalId` that the control plane does not know answers `400` without echoing it, so a
    mistyped identifier never creates an assignment that no principal holds, and only a caller with grant authority at a
    node can probe a username. The exception is a directory that does not describe every control-plane principal, as
    a workload cell's does not (`IControlPlanePrincipalDirectory.DescribesEveryPrincipal`): it grants a `principalId`
    it cannot describe without an existence check, and a `userName` it cannot resolve still answers `400`. A control
    plane that fails or is unreachable is a `DirectoryUnavailableException`, which the management endpoints answer
    `503`, for a list and for a grant, and a name is never guessed.

### Audit

13. **Every grant and revoke records a `RoleAssignmentChangedAuditEvent`** (see
    [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md)). Its resource type is
    `role_assignment` (`ManagementResourceTypes.RoleAssignment`), its change type `created` or `deleted` and its
    resource id the assignment id. Its resources are the assignment, then its node (`NodeType`, `NodeId`). Its
    `subject_id` is the principal, and its `resource_values` are `assignmentId`, `principalId` and `roleName`; the
    holder's summary is not recorded.
14. **The event's tenant is the assignment's**, the control-plane tenant for a team node, and its actor is the acting
    administrator or client.
15. **The endpoint handler records the event after the commit**, for the owners and admins routes, the creator's
    `Owner` assignment and a new team's `TeamAdmin` assignment. Assignments removed with their node are covered by the
    node's own `deleted` event.

### The caller's own assignments

16. **`GET /api/me` returns `CallerResource`.** `principalId` is null when none resolves, `isPlatformAdministrator` is
    true when the caller holds `Operations.Read` at the platform node, and `assignments` are
    `RoleAssignmentResource` items. How the endpoint authenticates the caller is
    [ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md)'s.
17. **`GET /api/me/teams` and `GET /api/me/tenants`** each return one `CollectionResource` page of the teams or tenants
    where the caller holds any assignment, ordered by identifier. The tenants are returned without `environment`.
18. **Authority stays per request.** The token names no team or tenant, so one token spans every place the caller
    holds a role, and a grant or a revocation applies on the next request.

### The console

19. **The console shows grant and revoke only when `canChange` is true**, and refetches every role list after any role
    change. It shows each holder by its display name and username, or by the principal id when `principal` is null, and
    its grant form takes a username or a principal id.
20. **The console opens on `/home`, and its tenant lists tell the cells apart.** `/home` lists "Open the platform" for
    a platform administrator and the caller's teams and tenants, names each cell that did not answer, and opens a sole
    destination directly, replacing the history entry, only when every cell answered. `/home` is not a management
    scope. Two cells list one tenant while it moves between them
    ([ADR-1514](1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md)), so the
    home, the team's tenant list and the promotion dialog key each row and option by its cell and its tenant
    identifier, and a row's Open, Edit and Delete act on the copy in its own cell. The tenant page's Move keeps a
    failed move when its dialog closes, offers Release, which runs only the release, when the export's `409` names in
    `placedCell` the cell that the global directory already places the tenant in, says that the outcome is unknown
    when the import's `503` carries `moveOutcome` `unknown`, says when the list of cells is loading or could not be
    loaded, and opens the tenant in the cell it moved to once the move completes.
21. **A header Home control returns to `/home`.** A platform route becomes `/home` once the caller is known to lack
    platform authority. Back controls lead to the parent the caller can reach: the platform's team list for a platform
    administrator, a tenant's team for a platform administrator or a role holder there, and otherwise Home. While the
    caller's access is unknown, a back control leads where the route's parent leads and a platform route stays.

## Options considered

- **A sibling `…/owners/authority` resource.** The console would make a second request per list and the two answers
  could disagree. Rejected.
- **Separate or per-assignment grant and revoke flags.** The refusals that do not depend on the principal are the same
  for every item, so one flag on the list says it all. Rejected.
- **Computing authority in the browser.** It repeats the server's rules and drifts from them. Rejected.
- **Owner-named or per-role contracts, or `RoleAssignmentResource` items in a role list.** Each new role would add
  contracts, and a list at one node has no use for the node on every item. Rejected.
- **A console-side principal lookup endpoint.** The console would make a second request, the endpoint would need an
  authorization of its own, and it would disclose names outside a role list. Rejected.
- **Names from the serving cell's own tables.** The control-plane principals are not in a tenant's cell. Rejected.
- **A flow per handler.** The statuses, refusals and audit records would diverge. Rejected.
- **Auditing in the store, or a per-node `role.granted` change type.** The store does not know the actor, and a change
  type per node multiplies the vocabulary of one fact. Rejected.
- **A team claim, token or audience.** A token would then span one team, and a caller with roles in several would need
  one per place, with a stale claim after every grant or revocation. Rejected.
- **Probing endpoints from the console to find where the caller may act.** It costs a request per place and shows a
  `403` as a failure. Rejected.
- **Chosen:** role-neutral contracts, one grant and revoke flow, `canChange` on the list, holders named from the
  control plane in the same answer, an audit event recorded after the commit, and `GET /api/me` for the caller's
  assignments.

## Consequences

- A new role list is a `RoleList` descriptor and a route, with no new contracts, statuses or audit code.
- The console never shows an action the caller's `403` would refuse, except where the principal named decides, as the
  last-owner `409` does.
- Every change to who holds authority has an audit record with its node and its principal, whichever route made it.
- A role list names every holder from one request, whichever cell serves it, and a grant names its principal by
  username or identifier. A control plane that cannot answer makes the list and the grant answer `503`.
- A caller sees every team and tenant where it holds a role from one token, and a grant or revocation shows on its next
  request.
- The `canChange` answer can disagree with a later request only when the caller's authority changes between them, and
  the request is then refused as usual.
- Any role change invalidates every role list the console holds, because the caller's authority over any list can change.

## References

- Issues [#566](https://github.com/NCodeGroup/identity/issues/566),
  [#583](https://github.com/NCodeGroup/identity/issues/583),
  [#623](https://github.com/NCodeGroup/identity/issues/623),
  [#627](https://github.com/NCodeGroup/identity/issues/627),
  [#628](https://github.com/NCodeGroup/identity/issues/628) and
  [#934](https://github.com/NCodeGroup/identity/issues/934)
- [ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md):
  management authority is a role assignment at a node.
- [ADR-1418](1418-limits-are-set-by-the-authority-above-their-scope-and-readers-learn-which-they-may-change.md): a
  reader learns which limits it may change.
- [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md):
  authorization at the node before the read.
- [ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md): custody of a machine
  client.
- [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md): the audit record shape.
- [ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md):
  how the management API authenticates a caller.
