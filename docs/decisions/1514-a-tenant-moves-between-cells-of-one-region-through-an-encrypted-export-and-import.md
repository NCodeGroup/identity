# 1514. A tenant moves between cells of one region through an encrypted export and import

- **Status:** Accepted
- **Date:** 2026-11-13
- **Deciders:** NCode Group

## Context

A tenant lives in exactly one cell, and its data stays in that cell's region
([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
[ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).
An operator still has reasons to place a tenant in another cell of the same region: a customer outgrows a shared cell
and takes a dedicated one, or a cell is drained for maintenance. The tenant keeps its identifier and its issuer, so its
clients, its relying parties and its tokens are unaffected
([ADR-1160](1160-a-tenant-is-named-by-its-creator-validated-by-its-strategy-and-never-reissued.md),
[ADR-1165](1165-a-tenants-stored-issuer-is-its-single-origin-and-its-mtls-alias-a-routed-second-host.md)).

Four facts shape how a tenant travels:

- **The cells share nothing but the directory.** Each cell has its own database, its own Valkey and its own root keys,
  so a tenant's rows cannot be copied between databases by a foreign key, a shared table or a database link.
- **A tenant's secrets are ciphertext under its cell's keys.** Stored secrets and grant payloads are protected by the
  tenant's master keys, which are wrapped under the nearest root key of the cell
  ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md),
  [ADR-1414](1414-a-tenant-can-wrap-its-master-keys-under-a-root-key-it-controls.md)). The target cell cannot read them
  until it holds the master keys, and those keys must never cross the network in the clear.
- **A tenant's remote keys stay in the key management service.** The cell's sweep destroys a key that no stored secret
  names and that carries its own cell's tag
  ([ADR-1478](1478-a-background-job-destroys-the-key-management-service-keys-that-no-stored-secret-names.md)), so a
  moved tenant's keys must carry the target cell's tag before the source's sweep meets them.
- **The directory is the one place that says where a tenant lives.** The identifier is reserved platform-wide, so a move
  changes a placement, and neither deletes nor reissues anything.

## Decision

**A tenant moves between cells of one region through an encrypted export from the source cell and an import into the
target cell, in seven steps. The tenant keeps its identifier and its issuer, and a move across regions is refused: a new
tenant is created there instead.**

1. **The tenant is disabled (`IsDisabled`), and its cell's deployment does not require it.** The export answers `409`
   for a tenant that is enabled, so nothing changes while the tenant travels, and for a tenant the deployment requires
   (`ITenantResolver.IsRequiredTenant`: the root tenant, which under the static strategy is its tenant), which never
   leaves its cell, because it is part of its cell's deployment, which creates it on demand. The control-plane cell's
   root tenant is also the tenant the directory places in that cell from its own configuration
   (`DirectoryServer:ControlPlaneTenantId`) and whose tokens are the only ones it accepts, and that cell creates, judges
   and keys it without the directory, so the directory refuses to offer or move it as well.
2. **The target publishes a transfer key.** `ITenantTransferKeyService` makes an EC P-256 key with a random `kid`, `use`
   `enc` and `alg` `ECDH-ES+A256KW`, keeps its private half protected by Data Protection in the shared expiring value
   set for one hour, and returns the public JWK. `POST api/tenant-imports/transfer-keys` serves it. Taking the key
   returns it once and removes it, so a package is opened at most once, and an expired or taken key is gone.
3. **The source exports the tenant's graph.** `POST api/tenants/{tenantId}/export` takes the transfer key's public JWK
   and answers one compact JWE to it (`ECDH-ES+A256KW`, `A256GCM`) of the package's format version, the tenant's region,
   its identifier, its sections and its master keys. The package carries a format version, a cell imports only its own
   format version, and a section's shape changes only with a new format version. The sections come from the
   `ITenantTransferContributor` strategy collection in `Order`: the core contributor carries the tenant row and its
   settings, its clients with their secrets, its client grants, its resource servers with their scopes and secrets, its
   own secrets and its tenant-level role assignments; the local accounts contributor carries each local account, its
   claims and its password hash, through `ILocalAccountTransferStore`; `LocalAccountTransferRecord` carries the hash on
   the public surface because the stores that implement the seam and the contributor that reads it are separate
   packages, and it is a plain class whose `ToString()` writes none of it. Secret values stay as their protected
   envelopes.
   `IMasterKeyTransfer` seals the owner's master keys to the same recipient key, with the owner's root key reference
   when the tenant holds its own. A package is at most 64 MiB (`TenantTransferLimits.MaxPackageBytes`), the tenant's
   local accounts included, and the export answers `422` for a larger one, so a move's memory is bounded in both cells;
   the move offer recorded for it expires unused. The route is authorized with `Operations.Update` at the platform node,
   writes the audit record `tenant.exported` and never logs its response.
4. **The target imports the graph and rewraps the master key under its root.** `POST api/tenant-imports` takes the `kid`
   and the package in a request body of at most 64 MiB plus 64 KiB (`413` above it). It answers `422` for an empty
   `kid`, takes the private key once (`422` when it is gone), and answers `422` before it decrypts a package whose
   protected header does not name `ECDH-ES+A256KW` and `A256GCM`, so a package sealed to another kind of key is refused
   before any key decrypts it. It answers `422` when the package's format version is not the target's or its region is
   not the target's `Cell:Region`, and `409` when the directory places the tenant in the cell. A tenant that the cell
   holds while the directory places it in another cell is the residue of an import that stopped before its move, and the
   import removes those rows before it imports. It imports every section in `Order`, each committing on its own, then
   the master keys, which `IMasterKeyTransfer` stores under the target's nearest root key, the tenant's own when it
   carries one. The tenant's own root key passes the checks that `IOwnerRootKeyManager.SetAsync` applies, the owner's
   policies and an allowed key wrapper with a key reference and an access identity that fit it, the cell's cloud regions
   included, or `IMasterKeyTransfer` stores nothing and the import answers `422`. A remote key that lies outside the
   location that its provider uses in the target cell (`IRemoteKeyProvider.IsInLocation`) answers `422` before any key
   is retagged, because the target's sweep lists only that location. When any part before the move fails, the import
   removes every section it imported and tags the remote keys back to the source cell before it answers. The tenant
   stays disabled, and the audit record `tenant.imported` names the source cell.
5. **Some records travel and others do not.** The package carries the tenant's configuration, its secrets, its local
   accounts and its master keys. Grants, consents, sessions, federated principals and identities are dropped, so each
   person signs in again, and the dropped rows hold only what a sign-in recreates. Audit and usage records stay in the
   source cell, in its region, until their retention ends
   ([ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md)).
6. **The directory placement changes, and the source deletes the tenant without retiring it.**
   - The export takes the target cell's name (`targetCell`) with the transfer key. It records a move offer from the
     source cell to the target cell through `ITenantDirectory.OfferMoveAsync`, valid for the transfer key's hour, and
     writes the target cell's name into the package, so only that cell imports it. A conflict, such as a tenant the
     directory does not place in the source cell, cells of different regions or the control-plane cell's root tenant,
     which the directory never offers, answers `409`; when the directory places the tenant in another cell, the `409`
     names that cell in the problem's `placedCell`, so the caller releases the source's copy instead of exporting it
     again. An import of a package that names another cell answers `422`.
   - **Key custody is fenced locally on each side.** The source sets the tenant's `KeyCustody` to `outgoing`, naming the
     target in `KeyCustodyCell`, before it asks the directory for the offer, and clears it when the offer is refused.
     The target inserts the tenant `incoming`, naming the source, and sets `held` once the directory confirms the move.
     While a tenant is fenced, its cell neither rolls over its keys nor deletes its secrets, and never reads the
     directory to decide that
     ([ADR-1536](1536-a-platforms-global-plane-is-a-standalone-directory-and-a-dedicated-control-plane-cell-and-every-other-cell-runs-and-is-managed-on-its-own.md)).
     Enabling a fenced tenant needs a live read that finds it placed in this cell, which sets `held`: the read answers
     `503` when the directory cannot be reached and `409` when the tenant is placed elsewhere.
     `TenantKeyCustodyReconciler` runs every 15 minutes and sets `held` for an `incoming` tenant that the directory
     places in the cell, which finishes an import whose confirmation was lost.
   - The import calls `ITenantDirectory.MoveAsync` for the tenant from the source cell to the target cell. The directory
     refuses the control-plane cell's root tenant and, in one serializable transaction, requires the placement to name
     the source cell, both cells to be registered in the same region and the source cell's unexpired move offer to the
     target cell, which the move consumes. The route moves the tenant only for the target cell that calls, so a cell
     cannot take a tenant offered to another, and any other caller gets `403`. The directory can commit a move that it
     does not confirm, such as one whose acknowledgement a timeout loses or whose transaction a retry runs again, so the
     import reads the placement back, on a token of its own, whenever the move answers a conflict, cannot be reached, is
     cancelled or fails. A placement in the target cell completes the import. A placement in the source cell or in no
     cell, where the move cannot have committed, makes the import remove what it imported and answer the move's own
     failure. A placement in another cell, or a directory that cannot be read, keeps what the import wrote and the keys'
     tags, and answers `503` that the outcome of the move is unknown: a repeated export then names the target cell in
     `placedCell` when the move committed, and a repeated import otherwise clears the target's copy before it imports.
     The `503` carries the problem member `moveOutcome` with the value `unknown`, so the console says that the outcome
     is unknown instead of saying where the tenant is. The local adapter always answers a conflict, because a single
     cell has no other cell.
   - The remote keys the import found carry the target cell's tag through `IRemoteKeyProvider.RetagKeyAsync`, so the
     target's sweep keeps them and the source's sweep, which lists only keys of its own name, never destroys them. The
     cells of a region that move tenants between them share the location that their providers use, one AWS account and
     region, one Key Vault or one Cloud KMS key ring, and each cell's identity may tag a key with the name of any cell
     of the region, so the target retags a moved tenant's keys and tags them back when it undoes an import.
   - `POST api/tenants/{tenantId}/release-moved` runs at the source cell. It answers `409` for a tenant the deployment
     requires, which never leaves its cell, so a placement of the same identifier in another cell, such as the
     control-plane cell's root tenant `root` beside a cell's own `root` tenant that has no issuer, never removes the
     cell's own; otherwise it proceeds only when the directory's placement names another cell, and answers `503` when
     the directory is unreachable, never releasing on a guess. It calls each contributor's `RemoveAsync` in reverse
     `Order`, then `ITenantStore.RemoveFromCellAsync`, which deletes the tenant's rows and master keys without checking
     dependents, destroying a remote key or retiring the identifier or the issuer, because the tenant lives on in its
     target cell ([ADR-1212](1212-deleting-a-client-resource-server-principal-or-tenant-removes-what-it-owns.md)). The
     audit record `tenant.released` names the target cell. While the directory places the tenant in another cell,
     deleting the tenant or one of its secrets at the source answers `409` naming that cell, and `503` when the
     directory cannot be reached, so the release is the only path that removes the source's copy, and it destroys no
     remote key.
7. **The operator writes the DNS record and enables the tenant.** The tenant's issuer is unchanged, so its hostname
   resolves to the target cell through an explicit DNS record
   ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)),
   which `docs/tenant-move.md` describes. The operator then enables the tenant in the target cell.

The management console runs the four calls in order (transfer key, export, import, release) for an operator who confirms
a move, shows the stage that failed and what remains, keeps a failed move when its dialog closes, offers the release
alone when the export names in `placedCell` the cell that the directory already places the tenant in, and opens the
tenant in the cell it moved to once the move completes.

## Options considered

- **A logical replica of the tenant's rows.** Rejected: the cells share no database, replication is per database and not
  per tenant, and a replica would move the tenant's audit and usage rows, which stay in the source region's records.
- **A database dump of the tenant's rows.** Rejected: the dump holds ciphertext that only the source cell's keys can
  open, so it is useless without the master keys, and shipping those keys in a file puts the tenant's whole protection
  in one artifact. A dump also bypasses the target's validation and its row-level security.
- **An export and import through the management API (chosen).** The package is one JWE to a key that exists for an hour
  and is taken once, so the master keys travel sealed and a captured package cannot be opened twice. The target
  validates each section through its own stores, rewraps the keys under its own root, and the directory arbitrates the
  placement.
- **A live move of an enabled tenant.** Rejected: writes during the export would be lost or need a replication
  protocol. Disabling the tenant for the move is short, explicit and testable.
- **A move across regions.** Rejected: a tenant's data stays in its cell's region, and the platform's hostnames carry
  the region. A new tenant is created in the other region.

## Consequences

- A tenant is unavailable from the disable until the operator enables it in the target cell. The operator schedules the
  move, and the DNS record is part of the move.
- Each person of the tenant signs in again, because sessions and grants are dropped. Refresh tokens and authorization
  codes do not survive the move.
- The tenant's identifier and issuer stay reserved by the directory throughout, and no other tenant can take them.
- A tenant the deployment requires never moves: a cell's root tenant stays in that cell, so a cell under the static
  strategy, whose one tenant is its root tenant, moves no tenant, and the control-plane cell's root tenant stays in the
  control-plane cell.
- The source cell keeps the tenant's audit and usage records until their retention ends, while the target starts its own.
- The operator who moves a tenant holds the `Update` operation at the platform node in both cells, and each cell
  authenticates to the directory as its own machine client.
- The transfer key lives in the target cell's shared expiring value set, so a replica other than the one that issued it
  can take it, and a key that nobody takes expires after an hour.
- The ASP.NET Core Identity adapter moves a tenant's local accounts one account at a time through `UserManager<TUser>`:
  its export reads each account's claims with a call of its own, one query per account over ASP.NET Core Identity's
  Entity Framework store, and its import writes each account twice: the user manager creates it, which issues a new
  security stamp, and one update then writes its security stamp, lockout state and claims. ASP.NET Core Identity's store
  contracts read and write one user at a time, and the adapter goes through `UserManager<TUser>` so that it works over
  any ASP.NET Core Identity store; the Entity Framework local-account store reads a tenant's accounts and their claims
  in a fixed number of queries. Only a move exports and imports a tenant's local accounts, while the tenant is
  disabled, so the cost is accepted.
- A failure after the import and before the release leaves the tenant in both cells with the directory naming the
  target. The release at the source finishes it, and nothing serves the source's copy, because the tenant stays
  disabled there. Neither the source's key rollover nor its seed changes the copy's keys while the directory places the
  tenant in the target, because the tenant's key custody fence is `outgoing` or `incoming` there and refuses the change.
- A tenant moves only while its package is at most 64 MiB, its local accounts included, and a proxy in front of the
  target cell admits a request body of 64 MiB plus 64 KiB. A larger tenant cannot move.
- An import whose move the directory does not confirm can end with the target holding the tenant while the directory
  cannot be read back or places the tenant in a cell other than the source and the target. The target keeps that copy,
  and the tenant's remote keys keep the target's tag, until a repeated import clears it or completes the move, so the
  operator finishes such a move instead of abandoning it.
- The cells of a region that move tenants share their key management location, and each cell's identity can tag the
  others' keys, so each identity reaches the region's keys and not only its own cell's.

## References

- [ADR-1160](1160-a-tenant-is-named-by-its-creator-validated-by-its-strategy-and-never-reissued.md),
  [ADR-1165](1165-a-tenants-stored-issuer-is-its-single-origin-and-its-mtls-alias-a-routed-second-host.md),
  [ADR-1212](1212-deleting-a-client-resource-server-principal-or-tenant-removes-what-it-owns.md),
  [ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md),
  [ADR-1303](1303-audit-records-are-kept-in-an-indexed-store-that-each-replica-writes-in-batches.md),
  [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
  [ADR-1414](1414-a-tenant-can-wrap-its-master-keys-under-a-root-key-it-controls.md),
  [ADR-1478](1478-a-background-job-destroys-the-key-management-service-keys-that-no-stored-secret-names.md),
  [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)
