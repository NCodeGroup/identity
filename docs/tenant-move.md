# Tenant move runbook

- **Status:** Living runbook — update it in the same change as anything it describes.
- **Audience:** an operator who moves a tenant from one cell of a platform to another cell of the same region
  ([ADR-1514](decisions/1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md)).
- **Scope:** the whole move: the four calls that the management console runs for you (transfer key, export, import,
  release) and the three steps around them that only the operator does (disable the tenant, point its hostname at the
  target cell, enable it). The platform's cells and their machine clients are
  [Run the cells of a platform](platform-bootstrap.md#9-run-the-cells-of-a-platform).

A tenant keeps its identifier and its issuer when it moves, so its clients, its relying parties and the tokens it issued
are unaffected. Only the cell that serves it changes. A tenant lives in exactly one cell, and its data stays in that
cell's region, so a tenant never moves across regions: create a tenant in the other region instead.

| Placeholder    | Meaning                                                                      |
| -------------- | ---------------------------------------------------------------------------- |
| `<tenant>`     | The identifier of the tenant that moves                                      |
| `<source>`     | The name (`Cell:Name`) of the cell that holds the tenant                     |
| `<target>`     | The name of the cell that receives the tenant                                |
| `<source-api>` | The management API of `<source>`, at its `Cell:ManagementUrl`                |
| `<target-api>` | The management API of `<target>`, at its `Cell:ManagementUrl`                |

The management console reaches a cell's management API at `/management/cells/<cell>/api/…`, and the routes below are
relative to the API of the cell that each stage names.

## 0. Before you start

- **Who may move a tenant.** The routes that disable, export, import and release a tenant need the update operation at
  the platform node, which a platform administrator (the `GlobalAdmin` role) holds. A team administrator or a tenant
  administrator cannot move a tenant, and the console shows the Move action to platform administrators only. The caller
  needs that authority in **both** cells. A cell other than the control-plane cell asks the control-plane cell, so one
  platform administrator holds it everywhere, and such a cell answers `503` while it cannot reach the control plane.
- **Both cells are in one region.** `<source>` and `<target>` have the same `Cell:Region`. The import refuses a package
  from another region with `422`, and the directory refuses a move between cells of different regions with `409`.
- **A tenant the deployment does not require.** `<source>`'s root tenant, which under the static strategy is its one
  tenant, never moves: the export and the release refuse it with `409`, and the directory refuses an offer or a move of
  the control-plane cell's root tenant with `409`.
- **A directory that knows both cells.** The move changes the tenant's placement in the global directory, so both cells
  reach the same directory (`Directory:Url`) and are registered in it. A platform of one cell has no other cell to move
  a tenant to: the local directory adapter answers every move with a conflict.
- **A tenant within a move's ceiling.** A tenant travels as one package of at most 64 MiB, its local accounts included,
  and the import admits a request body of at most 64 MiB plus 64 KiB. The export refuses a larger tenant with `422`, and
  a proxy in front of `<target>`'s management API admits a request body of that size.
- **The target can use and retag the tenant's keys.** A tenant's remote keys stay in the key management service: the
  import tags each with `<target>`'s name, and an import that `<target>` undoes tags each back with `<source>`'s name.
  Each key lies where `<target>`'s sweep lists keys, in `<target>`'s AWS account and region, its Key Vault or its Cloud
  KMS key ring, and the import refuses a key outside `<target>`'s region, vault or key ring with `422`, so the cells of
  a region that move tenants share that location. On each key, `<target>`'s identity needs the key's own operation, as
  for a key it binds, and the right to tag the key with either cell's name: on AWS `kms:TagResource` whose
  `aws:RequestTag/ncode-identity:deployment` condition lists the names of the region's cells, on Azure
  `Microsoft.KeyVault/vaults/keys/read` and `Microsoft.KeyVault/vaults/keys/update/action` on the region's vault, and on
  Google `cloudkms.cryptoKeys.get` and `cloudkms.cryptoKeys.update` on the region's key ring ([scoping the
  identity](capabilities/remote-keys.md#permissions)). A root key that the tenant brings passes `<target>`'s root-key
  checks, an allowed provider and a key in `<target>`'s cloud regions among them, or the import answers `422`.
- **A window with the tenant unavailable.** The tenant serves no request from the disable until it is enabled in
  `<target>`. Choose a time for it, and have the DNS change ready.

## 1. Disable the tenant

Run at `<source>`: `PATCH <source-api>/api/tenants/<tenant>` with the JSON Patch operation `replace` of `/isDisabled`
by `true`. The tenant stops serving requests, and nothing about it changes while it moves. The export refuses a tenant
that is enabled with `409`.

**Verify:** `GET <source-api>/api/tenants/<tenant>` shows `isDisabled` `true`. The console enables its Move action for
the tenant at this point.

## 2. Issue a transfer key at the target

Run at `<target>`: `POST <target-api>/api/tenant-imports/transfer-keys`. The response holds a `kid` and the public JSON
Web Key `jwk` of a one-time key: an elliptic curve key on `P-256` with `use` `enc` and `alg` `ECDH-ES+A256KW`. The cell
keeps the private half for one hour, and the import takes it once, so one package opens at most once.

**Verify:** the response is `200` with `kid` and `jwk`.

## 3. Export the tenant from the source

Run at `<source>`: `POST <source-api>/api/tenants/<tenant>/export` with the body
`{ "jwk": <the transfer key's jwk>, "targetCell": "<target>" }`. The export records a move offer from `<source>` to
`<target>` in the directory, valid for the transfer key's hour. The response `package` is one compact JWE to the key. It
holds the cell's region, the target cell's name, the tenant's identifier, the tenant's records and the tenant's master
keys sealed to the same key. The audit record `tenant.exported` records the export.

| Status | Meaning                                                                                                              |
| ------ | -------------------------------------------------------------------------------------------------------------------- |
| `200`  | The package.                                                                                                         |
| `404`  | The tenant does not exist in `<source>`.                                                                             |
| `409`  | The tenant is `<source>`'s root tenant, which its deployment requires, or it is not disabled, or the directory refuses the move offer (the tenant is not placed in `<source>`, a cell is not registered, the cells are in different regions, or the tenant is the control-plane cell's root tenant). When the directory places the tenant in another cell, the problem's `placedCell` names that cell: a move there completed, and only the release (stage 5) remains. |
| `422`  | The key is not a `JWK` of an elliptic curve key on `P-256` for encryption, or it is refused; `targetCell` is not a valid cell name; or the tenant's package is over a move's 64 MiB, and the move offer that the export recorded expires unused. |
| `503`  | The directory is unavailable, with a `Retry-After` header. No package is made.                                       |

The package names its target cell, and only that cell imports it. Only the holder of the transfer key's private half,
which is `<target>`, opens the package. It still holds password hashes and protected secrets, so keep it out of logs,
shell history and files, and hand it straight to the import.

## 4. Import the tenant into the target

Run at `<target>`: `POST <target-api>/api/tenant-imports` with the body `{ "kid": <the transfer key's kid>, "package":
<the package> }`. The import takes the transfer key, checks that the package's header names `ECDH-ES+A256KW` and
`A256GCM`, decrypts it, imports each section of the tenant's records, stores the master keys under `<target>`'s own root
keys, points the remote keys that the tenant's secrets reference at `<target>`, and asks the directory to move the
tenant's placement from `<source>` to `<target>`, which it does only for `<target>` calling on the unexpired move offer
that the export recorded (any other caller gets `403`). The directory can commit a move that it does not confirm, so
when the move answers a conflict, cannot be reached, is cancelled or fails, the import reads the placement back: a
placement in `<target>` completes the import, a placement in `<source>` or in no cell undoes it, and a placement in
another cell, or a directory that cannot be read, leaves the outcome unknown. The tenant stays disabled in `<target>`.
The audit record `tenant.imported` names the source cell.

| Status | Meaning                                                                                                                                                                                                                                                                                                         |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `200`  | The tenant is imported and placed in `<target>`, also when the directory committed a move that it did not confirm. |
| `409`  | The directory places the tenant in no cell or already in `<target>`; or the directory refuses the move (`<source>` holds no unexpired move offer to `<target>`, a cell is not registered, or the cells are in different regions) and places the tenant in `<source>` or in no cell. Nothing the import wrote remains. |
| `413`  | The request is larger than a move admits, 64 MiB plus 64 KiB. Nothing is imported. |
| `422`  | The `kid` is empty, or the transfer key is unknown, expired or already used; the package is not sealed to a transfer key (its header does not name `ECDH-ES+A256KW` and `A256GCM`), does not decrypt with the key or is malformed; its region is not `<target>`'s region; it names a target cell other than `<target>`; it has a section that `<target>` does not import; the root key that the tenant brings fails `<target>`'s root-key checks; or the tenant's secrets reference a remote key of a provider that `<target>` does not register, or one outside the location that `<target>`'s provider uses. Nothing the import wrote remains. |
| `503`  | The directory is unavailable, with a `Retry-After` header, before the import writes anything, or at a move after which the directory places the tenant in `<source>` or in no cell; nothing the import wrote remains. Or the outcome of the move is unknown, also with a `Retry-After` header: the directory cannot be read back or places the tenant in a cell other than `<source>` and `<target>`, and `<target>` keeps what the import wrote; the problem's `moveOutcome` member is then `unknown` ([recovering](#recovering-from-a-failed-stage)). |

**Verify:** `GET <target-api>/api/tenants/<tenant>` answers `200` with `isDisabled` `true`.

## 5. Release the tenant at the source

Run at `<source>`: `POST <source-api>/api/tenants/<tenant>/release-moved`. The route answers `409` for `<source>`'s root
tenant, which its deployment requires, and otherwise proceeds only when the directory places the tenant in another cell.
It removes the tenant's records and master keys from `<source>`. It destroys no remote key, because `<target>` holds the
tenant's keys, and it retires no identifier or issuer, because the tenant lives on. The audit record `tenant.released`
names the target cell.

| Status | Meaning                                                                                                                            |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| `204`  | The tenant is released from `<source>`.                                                                                            |
| `404`  | The tenant does not exist in `<source>`.                                                                                           |
| `409`  | The tenant is `<source>`'s root tenant, which its deployment requires, or the directory does not place the tenant in another cell. |
| `503`  | The directory is unavailable, with a `Retry-After` header. Nothing is released.                                                    |

The route never releases on a guess: it reads the placement from the directory each time. While the directory places the
tenant in another cell, deleting the tenant or one of its secrets at `<source>` answers `409` and names that cell, so
the release, which destroys no remote key, is the only way to remove `<source>`'s copy.

**Verify:** `GET <source-api>/api/tenants/<tenant>` answers `404`.

## 6. Point the tenant's hostname at the target

Manual because DNS is written outside the product. The tenant keeps its issuer, which is its single origin, so its
hostname has to resolve to `<target>`'s ingress. Write an explicit DNS record for the tenant's hostname,
`<tenant>.auth.<region>.ncodeid.net` on the hosted platform, and for the tenant's mTLS alias,
`<tenant>.mtls.<region>.ncodeid.net`, when the tenant publishes one. The record names `<target>`'s ingress and takes
precedence over the region's wildcard record. On the NCode hosted platform the OpenTofu pipeline of the infrastructure
repository is the only writer of DNS ([hosting analysis](hosting-analysis.md#domains-and-dns),
[#755](https://github.com/NCodeGroup/identity/issues/755)). `<target>`'s ingress has to serve a certificate for the
hostname, as it does for the other tenants of its region.

**Verify:** the hostname resolves to `<target>`'s ingress from outside the platform. A resolver that caches the
hostname's cached answer reaches `<source>` until the record's TTL ends, and `<source>` holds no such tenant.

## 7. Enable the tenant in the target

Run at `<target>`: `PATCH <target-api>/api/tenants/<tenant>` with the JSON Patch operation `replace` of `/isDisabled`
by `false`.

**Verify:** the discovery document at the tenant's issuer answers from `<target>`, and a client of the tenant obtains a
token with its secret.

## What moves and what stays

| Carried to the target                                                                                                    | Why it is safe                                                                                   |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| The tenant record and its settings; the issuer is unchanged                                                              | The tenant keeps its identifier and its issuer.                                                  |
| Its clients with their secrets, its client grants, its resource servers with their scopes and secrets                   | Secret values travel as protected envelopes.                                                     |
| The tenant's own secrets and its tenant-level role assignments                                                          | The same.                                                                                        |
| Its local accounts, with their claims, password hashes and security stamps                                              | The section is sensitive and travels only inside the encrypted package.                          |
| Its master keys                                                                                                          | Sealed to the transfer key, then stored under `<target>`'s nearest root key (the tenant's own when it carries one). |
| Its remote keys in a key management service                                                                              | They stay where they are, in the location that `<target>`'s provider uses, and carry `<target>`'s tag, so `<source>`'s key sweep skips them. |

| Not carried                                                                | What follows                                                                                  |
| -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Authorization grants, consents and sessions, federated principals and identities | They end. Each person signs in again, and refresh tokens and authorization codes do not survive the move. |
| Audit records and usage records                                            | They stay in `<source>`, in its region, until their retention ends. `<target>` starts its own. |

The directory keeps the tenant's identifier and issuer reserved throughout, so no other tenant takes them. Its
placement changes from `<source>` to `<target>` at the import. The audit records of the move are `tenant.exported`
and `tenant.released` in `<source>` and `tenant.imported` in `<target>` ([audit records](audit-records.md)).

## Recovering from a failed stage

Each stage is safe to repeat until the release. The console runs the four calls in order, stops at the first failure,
and names the stage that failed, why it failed and the stages that remain. Closing the dialog keeps a failed move:
reopening it shows the failure and its Retry, which runs the move again from the stage that failed, or from the transfer
key after a failed import, because the import spends it. When the export answers `409` with `placedCell`, the console
says that the directory already places the tenant in that cell and offers Release, which runs only the release at
`<source>`. Closing the dialog after a completed move opens the tenant in the cell it moved to. When the import answers
that the outcome of the move is unknown, the console says so instead of saying where the tenant is, and its Retry runs
the move again from the transfer key: an export that then finds the tenant placed in `<target>` offers Release.

- **Before the import** (stages 1 to 3). Nothing is placed or written in `<target>`. Repeat the stage. The disable is
  idempotent. An export has no effect on the tenant, so repeating it only writes another `tenant.exported` record. A
  transfer key that has expired, been used or been refused needs another one from stage 2, and then another export,
  because a package opens only with the key it was sealed to.
- **The import is refused or fails** (`409`, `413`, `422`, `503`, or an error). Unless it answers that the outcome of
  the move is unknown, the import removes what it wrote: each section's records and the tenant's master keys in
  `<target>`, and it puts the remote keys it retagged back under `<source>`'s tag. Such an import leaves `<target>`
  without the tenant, the directory placement in `<source>` and the tenant disabled in `<source>`. The transfer key is
  taken before the import reads the package, so a failed import spends it: fix the cause, then repeat from stage 2
  (another transfer key, another export, the import). To abandon the move, enable the tenant in `<source>`.
  - `409` because the directory places the tenant in `<target>`: an import of it already completed. Release at
    `<source>` instead. A tenant that `<target>` holds while the directory places it in `<source>` is the residue of an
    import that stopped before its move, and the next import removes those rows before it imports.
  - `409` from the directory: register both cells in the directory, or choose a `<target>` of the tenant's region.
  - `409` because the directory places the tenant in no cell, before the import or after its move: the tenant was
    deleted and its identifier retired while it moved, so the move has nothing to finish and `<target>` holds no copy.
  - `413` for the ceiling: the tenant's package is over a move's 64 MiB, which the export also refuses with `422`, and
    the tenant cannot move until it is smaller.
  - `422` for a region: choose a `<target>` of the package's region.
  - `422` for a root key or a remote key: give `<target>` the provider, the location or the cloud region that the key
    needs, or choose another `<target>` ([before you start](#0-before-you-start)).
  - `503` that says the directory is unavailable: restore the directory, then repeat from stage 2.
  - `503` that says the outcome of the move is unknown: `<target>` keeps what it imported, and the remote keys keep
    `<target>`'s tag. Finish the move instead of abandoning it: repeat from stage 2. When the directory moved the
    tenant, the export answers `409` whose `placedCell` names `<target>`, and only the release (stage 5) remains.
    Otherwise the export and the import run again, and the import clears `<target>`'s copy before it imports. The
    release at `<source>` reads the placement itself, so it never removes the last copy.
- **The import succeeded and the release did not** (`503`, `409`, an error, or a lost connection). The tenant is in both
  cells and the directory names `<target>`. `<source>`'s copy stays disabled, so nothing serves it. Repeat the release
  until it answers `204`. A `503` means the directory is unreachable, so nothing was released. A `409` means the
  directory does not name another cell, so check the placement before anything else. A partial release is repeated the
  same way, and a repeat after `204` answers `404`. Deleting `<source>`'s copy instead answers `409` and names
  `<target>`, and deletes nothing.
- **DNS and the enable** (stages 6 and 7) are repeatable. A tenant that does not serve from `<target>` after the enable
  is disabled again with stage 1's call at `<target>`, while the hostname and the cell's logs are checked.

## References

- [ADR-1514](decisions/1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md): the
  decision, the options and the consequences.
- [Run the cells of a platform](platform-bootstrap.md#9-run-the-cells-of-a-platform) and
  [cells and the global directory](hosting-analysis.md#cells-and-the-global-directory).
- [Domains and DNS](hosting-analysis.md#domains-and-dns): the tenant hostnames.
- [Audit records](audit-records.md): the events of a move.
