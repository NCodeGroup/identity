# 1051. A local account belongs to one local connection behind a named pluggable store seam

- **Status:** Accepted
- **Date:** 2026-10-10
- **Deciders:** NCode Group

## Context

The stack authenticates an end-user by delegating to an ASP.NET Core authentication scheme at the
subject-authentication seam ([ADR-1401](1401-userinfo-authenticates-an-access-token-returns-enricher-built-claims-and-audits-disclosures-on-opt-in.md)):
the host presents a `ClaimsPrincipal`, and the runtime resolves it to a durable `PrincipalId`
([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)).
That is sufficient when another authority has already authenticated the user, but it leaves three capabilities with no
first-class shape:

- **Credential validation.** The server needs a place to verify a user name or email and a password, for the interactive
  sign-in and for the resource-owner password grant.
- **Active status.** The server needs a persisted view of whether an account is enabled or disabled.
- **Profile claims.** The id_token and UserInfo claims enrichers need a server-held profile to source `email`, `name`
  and the like from.

A `Principal` is deliberately contentless: a server-owned opaque join key that aggregates one human and many
identities, the persisted twin of `ClaimsPrincipal`. A `FederatedIdentity` is the persisted twin of a `ClaimsIdentity`:
one connection and one subject, `(connection, subject) -> principal`
([ADR-1535](1535-a-tenant-signs-users-in-through-named-connections-and-a-local-account-belongs-to-one.md)). Neither holds
a credential or a server-owned profile, because every such identity is authenticated by an external authority. What a
tenant also needs is the server's own kind of authority, a local connection, where this server holds the credential, the
profile and the status. It is Auth0's "database connection" beside a social or enterprise connection.

The forces:

- The local account must not be an opinionated model baked into the core. End-user storage is a host concern, and a
  host that already runs ASP.NET Core Identity (`IdentityUser`, `UserManager<TUser>`) must be able to plug it in.
- A tenant keeps several groups of local users, each behind a store of its choice, so a store is selected by name and a
  user name or an email is unique within one group only.
- Credential handling differs by backend, so a seam that exposes a raw hash field leaks hashing semantics and makes
  every backend author implement and register several interfaces that differ only by verb.
- A capability specific to local accounts, the password grant, must not make every host pay for the account stack when
  it fronts a different user store or disables the grant.
- A local account is an end-user subject, so a store registration must prove at compile time that a capability has a
  store ([ADR-1069](1069-di-registration-proves-every-prerequisite-at-compile-time-through-marker-builders.md)).

## Decision

**A local account belongs to exactly one connection of the `local` strategy, and its identity is
`(connection, LocalAccountId)`. The core consumes local accounts through exactly one pluggable seam,
`ILocalAccountStore`, selected by name, and never persists end-user accounts itself; the password grant is a capability
of the account stack, not of the authentication package.**

### A local account belongs to a local connection

`FederatedIdentity` stays the single linkage concept, `(connection, subject) -> principal`, for every connection; the
`Federated` prefix marks "this system's own type", not "external only". A connection of the `local` strategy is the
server's own: its identities have the account id as the subject, and only they carry a `PersistedLocalAccount` (a
required `ConnectionId`, an optional user name, an optional email, claims, enabled state, and a store-managed read-only
`SecurityStamp`) in a 1:1 companion record:

| concept                 | role                                       | present for                          |
| ----------------------- | ------------------------------------------ | ------------------------------------ |
| `Principal`             | contentless actor (`PrincipalId`)          | every human                          |
| `FederatedIdentity`     | linkage `(connection, subject)` -> principal | every connection (external or local) |
| `PersistedLocalAccount` | credential, profile and status payload     | local connections only (1:1)         |

External identities never grow password or profile columns, and resolution and linking operate on one uniform linkage
with no local special case. A local sign-in resolves like any other: the interactive verifier and the password grant
attach the connection's id to the principal under `urn:ncode:identity:connection`, and the account's identity yields the
`PrincipalId` the `sub` claim carries. The payload is a data contract, like every other; pluggability is expressed by
the store seam, not by turning the record into an interface.

A user name and an email are each unique within the connection and a null never conflicts, so two connections of one
tenant can hold the same email. The `local_account_identifier` setting decides whether an account needs an email, a user
name, or at least one of them.

### The one seam

`ILocalAccountStore` (namespace `NCode.Identity.OpenId.Accounts.Stores`) is the single, backing-store-agnostic seam.
Credentials are first-class operations, and the contract exposes no password hash:

- `AddAsync(account, ReadOnlyMemory<byte>? password, ct)` and `SetPasswordAsync`, where the store hashes the credential
  and rotates the security stamp.
- `VerifyCredentialAsync(identifier, password, isRefusedAsync, cancellationToken)`, where the identifier is a
  `LocalAccountSignInIdentifier` that carries the `ConnectionId` and that the `local_account_identifier` setting allows
  to be an email, a user name or either.
  The store performs the comparison, may upgrade a stored hash on success, and returns a
  `LocalAccountVerificationResult`
  ([ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md)).
- `GetByUserNameOrDefaultAsync`, `GetByEmailOrDefaultAsync` and `GetPageAsync`, each taking the connection id, because
  those lookups are scoped to one connection.
- `UpdateAsync`, `GetByIdOrDefaultAsync`, `RemoveAsync` and `ReplaceClaimsAsync` for profile, lookup by the opaque
  account id, and claim management.

A password crosses the seam as `ReadOnlyMemory<byte>`, never as a managed `string`: callers encode it with
`SecureEncoding.UTF8` into a pinned, zeroing `SecureMemoryPool<byte>` buffer they own, so the raw password does not
linger on the GC heap. The seam is asynchronous, so a `ReadOnlySpan<byte>` cannot cross it. A stored hash is a
`string`: a one-way derivation held at rest, not the secret.

`IPasswordHasher` (namespace `NCode.Identity.OpenId.Accounts.Credentials`) is the hashing facade. Its internal default,
`DefaultPasswordHasher` (`NCode.Identity.OpenId.Accounts`), is PBKDF2-HMAC-SHA256 over `Rfc2898DeriveBytes.Pbkdf2` with
versioned parameters and rehash-on-verify. The Entity Framework store uses the `IPasswordHasher` that the host registers
with `AddDefaultPasswordHasher()`; the ASP.NET Core Identity adapter uses the user manager's hasher.

### Stores are registered under a name

A local connection's options name its store (`LocalConnectionOptions.Store`). `LocalAccountStoreRegistration` holds a
store's name and factories, `LocalAccountStoreCatalog` indexes the registrations by name and resolves a connection's
store, and the public `AddLocalAccountStore(...)` registration in `NCode.Identity.OpenId.Accounts.Abstractions` adds one
registration and also registers `LocalConnectionStrategy`, the `local` strategy. Every store implementation, the
family's two and a custom one, calls it, as every surrogate-key consumer calls `AddSurrogateKeyGenerator()`: an
implementation package cannot host it, because both store packages and the capability packages need it. A connection
that names an unregistered store is refused when it is written. The `local` connection that every tenant is created with
names the store `default`.

### Two reference stores

Both extend `IServiceBuilder<StoreManagerLibrary>`, take a `storeName` that defaults to `default`, and are opt-in
packages the core does not depend on:

- `AddLocalAccountEntityFramework(string storeName = "default")` in
  `NCode.Identity.OpenId.Persistence.EntityFramework.Accounts` stores the account on the shared `OpenIdDbContext`, so a
  caller that also writes the account's identity, as local-account management does
  ([ADR-1057](1057-local-account-management-is-an-opt-in-capability-that-provisions-the-principal-eagerly-and-authorizes-per-tenant.md)),
  commits both in one unit of work. Its unique indexes are `(tenant, LocalAccountId)`,
  `(tenant, connection, NormalizedUserName)` and `(tenant, connection, NormalizedEmail)`.
- `AddAspNetIdentityLocalAccountStore<TUser>(string storeName = "default")` in
  `NCode.Identity.OpenId.Accounts.AspNetIdentity` (`TUser : IdentityUser, ITenantBoundUser, new()`) implements the seam
  over `UserManager<TUser>`. `ITenantBoundUser`, the contract of a user that belongs to one tenant and one connection
  (`ConnectionId`), is in `NCode.Identity.OpenId.Accounts.Abstractions`. The adapter's lookup normalizer prefixes
  `{normalizedTenantId}|{connectionId}|`, the connection id written verbatim, from an ambient connection scope that the
  store opens around each `UserManager` call, so Identity's own unique index on `NormalizedUserName` is unique per
  connection. An account with no user name is stored with Identity's `UserName` set to its `LocalAccountId` and read
  back as `null`. The adapter treats Identity's lockout as disabled. Because such a store does not take part in the
  `OpenIdDbContext` unit of work, `EntityStoreManager` resolves it through a DbContext-less store-factory fallback. The
  adapter stays store-agnostic: it names no Entity Framework Core type. The opt-in
  `NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore` package references only the accounts abstractions
  of the family and holds `HasTenantLookupLengths()` for a host whose Identity model is an `IdentityDbContext`, which
  sizes `UserName`, `NormalizedUserName`, `Email` and `NormalizedEmail` from `AccountMaxLengths`, which counts the
  connection id in the prefix, in one call, tested over a real Identity model. A host on another store sizes the four
  columns itself.

Any other store (LDAP, a bespoke table, an external API) is a third implementation of the same seam, registered with
`AddLocalAccountStore(...)`.

### Registration proves a store exists

Every store registration returns `IServiceBuilder<LocalAccountLibrary>`. `AddLocalAccountAuthentication`,
`AddLocalAccountInteractiveAuthentication` and `AddLocalAccountManagement` take that builder as a required parameter,
so a capability without a store is a compile error; a custom store returns the marker too.

### The password grant is dependency-inverted

`NCode.Identity.OpenId.Authentication` references no accounts package and registers
`DefaultUnsupportedPasswordGrantHandler`, which answers `unsupported_grant_type`. `AddLocalAccountAuthentication` in
`NCode.Identity.OpenId.Accounts.Authentication` registers `DefaultAuthenticatePasswordGrantHandler` in its place and
adds `DefaultCreatePasswordGrantSubjectHandler` for the public `CreatePasswordGrantSubjectCommand`. Support follows the
presence of the capability handler, not a flag: a rejected credential is a failure (`invalid_grant`), never
"unsupported".

The grant takes the `connection` request parameter and otherwise uses the client's `default_connection`; it accepts only
an enabled connection of the `local` strategy that the client's `allowed_connections` allows, and it reads the
connection's settings for the password limits. The failed-attempt limit keys on `(tenant, connectionId, accountKey)`.

`AddLocalAccountAuthentication` and `AddLocalAccountInteractiveAuthentication` each register, once through
`TryAddEnumerable`, the account-sourced UserInfo enricher `DefaultGetLocalAccountUserInfoClaimsHandler` (OpenID Connect
Core section 5.4) and `DefaultValidateLocalAccountSubjectHandler`
([ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md)), which refuses a
subject whose account is disabled or gone.

### A local account has a display name

Every local account has a required display name of at most 300 characters, which defaults to its user name, else its
email, when the account is created. It is the only source of the account's `name` claim in the cookie principal, the
password-grant subject and UserInfo, and a `name` profile claim is refused. The ASP.NET Core Identity adapter stores it
through `ITenantBoundUser.DisplayName`.

### Borrowed from ASP.NET Core Identity

ASP.NET Core Identity is the reference for shape and lessons. The family borrows store segregation, the `SecurityStamp`
(a store-managed value that each credential change rotates and nothing reads) and the hasher shape. Its `IdentityUser`
aggregate is not adopted: it fuses actor, credential and profile and nests external logins beneath the user, which
double-counts a human holding a local and a social login. The adapter maps inward, so an `IdentityUser` is an account
plus its identity in the connection.

## Options considered

- **Credentials on the `Principal`.** Rejected: it recontents the deliberately contentless join key, and a human has
  one principal but may hold several credentials across connections, so "the principal's password" is ill-defined.
- **An account that owns a `PrincipalId` behind a second resolver path.** Rejected: it forks resolution. Hanging the
  payload off an identity of a local connection keeps one linkage and one resolution path.
- **Sparse credential columns on the identity.** Rejected: external, lookup-only identities would carry password and
  profile columns that are always null.
- **A standalone account model independent of the principal and identity.** Rejected: it reintroduces the
  duplicate-human hazard ([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md))
  exists to prevent.
- **A `ConnectionIdentity` name for the linkage.** Rejected: it splits the `Federated*` family into mixed vocabulary
  with no clean pair, since `Identity` collides with `ClaimsIdentity` and the product name. The prefix already means
  "this system's persisted identity type", so an identity of a local connection fits the existing name.
- **One namespace of local accounts per tenant, with one store.** Rejected: a tenant cannot keep two groups of users
  apart, give each its own backend and password policy, or let the same email exist in both
  ([ADR-1535](1535-a-tenant-signs-users-in-through-named-connections-and-a-local-account-belongs-to-one.md)).
- **A source, provisioner and store split.** Rejected: every operation reads or writes the same account row, so the
  split multiplies the backend author's work and leaks the hash onto the public contract.
- **The password grant in the authentication package, or in the accounts package.** Rejected: the first keeps an
  unwanted dependency and ships the grant to hosts that do not want it; the second makes the accounts package depend
  on authentication internals.
- **Adopting `IdentityUser` wholesale.** Rejected for the duplicate-human conflation above.
- **One `ILocalAccountStore` with first-class credential operations, selected by name per local connection, a 1:1
  payload on the connection's identity, and the grant in an opt-in package.** Chosen: one seam to implement and register,
  an encapsulated hash, one resolution path and a composable grant.

## Consequences

- The password grant, active-status validation and profile-claim enrichment have a first-class home without the core
  persisting any end-user data.
- A host with no accounts package answers the password grant with `unsupported_grant_type`; a host with a store that
  rejects the credential answers `invalid_grant`.
- `sub` for a local login is the resolved `PrincipalId`, so relying parties see one durable subject per human.
- A tenant keeps several groups of local users, each with its own store, identifier rule and password policy, and the
  same email can belong to an account in each.
- `NCode.Identity.OpenId.Authentication(.Abstractions)` reference no account abstractions, so the dependency points
  from the accounts stack to authentication only.
- An ASP.NET Core Identity host brings Microsoft's EF-backed user store, or any `IUserStore<TUser>`, with no core
  change.
- The abstraction stays on the BCL `ReadOnlyMemory<byte>`; only the implementation packages take the `NCode.Buffers`
  dependency.

## References

- [ADR-1535](1535-a-tenant-signs-users-in-through-named-connections-and-a-local-account-belongs-to-one.md):
  connections, the `local` strategy and the connection-keyed identity.
- [ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md):
  principals and federated identities.
- [ADR-1401](1401-userinfo-authenticates-an-access-token-returns-enricher-built-claims-and-audits-disclosures-on-opt-in.md):
  the subject-authentication seam and claims enrichers the account store feeds.
- [ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md): opaque
  server-generated ids the account follows.
- [ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md):
  why the reference stores are opt-in packages that the core does not depend on.
- [ADR-1001](1001-behavior-is-a-service-a-strategy-collection-or-a-mediator-seam-chosen-by-one-ladder.md): the service
  model under which the default handlers delegate to `ILocalAccountStore`.
- [ADR-1025](1025-the-family-is-greenfield-and-pre-release-tracks-its-public-api-and-models-auth0-parity-plus.md): the
  pre-release, Auth0-parity-plus posture.
- [ADR-1069](1069-di-registration-proves-every-prerequisite-at-compile-time-through-marker-builders.md): marker builders.
- [ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md),
  [ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md): credential verification, refusal and
  disabled accounts.
- ASP.NET Core Identity (`Microsoft.AspNetCore.Identity`): prior art for store segregation, `SecurityStamp` and the
  hasher shape.
