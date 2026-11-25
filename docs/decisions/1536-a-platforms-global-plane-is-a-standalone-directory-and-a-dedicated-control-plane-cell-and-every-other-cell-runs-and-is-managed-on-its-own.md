# 1536. A platform's global plane is a standalone directory and a dedicated control-plane cell, and every other cell runs and is managed on its own

- **Status:** Accepted
- **Date:** 2026-11-23
- **Deciders:** NCode Group

## Context

A platform is a global directory over independent cells
([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)),
and a cell is the unit of release, restore and failure: its outage leaves every other cell serving
([ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md), point 12). A cell
serves a tenant from its own tenant row and never calls the directory, and it mirrors the team and platform documents
and the team root keys from the directory's change feed
([`DirectoryChangeFeedService`](../../src/NCode.Identity.OpenId.GlobalDirectory/DirectoryChangeFeedService.cs), polled
every `Directory:PollInterval`, 30 seconds by default).

Management and the directory must hold to the same rule. One cell, the control-plane cell (`Cell:IsControlPlane`),
holds the platform and team authority, the platform and team settings, setup and the team root keys (ADR-1411,
decision 4). If it also served customer tenants, or if any other cell called it at run time, it would be a single
point of failure:

- A cell that validates every management token against the control-plane tenant's issuer, fetching that issuer's
  discovery document and key set, or asks the control-plane cell who a caller is at the platform and team nodes, refuses
  management requests with `503` whenever the control-plane cell cannot be reached
  ([ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md),
  decision 2;
  [ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md),
  decision 5).
- A cell that obtains its directory token from the control-plane tenant, and a directory that accepts only that
  tenant's tokens
  ([`DirectoryAuthenticationGate`](../../src/NCode.Identity.OpenId.GlobalDirectory.Server/DirectoryAuthenticationGate.cs)),
  leaves no cell able to reach the directory while the control-plane cell is down, which stops the creation, deletion
  and move of tenants and teams and the team feed platform-wide.
- A directory that runs from a cell's own Helm release fails and rolls back with that release, because they are one
  release.
- The management console is a client of the control-plane tenant and forwards that tenant's token to every cell
  ([ADR-1437](1437-the-management-console-is-a-confidential-client-whose-own-bff-host-serves-only-the-console.md),
  decision 6).

An outage, a bad release, a restore or an overload of one cell must therefore never refuse management in another cell or
stop the directory.

The forces:

- **A cell fails alone, management included.** An administrator of a tenant should be able to manage it while any other
  cell is down, as its users can sign in while any other cell is down.
- **Some state is global and needs one owner.** Tenant and team identifiers are unique platform-wide and never reissued,
  a tenant is placed in one cell, teams span cells, and the platform has one settings root and one set of platform
  administrators. Something must own them and authorize changes to them.
- **The owner of global administration must not depend on what it administers.** Whoever authorizes a change to the
  global namespace is itself registered in it, so its trust has to be anchored outside the namespace or the platform
  cannot start or recover.
- **Static stability.** A cell keeps doing everything it already does when the global services are unreachable; only
  operations that change global state stop.
- **Revocation.** A replicated authority lags its source, and a cell that answers from a copy keeps a revoked role until
  the copy catches up. That lag has to be bounded and the emergency path explicit.
- **A cell trusts no other cell.** Cells that are released and operated on their own make a compromised workload cell a
  realistic threat, so nothing a cell receives may let it act at another cell, the control-plane cell included.
- **The order of publication is the order of the source.** A cell's database and the directory cannot commit together,
  so a change to replicated authority has to reach the directory exactly when it commits, survive a crash between the
  two, and never be overwritten there by an older copy.
- **A platform starts from nothing.** The first request the directory serves comes from the control-plane cell, which
  holds no directory token yet, so the keys that request is verified with exist before either release starts.
- **One cell needs no extra service.** A single deployment with local directory adapters is a platform of one cell
  (ADR-1411, decision 3), and it stays that.
- **Standards.** Cells authenticate to the directory with OAuth 2.0
  ([RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)) and JWT client authentication
  ([RFC 7523](https://datatracker.ietf.org/doc/html/rfc7523)), under the current OAuth security best practice
  ([RFC 9700](https://datatracker.ietf.org/doc/html/rfc9700)).

## Decision

**A platform of more than one cell has a global plane of two isolated parts: the global directory, deployed on its own,
and a dedicated control-plane cell that holds only the control-plane tenant. Every other cell serves, authenticates its
management callers and authorizes them from its own data, and depends on no other cell at run time. The control-plane
tenant is the platform's one trust anchor: the directory pins it in its configuration, and every cell verifies its
tokens with keys and authority replicated through the directory's change feed.**

1. **The global plane.** The global directory and the control-plane cell form the global plane. Neither shares a
   release, compute, a database or a Valkey with a cell that serves customer tenants, and each is released, scaled and
   restored on its own. A platform of one cell, with the local directory adapters (no `Directory:Url`), has no global
   plane: its one cell serves the control-plane tenant and every other tenant, as ADR-1411 decision 3 describes, and
   nothing in this record changes it. One seam, `IPlatformTopology`, answers `HasDirectory`, `IsControlPlaneCell` and
   `HasManagementTenant`: the host registers the local answer (no directory), the directory client replaces it when
   `Directory:Url` is set, and every rule that holds only on a platform with a directory (DPoP-only management tokens,
   the dedicated control-plane cell, the outbox) reads it.
   The directory client's own HTTP client bounds every call with a `Polly.Core` pipeline, as ADR-1385 decision 2 says: a
   total timeout, then, for a GET or HEAD without a body only, a jittered retry and a per-attempt timeout, so a token
   request and every write are sent once.
2. **The directory is a release of its own.** It is deployed by its own chart, `ncode-identity-directory`, published
   beside `ncode-identity-host`, from the `ncode-identity-directory` image, with its own database, its own route and
   backend TLS, and one replica, because it keeps its single-use state in process. The host chart keeps only a cell's
   directory client (`directory.url`, `directory.clientId`, `directory.keyRing`, `directory.keys`) and holds no directory-server value, so no cell's release can run, upgrade or roll back the
   directory.
3. **The directory issues the cells' tokens.** The directory has a token endpoint, `POST /directory/v1/token`, which
   grants `client_credentials` to the machine client `cell-<name>` of a registered cell. The client authenticates with
   `private_key_jwt` (RFC 7523): an assertion whose `iss` and `sub` are `cell-<name>`, whose `aud` is the token
   endpoint's URL or the directory's issuer (RFC 7523 section 3), with a `jti` the directory accepts once, remembered in
   its `IExpiringKeySet` until the assertion's `exp`, and an `exp` at most five minutes ahead, signed by the key of the
   cell's `Directory:KeyRing`. A failure answers as RFC 6749 section 5.2 says. The directory verifies the assertion against the public keys it holds in that cell's
   record (decision 5). The access token is a JWT that the directory signs with its own key ring
   (`DirectoryServer:KeyRing`, a PEM key from a Secret or keys a key management service holds, as a cell's key rings
   are): `iss` is the directory's own issuer (`DirectoryServer:Issuer`, its public URL), `aud` is `urn:ncode:directory`,
   `scope` is `directory`, `client_id` is `cell-<name>`, it carries no `cnf`, and it lives five minutes
   (`DirectoryServer:TokenLifetime`). The directory validates its own tokens locally and accepts a token of no other
   issuer, so no cell's availability decides whether another cell can reach the directory.
4. **The control-plane tenant is the trust anchor, pinned in the directory's configuration.**
   `DirectoryServer:ControlPlane:Cell` names the control-plane cell, `DirectoryServer:ControlPlane:Region` and
   `DirectoryServer:ControlPlane:Kind` give its region and kind, `DirectoryServer:ControlPlane:TenantId` and
   `DirectoryServer:ControlPlane:Issuer` name its control-plane tenant and that tenant's issuer, and
   `DirectoryServer:ControlPlane:ClientJwks` holds the public keys of the control-plane cell's own machine client. The
   directory's `DirectoryAnchorInitializer` initializes the anchor at startup, before it serves any request: in one serializable transaction, which
   every start repeats and which changes nothing when the stored anchor already matches, it writes the control-plane
   cell's record with those keys and the control-plane tenant's placement in that cell, as it already holds that tenant
   (ADR-1411, decision 2). Until that transaction commits, the directory's readiness check fails and every route, the
   token endpoint included, answers `503`, so the first request it serves, the control-plane cell's token request
   against an empty database, finds the anchor's keys. A configuration that conflicts with the stored state, such as a
   tenant identifier that another cell holds or retired, fails the start, and the directory never becomes ready. The
   directory refuses every request that would register, move, retire or re-key either (`409`): the anchor changes only
   by configuration, and the next start writes the configured keys into the anchor's record. Only the anchor cell writes
   global state: it registers, updates and retires cells, publishes the team, platform, authority and key documents
   (decision 7), retires any tenant and writes any tenant's record. Every other cell writes only its own cell descriptor
   and the reservations and placements of the tenants it holds.
5. **The platform registers its cells.** A cell exists on the platform once a global administrator registers it through
   the control-plane cell's management API, `PUT /api/cells/{name}`, with its region, kind and the public keys of its
   machine client `cell-<name>` (the public half of its `Directory:KeyRing`, as the `directory-client-jwks` command of
   decision 12 prints it); the control-plane cell writes that record to the directory. `GET /api/cells` and
   `GET /api/cells/{name}` list and read the records, and `DELETE /api/cells/{name}` retires a cell that holds no
   tenant. The anchor writes them to the directory through `PUT /directory/v1/cells/{name}/registration` (region, kind
   and JWKS) and `DELETE /directory/v1/cells/{name}`, which answers `409` while the cell holds tenants and never
   reissues a retired name. A cell whose record is missing or retired is refused at the token endpoint with `invalid_client` and keeps
   retrying with backoff, as a cell's registration with the directory retries
   ([`CellRegistrationService`](../../src/NCode.Identity.OpenId.GlobalDirectory/CellRegistrationService.cs)). Once
   authenticated, a cell reports its own descriptor (management URL, region, kind) through
   `PUT /directory/v1/cells/{name}`, and the directory refuses a descriptor whose region or kind differs from the
   registered record, or whose management URL another cell reports (`409`). No cell holds a machine client in another
   cell's tenant.
6. **The control-plane cell is dedicated.** On a platform with a directory, the control-plane cell holds the
   control-plane tenant and nothing else. The directory refuses to place any other tenant in the anchor cell (`409`),
   and the control-plane cell refuses to create any other tenant, through the management API or on demand by a
   tenant-resolution strategy. It holds the platform's administrators (the control-plane tenant's local accounts and
   machine clients), the role assignments at the platform and team nodes, the platform settings and encryption keys, the
   teams and their root keys, setup, and the platform and team audit events (ADR-1411, decision 17). It runs the same
   host and chart as every cell, so the platform administers itself with its own product, through the same code a
   customer's tenant runs on.
7. **The control-plane cell publishes the authority and its keys through an outbox, and the feed replicates them.**
   Beside the team and platform documents, the control-plane cell publishes to the directory, and the change feed
   carries to every cell, three more kinds of document, each one envelope, a `ReplicatedDocument` of `Kind`, `Key`,
   `Revision` and `Content`, where `Content` is null for a retired document. Every other `Content` is the
   schema-versioned envelope of the stored payloads, `{ "$ver": 1, "$payload": { ... } }`, written and read through
   `IPersistedPayloadSerializer`; the directory relays it as an opaque value, a reader of content without the envelope
   throws, and the shapes below are each document's `$payload`:
   - The `platform-authority` document (key empty) lists every role assignment at the platform node and the enabled
     clients of the control-plane tenant: `{ assignments: [{ principalId, roleName }], clients: [{ principalId,
     clientId }] }`. The clients let a workload cell know, offline, whether a token's subject is a client acting for
     itself, which is the audit actor, without calling the control-plane cell.
   - The `team-authority` document (key is the team identifier) lists every role assignment at that team's node, with
     the platform document's `assignments` shape and no `clients`.
   - The `management-keys` document (key empty) holds the public keys of the control-plane tenant that verify management
     tokens (audience `urn:ncode:management`) and the key identifiers it has revoked (decision 12): `{ keys: [{ kid,
     secretType, algorithm, use, keySizeBits, publicKey, activatesWhen, retiresWhen, expiresWhen }], revokedKeyIds: []
     }`. `publicKey` is the persisted secret's base64url public material. Keys that activate later are included, so a
     successor reaches every cell before it signs, and a cell ignores an expired entry.

   The following hold for every document:
   - **Source revisions.** Each document carries a source revision, a number the control-plane cell keeps per document
     in a row of its own, a concurrency token, so two transactions that change one document serialize. The revision
     rises by one in the transaction that changes what the document holds: the grant or revocation of an assignment at
     its node, a team's creation or retirement, a change of the control-plane tenant's clients, or a change of its key
     set by the key rollover ([ADR-1404](1404-a-tenants-provisioned-keys-roll-over-ahead-of-retirement-and-expire-after-a-grace.md)),
     which adds, activates and retires keys, or by a revocation.
   - **A transactional outbox enforced in the persistence layer.** An Entity Framework Core `SaveChangesInterceptor`,
     `ReplicatedDocumentOutboxInterceptor`, runs only in the control-plane cell of a platform with a directory
     (`IDirectoryOutboxPolicy`, which reads `IPlatformTopology`). In `SavingChangesAsync` it finds the tracked changes
     that touch a replicated document, composes each touched document whole from the database rows overlaid with the
     tracked changes, raises its source revision and adds a `DirectoryOutbox` row in the same `SaveChanges`, so a change
     and its publication commit or roll back together and nothing reaches the directory before its change commits. No
     writer can forget it: setup, recovery, the management API and key rollover all go through `SaveChanges`.
   - **Ordered, idempotent publishing.** The `DirectoryOutboxPublisher` runs in the control-plane cell only. A commit
     wakes it (`IDirectoryOutboxSignal`), and it also runs at every start and every `Directory:PublishInterval`, five
     seconds by default. It sends each document's newest pending row, in revision order, because a document is published
     whole and a newer row supersedes an older one, and deletes the rows up to a revision only once the directory
     acknowledges it, retrying with backoff until then.
   - **Stale publications change nothing.** The directory keeps each document's latest revision and stores a publication
     only when its revision is higher. A publication that is not higher answers `200` with the stored revision and
     changes nothing, so a retried publication is idempotent and a delayed one never overwrites a newer one.
   - **Crash and restore recovery.** A crash between the commit and the publication leaves the row in the outbox, and
     the publisher sends it at the next start. At startup the publisher also reads the revisions the directory holds
     (`GET /directory/v1/replicated-revisions`, in pages of at most 1000 that follow the `after` continuation), and a
     document whose local revision is behind the directory's, as after a restore of the control-plane cell's database,
     is republished from its current state at the revision after the directory's.
   - **Routes.** The directory maps `PUT` and `GET /directory/v1/platform/authority`, `PUT` and
     `GET /directory/v1/teams/{teamId}/authority`, `PUT` and `GET /directory/v1/platform/management-keys` and
     `GET /directory/v1/replicated-revisions`. Only the anchor cell may `PUT` a document or read the revisions (`403`
     otherwise), a document whose `Kind` or `Key` differs from its route is `400`, and every cell may `GET` a document.

   The feed carries each document as `DirectoryChange.Replicated`, keeps each document's latest change, as it keeps each
   team's latest document (ADR-1411, decision 5), with the same ordering, the same 64 KiB document limit and the same
   page limit, and each change carries its source revision. A retired team's authority document is retired with it.
   These documents hold opaque principal identifiers, role names, client identifiers, public keys and key identifiers,
   never a name, an email address or a credential, so the directory still holds no personal data of an end user. A cell
   applies them in feed order into its own tables, under the checkpoint it already keeps: `ReplicatedDocuments` holds one
   row per document with its revision, and `RevokedManagementKeys` holds each revoked key identifier, its revocation time
   and its source. It applies a document only when its revision is higher than the one it holds and merges the revoked
   identifiers, so no later document returns a revoked key. In the control-plane cell `RevokedManagementKeys` is the
   source of the revoked list.
8. **Every cell authenticates and authorizes management requests from its own data, and a management token is usable
   only at the cell it is presented to.** The remote `IManagementTokenValidator` validates a management token against
   the replicated management keys: issuer `Cell:ControlPlane:Issuer`, audience `urn:ncode:management`, and a key the
   replica holds. It never fetches a discovery document or a key set from the control-plane cell.
   - **Every management token is bound by DPoP.** On a platform with a directory, management tokens are
     sender-constrained by DPoP ([RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449)). The control-plane tenant
     issues a token for the audience `urn:ncode:management` only to a token request that carries a DPoP proof, so every
     such token is bound by `cnf.jkt`, and the management API of every cell of the platform, the control-plane cell's
     included, answers `401` to a management token without that binding or one sent under the `Bearer` scheme. The
     setting `require_dpop_bound_access_tokens` applies to a resource server as well as to a client: a token request
     whose resources include a resource server with it set must carry a DPoP proof. The management resource server is
     seeded with it `true` on a platform with a directory, in the control-plane tenant and in every cell's management
     tenant. A platform of one cell accepts `Bearer` and DPoP both.
   - **A proof names the cell it is sent to.** A management request carries one DPoP proof. The cell compares its `htu`
     with its own configured management URL (`Cell:ManagementUrl`; a control-plane cell, and a cell with a management
     tenant, checks at startup that its management URL has the origin of its root tenant's issuer and refuses to start
     otherwise),
     never with the request's `Host`, and accepts its `jti` once. A cell that receives a token therefore holds nothing
     it can use at another cell: the proof it received names its own URL, and only the client holds the private key that
     makes a proof for another cell's. Because the directory refuses two cells the same management URL (decision 5), no
     two cells accept the same proof.
   - **Every caller is a DPoP client.** The platform console and a cell console sign a proof for each cell they call, as
     ADR-1437 decision 6 describes the console's proofs, and an automation client, a machine client of the control-plane
     tenant or of a cell's management tenant, holds a DPoP key beside its client key. A cell's management tenant
     (decision 10) issues and its cell accepts management tokens under the same rule.

   The replicated `IControlPlaneAuthority` answers from the replicated platform and team authority, and a tenant's own
   assignments stay in the tenant's cell. No cell calls another cell to authorize a request: the control-plane cell maps
   no authority endpoint, and no cell holds a token for the control-plane cell. A
   cell whose replica holds no management keys yet refuses management tokens with `503`, and a cell with keys accepts
   them whatever the control-plane cell's or the directory's state. A workload cell holds no control-plane user names,
   because the directory holds no personal data, so its `IControlPlanePrincipalDirectory` names only the principals of
   its own management tenant. A role holder from the control-plane tenant is listed by identifier and granted by
   identifier, which the cell accepts without an existence check, and a username names a principal of the cell's own
   management tenant. The platform console grants control-plane users by the identifier it resolves at the
   control-plane cell.
9. **Replicated authority is statically stable.** A cell authorizes from the authority and keys it last applied, with no
   staleness cutoff, and it never takes an unreachable directory for a grant or a revocation. A granted or revoked role
   reaches every cell within the publish latency plus one feed poll. The publish latency is the time until the directory
   acknowledges the change's outbox row (decision 7): a few seconds while the control-plane cell and the directory are
   up, and otherwise until both are up again, the control-plane cell's restart after a crash included. One feed poll is
   `Directory:PollInterval`, 30 seconds by default. Disabling an administrator's account or machine client in the
   control-plane tenant is the emergency revocation of a principal: it obtains no new token, so its access ends in every
   cell within the management token's lifetime (`access_token_lifetime`, five minutes by default), whatever the state of
   the outbox or the feed. Each cell reports the age of its last applied feed page as a metric and in its readiness
   detail, so an operator sees a replica that has stopped advancing.

   **Key custody is local.** A tenant's key custody, whether this cell may roll over and delete the secrets of its keys,
   is a fence the cell keeps on the tenant's own row, never a read of the directory.
   `PersistedTenant.KeyCustody` is `held`, `outgoing` or `incoming`, provably closed because a move has two sides, and
   `KeyCustodyCell` names the other cell. Export sets `outgoing`, with the target cell, before it asks the directory for
   the offer, and clears it when the offer is refused. Import inserts the tenant `incoming`, with the source cell, and
   sets `held` once the directory confirms the move, and release-moved deletes the tenant. A fenced tenant is enabled
   only after a live read finds it placed in this cell, which sets `held`: the read answers `503` when the directory
   cannot be reached and `409` when the tenant is placed elsewhere. `DefaultTenantKeyCustodyGuard` reads only the local
   row, so key rollover and secret deletion never read the directory and keep working through any directory outage.
   `TenantKeyCustodyReconciler` runs every 15 minutes, on one replica per window, and sets `held` for an `incoming`
   tenant that the directory places in this cell. `RootTenantRules.IsTrustAnchor`, the control-plane tenant of the
   control-plane cell, is never fenced.
10. **A cell has a management issuer of its own.** A cell that serves its tenants through a dynamic tenant-resolution
    strategy (`DynamicByHost`, `DynamicByPath`) has a management tenant: its root tenant, seeded in the workload plane
    with the management scopes of the tenant families only. The resource node type `cell` and the built-in role
    `CellAdmin` join the authority hierarchy
    ([ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md)):
    an assignment of `CellAdmin` at the node `(cell, <name>)` manages every tenant placed in that cell, everything
    beneath them included, as `TenantAdmin` does at one tenant, and never reaches a team or the platform, never creates
    a tenant and never grants `TeamAdmin` or `GlobalAdmin`. Only a principal of the cell's management tenant holds
    `CellAdmin`, and its assignments live in the cell. The cell's management API accepts the tokens of two issuers: the
    control-plane tenant's, authorized as decision 8 describes, and its management tenant's, whose principals are
    authorized only by the assignments stored in that cell. A cell's issuer therefore signs only for authority the cell
    owns. The cell's management API lists its tenants at `GET /api/cell/tenants`, for a `CellAdmin` at `(cell,
    <Cell:Name>)` or a `GlobalAdmin`, as `GET /api/tenants` lists with a continuation token; `GET /api/cell` reads the
    cell, and `GET|POST|DELETE /api/cell/admins` list, grant and remove its `CellAdmin`s, the grant and revoke
    authorized with `Update` at `(cell, <name>)`. `CellAdmin` is granted only to principals of the management tenant,
    and its node covers the management tenant itself, so a `CellAdmin` manages that tenant's accounts. `GET /api/me`
    reports the caller's cell roles (`CallerResource.CellRoles`). `GET /api/me/tenants` stays the caller's explicit
    assignments, so inherited authority is never confused with it. A cell served by `StaticSingle` serves one customer
    tenant and has no management tenant: it is managed with the control-plane tenant's tokens and recovered offline
    (decision 12).
11. **The consoles.** The platform console runs from the control-plane cell's release as a client of the control-plane
    tenant. It manages the platform and the teams and lists and manages the tenants of every cell by calling each cell's
    management API with the control-plane tenant's token, as ADR-1437 decision 6 describes, from the cells its
    configuration names (`ManagementConsole:Cells`, the chart's `managementConsole.cells`). It is an aggregator: no
    cell's management depends on it. A cell with a management tenant (decision 10) can run a cell console from its own
    release, a client of that management tenant, which manages only that cell's tenants and keeps working when the
    global plane is unreachable. `ManagementConsole:Cell` names the cell of a cell console, whose bootstrap document
    carries `"console": "cell"` and `"cell": "<name>"` (a platform console's says `"console": "platform"`) and whose
    frontend hides the platform and team surfaces; its home page lists the cell's tenants from `GET /api/cell/tenants`.
    The platform console has a Cells page that lists the cells, registers one with its JWKS, retires one and shows
    whether it answers.
12. **Bootstrap, break-glass and the revocation of a key.** A platform starts in this order, and no step waits on a
    later one:
    1. **Keys first.** Before either release, the operator, or the infrastructure code that provisions the platform,
       provisions the directory's signing key (`DirectoryServer:KeyRing`) and the control-plane cell's directory-client
       key (its `Directory:KeyRing`), each a PEM key in a Secret or a key that a key management service holds, as a
       cell's key rings are. The host image's `directory-client-jwks` command reads a configured `Directory:KeyRing`,
       from its Secret or its key management service, and prints the public JWKS of its keys; the operator puts the
       control-plane cell's into the directory's `DirectoryServer:ControlPlane:ClientJwks`.
    2. **The directory.** It is deployed with its anchor configuration and initializes the anchor before it serves
       (decision 4).
    3. **The control-plane cell.** It is deployed with its directory client, creates its control-plane tenant locally
       without asking the directory (ADR-1411, decision 4) and runs setup, which creates the root administrator
       ([ADR-1512](1512-an-instance-is-set-up-once-through-a-one-time-setup-token-that-creates-a-root-global-administrator.md))
       and, in the same save, registers the platform console's client. Its first call to the directory is its token
       request, signed with the key the anchor pins.
    4. **The workload cells.** A global administrator registers each with the public JWKS that `directory-client-jwks`
       prints from that cell's key ring (decision 5). Each cell is then deployed with its directory client and, when it
       has a management tenant, runs its cell setup, which creates its first `CellAdmin` and its cell console's client.

    The one loop, the control-plane tenant authorizing changes to the namespace it is registered in, is broken by the
    pinned anchor. When the anchor itself is broken (every platform administrator locked out, or a setting that refuses
    every sign-in), an offline command of the host image, run as a Job with the cell's schema-owner credentials as
    `setup` and `secret-protection` are, recovers it: `recover-admin` creates or re-enables a local account in the
    control-plane tenant from a Secret and grants it `GlobalAdmin`, and in a cell with a management tenant it creates or
    re-enables a `CellAdmin`. Two offline operations recover it:

    - `recover-admin` creates or re-enables a local account in the anchor tenant from `Recovery:Admin:*` (`UserName`,
      `Password`, `Email`, `DisplayName`) and grants `GlobalAdmin` in the control-plane cell or `CellAdmin` in a cell with
      a management tenant; it refuses a workload cell without one.
    - `recover-sign-in` repairs a setting that blocks every sign-in. A settings descriptor is `IsSignInCritical` when
      it can: `allowed_connections`, `default_connection`, `grant_types_enabled`, `password_min_length`,
      `password_max_length`, `password_failed_attempts_limit`, `password_failed_attempts_window`, `email_mfa_required`,
      `require_dpop_bound_access_tokens` and `access_token_sender_constraint_required`. The command removes the anchor
      tenant's own values of those settings (the control-plane tenant, or a cell's management tenant) and, in the
      control-plane cell, the platform's values too. It re-enables the tenant's default local connection, the
      management console client and the management resource server, logs each change and records an audit event per
      change. A workload cell's platform values arrive from the feed and are repaired at the control-plane cell.

    A test proves recovery by acquiring a token and calling the management API after a blocking setting.

    **A compromised management signing key is revoked, not retired.** A rollover retires a key, and a retired key stays
    published and verifies tokens for the retirement grace (`tenant_key_retirement_grace`, seven days by default,
    ADR-1404), so a rollover never answers a compromise. A global administrator revokes the key's identifier through the
    control-plane cell's management API. In one transaction, the revocation removes the key from the control-plane
    tenant's key set, activates a replacement at once, without the activation delay, only when no remaining key of the
    revoked key's use and algorithm is active (a retired or pending key revoked beside an active signer plans none and
    reports that signer), and writes the management keys
    document at its next revision without the key and with its identifier listed as revoked (decision 7). The
    control-plane cell refuses the key at once. Every other cell drops it within the publish latency plus one feed poll
    (decision 9) and keeps its identifier as revoked, so no later document returns it; until then, the cell also refuses
    the tokens the replacement signs. A cell that cannot reach the directory keeps accepting tokens the revoked key
    signs for as long as it is cut off. An operator closes that exposure offline with the host image's
    `revoke-management-key` command, run in that cell as a Job with its schema-owner credentials, as `recover-admin` is:
    it removes the key identifier from the cell's replica and records it as revoked.
13. **The words.** The "global plane" is the directory and the control-plane cell. The "control-plane cell" keeps its
    name and `Cell:IsControlPlane`, and a "workload cell" is every other cell. A cell's "management tenant" is its root
    tenant when the cell has one (decision 10). "Platform" still means the directory and its cells together (ADR-1411,
    decision 19).

## Options considered

- **The control plane in a cell that also serves tenants, with the directory in its release.** One cell's outage,
  release or restore stops the management of every tenant and the whole directory, and the directory shares that cell's
  failed upgrades. Rejected.
- **The directory on its own compute, still trusting the control-plane tenant's tokens.** Every cell still obtains its
  directory token from the control-plane cell, so that cell's outage still cuts every cell off from the directory.
  Rejected.
- **The directory caching and persisting the control-plane tenant's keys.** It keeps validating, but a cell can obtain
  no new token while the control-plane cell is down, so the outage reaches the directory five minutes later. Rejected.
- **Mutual TLS between the cells and the directory.** It needs a certificate authority the platform does not run and TLS
  passthrough at every ingress, while each cell already holds a key ring that signs its client assertions. Rejected.
- **The anchor ensured on the directory's first write.** The first request the directory serves is the control-plane
  cell's token request, which needs the anchor's keys before any write has happened. Rejected: the anchor is initialized
  at startup, before the directory is ready.
- **A signed assertion on every directory request instead of a token endpoint.** It is no standard profile, and every
  request would carry a single-use `jti` for the directory to track. Rejected: `client_credentials` with
  `private_key_jwt` is the standard exchange.
- **Live authority with a cache that answers when the control plane is unreachable.** Every decision still calls the
  control-plane cell while it is up, and during an outage the cache answers with whatever it last saw, so a cell carries
  both the live coupling and a copy's lag, without the ordered, checkpointed delivery the feed gives a replica.
  Rejected.
- **Live authority alone, failing closed.** It keeps one answer per decision and makes every cell's management as
  available as the control-plane cell. Rejected: it is the coupling this record removes.
- **Replicated authority with a staleness cutoff.** A cell would refuse platform and team authority once its replica is
  older than a bound, so a directory outage longer than the bound becomes a management outage in every cell. Rejected:
  the lag is bounded by the feed while the directory is up, and disabling an account revokes everywhere within a token's
  lifetime whatever the feed does.
- **Replicated authority, statically stable (chosen).** Every cell decides from its own data, a revocation reaches every
  cell within the publish latency plus one feed poll, and disabling an account is the emergency path. It accepts the
  cost ADR-1417 names: a cell that the directory cannot reach keeps a revoked platform or team role until its replica
  catches up, which disabling the account bounds to one token lifetime.
- **Publishing an authority document beside its transaction.** Published before the local commit, a grant whose
  transaction then rolls back reaches every cell; published after it, a crash in between loses a revocation until a
  reconciler runs; and two whole-document publications of one node can arrive out of order, so the older one wins.
  Rejected: the outbox commits a change and its publication together, and source revisions order them at the directory
  and in every cell.
- **Revoking a compromised key by an immediate rollover.** Rejected: a rollover retires the key, which keeps verifying
  tokens for the retirement grace, seven days by default.
- **Bearer management tokens beside DPoP.** Rejected: any cell that receives a bearer token, from an automation client
  for example, can replay it at every other cell's management API, the control-plane cell's included, and act as its
  holder there.
- **An audience per cell for management tokens** (a resource indicator per
  [RFC 8707](https://datatracker.ietf.org/doc/html/rfc8707), such as each cell's management URL or
  `urn:ncode:management:<cell>`). A token minted for one cell would be refused by every other. But the control-plane
  tenant would register a resource for every cell, which ties registering a cell to that tenant's resource
  configuration, and the console would obtain and refresh one token per cell it calls. A token bound to a cell's
  audience but not to its holder still works for anyone who obtains it at the cell it names, so it protects the token
  less than a proof of possession does. Rejected: required DPoP binds every token to its client's key and every proof to
  the cell that receives it, with one token per caller and no per-cell registration.
- **Share the control-plane tenant's signing keys with every cell.** Rejected, as ADR-1191 rejects it: it spreads
  private key material across cells and regions, while public keys are all a cell needs.
- **A management issuer in every cell for all authority.** Rejected: a cell's issuer would sign for platform and team
  authority it does not own. A cell's management tenant signs only for the cell's own `CellAdmin` and tenant
  assignments.
- **Only per-cell management issuers, with platform administrators federated into each.** It needs enterprise
  connections ([#931](https://github.com/NCodeGroup/identity/issues/931),
  [#64](https://github.com/NCodeGroup/identity/issues/64)) before a platform can be run, and loses the one sign-in that
  sees every team's tenants together. Rejected; such a connection can still be added to a cell's management tenant.
- **The control-plane tenant placed through the directory like any tenant.** Placing it needs a global administrator's
  authority, which comes from that tenant, so the platform could not start. Rejected: the anchor is pinned in
  configuration.
- **A platform service that is not a cell.** A bespoke service for platform administration would duplicate the host's
  accounts, sign-in, keys and management API. Rejected: the control-plane cell is a cell, so the platform runs its own
  administration on its own product.
- **One console per cell only.** Rejected, as ADR-1437 rejects it: an operator could not see a team's tenants together,
  and the platform and team views exist only in the control-plane cell. The platform console keeps that view; a cell
  console is a cell's own slice beside it.
- **Extending `/api/me/tenants` to inherited authority.** A `CellAdmin` would see the cell's tenants without an
  assignment. Rejected: the route lists explicit assignments, and a listing of inherited authority is
  `GET /api/cell/tenants`.
- **Resetting every setting of the anchor tenant.** Recovery would clear a blocking value without knowing which one.
  Rejected: it would discard intended configuration, so `recover-sign-in` removes only the sign-in-critical settings.
- **A directory-only mode of the host chart.** One chart would still carry both failure domains' values and versions
  together. Rejected: a chart of its own makes the separation structural.

## Consequences

- A workload cell's outage, bad release or restore touches its own tenants, their management included, and nothing else.
- While the control-plane cell is down, every cell keeps serving and keeps authorizing management requests whose tokens
  are still valid; no administrator obtains a new control-plane token, platform and team changes and cell registrations
  wait, and a cell with a management tenant is managed by its own `CellAdmin`s. The directory keeps issuing tokens to
  cells, so tenant creation, which needs only the directory and the tenant's own cell, continues.
- While the directory is down, tenants and teams are neither created, deleted nor moved, cells are not registered, and
  the replicas stop advancing; serving and management continue in every cell.
- A role granted or revoked at the platform or a team reaches a cell within the publish latency plus one feed poll,
  rather than on the cell's next decision, and a revocation committed just before the control-plane cell fails reaches
  the cells once it is back. The emergency revocation is disabling the account, which takes effect everywhere within the
  management token's lifetime.
- The control-plane cell keeps an outbox and a source revision per replicated document, and the directory keeps the
  latest revision of each, so publication survives crashes, retries and restores without reordering.
- A compromised management key stops verifying in every cell that reaches the directory within the publish latency plus
  one feed poll; a cell cut off from it needs `revoke-management-key` run offline. Management in a workload cell pauses
  for that same bound after a revocation, because the replacement's tokens verify only once the cell has its key.
- Every caller of a platform's management API is a DPoP client, automation clients included, and a cell that receives a
  management token cannot use it anywhere else.
- Starting a platform begins with provisioning keys: the directory's signing key and the control-plane cell's
  directory-client key exist, and the anchor's public JWKS is in the directory's configuration, before either release.
- A platform of more than one cell runs one more release and database for the directory and dedicates compute to the
  control-plane cell. A platform of one cell runs one host and its database, with no directory.
- Adding a cell is a platform action: a global administrator registers its name, region, kind and public keys.
- The directory is a token issuer for the cells, with a key ring of its own to back up and rotate, and it still runs one
  replica; a second replica needs the shared-state adapter for its assertion-replay record, as for its other single-use
  state.
- `CellAdmin` and the cell node widen the authority hierarchy by one node type that sits beside the team chain, never in
  it.
- Key rollover and secret deletion keep working through any directory outage, because a tenant's key custody is a local
  fence ([#944](https://github.com/NCodeGroup/identity/issues/944)). The work is
  [#940](https://github.com/NCodeGroup/identity/issues/940).
- The records and documents that describe the control plane in a shared cell are rewritten
  in place to match it
  ([ADR-1508](1508-a-decision-record-is-rewritten-when-its-decision-changes-and-relates-only-to-template-records.md)):
  ADR-1411 (decisions 2 to 5, 15 and 19, and its consequences), ADR-1417 (decision 5, its options and consequences),
  ADR-1191 (decision 2, its DPoP rule, its options and consequences), ADR-1437 (decision 6 and its options), ADR-1404
  (the revocation of a key), ADR-1512,
  [ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md),
  [ADR-1057](1057-local-account-management-is-an-opt-in-capability-that-provisions-the-principal-eagerly-and-authorizes-per-tenant.md),
  [ADR-1018](1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md) and ADR-1134
  (points 12 and 13, and its consequences), and `docs/platform-bootstrap.md`, `docs/hosting-analysis.md`,
  `docs/backend-for-frontend.md`, `docs/first-deployed-instance.md` and both charts' READMEs.

## References

- [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md) —
  cells, the global directory, the feed and the control-plane cell
- [ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md) —
  role assignments at nodes and `IControlPlaneAuthority`
- [ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md)
  — management tokens and `IManagementTokenValidator`
- [ADR-1437](1437-the-management-console-is-a-confidential-client-whose-own-bff-host-serves-only-the-console.md) — the
  console and its cell forwarders
- [ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md) — a cell fails alone,
  and serving never calls the directory
- [ADR-1404](1404-a-tenants-provisioned-keys-roll-over-ahead-of-retirement-and-expire-after-a-grace.md) — key activation
  delay and retirement grace
- [ADR-1512](1512-an-instance-is-set-up-once-through-a-one-time-setup-token-that-creates-a-root-global-administrator.md)
  — setup and the root administrator
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)
  — the hosted platform's regions and domain
- [`DirectoryAuthenticationGate`](../../src/NCode.Identity.OpenId.GlobalDirectory.Server/DirectoryAuthenticationGate.cs),
  [`DefaultCellTokenSource`](../../src/NCode.Identity.OpenId.GlobalDirectory/DefaultCellTokenSource.cs),
  [`ReplicatedManagementTokenValidator`](../../src/NCode.Identity.OpenId.GlobalDirectory/ControlPlane/ReplicatedManagementTokenValidator.cs),
  [`ReplicatedControlPlaneAuthority`](../../src/NCode.Identity.OpenId.GlobalDirectory/ControlPlane/ReplicatedControlPlaneAuthority.cs),
  [`DirectoryChangeFeedService`](../../src/NCode.Identity.OpenId.GlobalDirectory/DirectoryChangeFeedService.cs),
  [`DefaultDirectoryStore`](../../src/NCode.Identity.OpenId.GlobalDirectory.Server/Stores/DefaultDirectoryStore.cs)
- [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749) section 4.4,
  [RFC 7523](https://datatracker.ietf.org/doc/html/rfc7523) section 2.2,
  [RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449), [RFC 8707](https://datatracker.ietf.org/doc/html/rfc8707),
  [RFC 9700](https://datatracker.ietf.org/doc/html/rfc9700)
- [#921](https://github.com/NCodeGroup/identity/issues/921), [#922](https://github.com/NCodeGroup/identity/issues/922)
  and [#845](https://github.com/NCodeGroup/identity/issues/845) (the console's cells, its view of cells, and fleet
  management); [#918](https://github.com/NCodeGroup/identity/issues/918),
  [#919](https://github.com/NCodeGroup/identity/issues/919) and
  [#933](https://github.com/NCodeGroup/identity/issues/933) (bootstrapping a control-plane cell with its directory)
