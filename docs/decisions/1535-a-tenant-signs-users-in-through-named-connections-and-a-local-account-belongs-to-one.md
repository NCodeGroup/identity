# 1535. A tenant signs users in through named connections, and a local account belongs to one

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

An end user reaches a tenant through some authority: this server's own credential check, a social provider, an
enterprise IdP or a scheme the host registers. Each sign-in yields an identity that resolves to one tenant-scoped
principal
([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)), and
a local account is an identity the server's own credential check verifies
([ADR-1051](1051-a-local-account-is-a-self-issued-connection-behind-one-pluggable-store-seam.md)). A tenant needs a
record of the authorities it accepts, each with a name that clients and requests can use. It needs more than one
namespace of local accounts, each behind a store of its choice, with user names and emails unique within a namespace.
A client needs a list of the authorities it accepts, and the names in that list must be defined by something.

The forces:

- **Separate groups of users in one tenant.** A tenant serves employees and customers, or consumers and partners, with a
  password policy, sign-up rules and identifier rules for each. The same email can belong to a person in each group. A
  second tenant also separates them, but splits the clients, resource servers, roles and keys they share.
- **Several backends.** One group lives in the server's own store and another in an LDAP directory, an ASP.NET Core
  Identity database or a legacy API, each behind the `ILocalAccountStore` seam.
- **Enablement per client.** A client accepts the groups and upstream IdPs it was built for, fail-closed, as
  `allowed_connections` merges
  ([ADR-1010](1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md)).
- **One model for every authority.** Enterprise federation ([#64](https://github.com/NCodeGroup/identity/issues/64))
  needs a per-tenant configuration of each upstream IdP: issuer, discovery, keys, claim mapping. Local, social,
  enterprise and host-scheme sign-ins are one concept, and Auth0, which the family follows
  ([ADR-1025](1025-the-family-is-greenfield-and-pre-release-tracks-its-public-api-and-models-auth0-parity-plus.md)),
  calls it a connection.
- **The linkage key must not merge distinct authorities.** Two connections can trust the same upstream issuer, such as
  two Google applications or two Entra app registrations with different claim mappings. An identity keyed by
  `(issuer, subject)` would merge the users of the two.
- **Which authority signed a user in is the server's knowledge.** The server knows which connection verified a
  credential or ran an authentication scheme. A claim the upstream supplies must not choose that connection.
- **Open vocabularies.** A package adds a connection kind by registering it, never by editing the core
  ([ADR-1150](1150-vocabularies-are-open-unless-provably-closed.md)), and a package's own entity registers its own
  setting scope ([ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md)).
- **A simple tenant stays simple.** A tenant with one group of local users configures nothing.

The decision is right if [#64](https://github.com/NCodeGroup/identity/issues/64)'s strategies, a host-scheme sign-in and
every local store fit the resource unchanged, if two groups in one tenant never collide or share a policy by accident,
and if no sign-in path reads its connection from an upstream claim.

## Decision

**A tenant accepts sign-ins only through connections: tenant-scoped resources of a registered strategy. An identity is
keyed by its connection and its subject, a local account belongs to exactly one local connection, and a client enables
the connections it accepts.**

### 1. A connection is a tenant-scoped resource

The vocabulary (`IConnectionStrategy`, `IConnectionStrategyCatalog`, `IConnectionResolver`, `IConnectionDependentSource`,
`ResolvedConnection`, `ConnectionSelection`, the name rules, the claim type and the default names) lives in
`NCode.Identity.OpenId.Abstractions`, namespace `NCode.Identity.OpenId.Connections`. Its persistence contracts
(`PersistedConnection`, `PersistedConnectionSettings`, `IConnectionStore`) live in
`NCode.Identity.OpenId.Persistence.Abstractions` and their Entity Framework store in
`NCode.Identity.OpenId.Persistence.EntityFramework.Core`. Its runtime (`DefaultConnectionStrategyCatalog`,
`DefaultConnectionResolver` and the seed of the `local` connection) lives in `NCode.Identity.OpenId.Core`, and its
management API in `NCode.Identity.OpenId.Management`.

| Member         | Meaning                                                                                                                                                                                                                        |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ConnectionId` | Opaque, server-minted with `ICryptoService.GenerateResourceId()` ([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)), never reissued                                 |
| `Name`         | Operator-chosen and immutable: 1 to 128 ASCII letters, digits, `-` and `_`, starting with a letter or digit, unique per tenant and compared case-insensitively; what clients, requests and the sign-in page name               |
| `Strategy`     | A registered strategy name, immutable: `local`, and [#64](https://github.com/NCodeGroup/identity/issues/64)'s `oidc`, `saml` and `wsfed`                                                                                        |
| `DisplayName`  | What the sign-in picker shows                                                                                                                                                                                                  |
| `IsEnabled`    | A disabled connection signs no one in and its identities fail subject validation, as a disabled account does ([ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md)) |
| `Options`      | The strategy's own JSON object, validated by its strategy and stored as a JSON document like a settings document: it is the operator's document, not a server-typed payload                                                    |
| `Settings`     | A settings document at the `connection` scope (see 5), a sub-resource with its own concurrency token                                                                                                                           |

A strategy is an `IConnectionStrategy` registered with `TryAddEnumerable`, one class per strategy, and indexed by
`IConnectionStrategyCatalog`, which rejects a duplicate name, as the tenant environment types are. A strategy has a
`Name`, a `SignInMethod` and `ValidateOptions`, which returns an error description or `null`. The sign-in method is an
open vocabulary: `password` means the server verifies a password, so the interactive form and the password grant serve
the connection. No host-scheme strategy ships; a host that delegates to its own ASP.NET Core authentication scheme
registers a strategy whose options name that scheme, so every sign-in comes through a connection.

Every tenant is created with an enabled `local` connection, displayed as "Username and password", whose options are
`{"store":"default"}` and whose settings are empty. `LocalConnectionSeedHandler` creates it as a `SeedTenantCommand`
handler of priority High and is idempotent. Every client accepts it by default.

The management API serves `/tenants/{tenantId}/connections` (list, create), `/{connectionId}` (get, patch, delete),
`/{connectionId}/settings` (get, patch), `/{connectionId}/settings/limits` and `/{connectionId}/effective-settings`,
authorized at the tenant's node with `TenantScopeResource.For(tenantId)`
([ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)).
Deleting a connection answers `409` while any registered `IConnectionDependentSource` reports dependents: core reports
its federated identities, and `NCode.Identity.OpenId.Accounts.Management` reports the local accounts in the connection's
store. The local-account routes nest under their connection,
`/tenants/{tenantId}/connections/{connectionId}/local-accounts`, so a route names the store, and
`/platform/local-accounts` uses the platform tenant's `default_connection`.

### 2. An identity is keyed by its connection

`FederatedIdentity` carries a required `ConnectionId` and is unique on `(tenant, ConnectionId, subject)`. It records the
upstream `iss` as an optional `Issuer` when the principal carries one, for audit and display, and that issuer is not
part of any key. A local connection's subject is the local account's id.

The connection comes from the path that authenticated the user, never from an upstream claim. The interactive verifier,
the password grant and each strategy's callback attach the connection's `ConnectionId` to the `ClaimsPrincipal` they
produce under the claim type `urn:ncode:identity:connection`, and the `idp` claim holds the connection's `Name`. Both
claim types are the server's: no sign-in path copies an upstream claim of either type. Principal resolution looks up
`(ConnectionId, subject)` and provisions only a principal whose connection claim names an enabled connection. Any other
principal keeps its raw subject as its reference and is never provisioned
([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)).

Subject validation resolves the subject's connection from that claim and refuses with `access_denied` an unknown or
disabled connection, a connection the client's `allowed_connections` does not list, and a connection that differs from
the connection the request names.

### 3. A local account belongs to one local connection

`PersistedLocalAccount` carries a required `ConnectionId` and an optional `UserName`, and `LocalAccountEntity`'s indexes
are:

| Index                                      | Unique | Purpose                                                          |
| ------------------------------------------ | ------ | ---------------------------------------------------------------- |
| `(tenant, LocalAccountId)`                 | yes    | The opaque id is the subject and the management address          |
| `(tenant, connection, NormalizedUserName)` | yes    | Sign-in by user name within the connection                       |
| `(tenant, connection, NormalizedEmail)`    | yes    | Sign-in by email and one account per email within the connection |

Both name columns are nullable and a null never conflicts, so a connection may do without user names or without emails.
The setting `local_account_identifier` (`email`, `user_name` or `email_or_user_name`, default `email_or_user_name`,
replace-merged, applying to tenants and connections) decides which identifier an account must have: `email` requires an
email, `user_name` a user name, and `email_or_user_name` at least one. A sign-in identifier is looked up as a user name
first, then as an email, as the setting allows.

`ILocalAccountStore` takes the connection id on every lookup that is scoped to a connection
(`GetByUserNameOrDefaultAsync`, `GetByEmailOrDefaultAsync` and `GetPageAsync`) and a `LocalAccountSignInIdentifier` for
`VerifyCredentialAsync`. Credential verification keeps its constant cost whether or not the account exists
([ADR-1239](1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md)). Failed-attempt limits
key on `(tenant, connectionId, accountKey)`, where `accountKey` is the found account's `LocalAccountId`, or the submitted
identifier when no account matches, and the limit and window come from the connection's settings
([ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md)).
`DefaultLocalAccountValidator` checks email uniqueness per connection for every store, and the Entity Framework store's
unique index backs it.

### 4. Each local connection names its store

A store is registered under a name. `LocalAccountStoreRegistration` holds the name and the factories, and
`LocalAccountStoreCatalog` indexes the registrations by name and resolves a connection's store from its
`LocalConnectionOptions.Store`. The public `AddLocalAccountStore(...)` registration, in
`NCode.Identity.OpenId.Accounts.Abstractions`, adds one registration and also registers `LocalConnectionStrategy`, the
`local` strategy. Every store implementation, the family's two and a custom one, calls it, as every surrogate-key
consumer calls `AddSurrogateKeyGenerator()`: an implementation package cannot host it, because both store packages and
the capability packages need it. `AddLocalAccountEntityFramework(string storeName = "default")` and
`AddAspNetIdentityLocalAccountStore<TUser>(string storeName = "default")` register under a name, and the seeded `local`
connection names `default`. Registration still returns `IServiceBuilder<LocalAccountLibrary>`, so a capability without a
store remains a compile error
([ADR-1069](1069-di-registration-proves-every-prerequisite-at-compile-time-through-marker-builders.md)), and a
connection that names an unregistered store is refused when it is written.

The ASP.NET Core Identity adapter's `ITenantBoundUser` carries a `ConnectionId`. Its lookup normalizer prefixes
`{normalizedTenantId}|{normalizedConnectionId}|` from an ambient connection scope that the store opens around each
`UserManager` call, so Identity's own unique index on `NormalizedUserName` is unique per connection. An account with no
user name is stored with Identity's `UserName` set to its `LocalAccountId` and read back as `null`, and
`AccountMaxLengths` counts the connection id in the prefix.

### 5. `connection` is a setting scope beneath `tenant`

Core registers the `connection` scope (`OpenIdSettingScopes.Connection`) with `tenant` as its parent
([ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md)), because the
linking settings in core and the password settings in `NCode.Identity.OpenId.Authentication` both apply to it. These
settings apply to `connection` as well as `tenant`: `password_min_length`, `password_max_length`,
`password_failed_attempts_limit`, `password_failed_attempts_window`, `password_reset_enabled`,
`email_verification_enabled`, `email_mfa_required`, `local_account_identifier` and the four `federated_identity_*`
settings ([ADR-1236](1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md)). `email_code_lifetime` and
`email_code_sends_per_hour` stay tenant settings, because they are mail delivery limits. No self sign-up exists, so no
sign-up setting exists to scope. A connection narrows the tenant's values through each setting's merge, and a sign-in
reads `ResolvedConnection.Settings`: the tenant's effective settings resolved through the connection's document with
`IEffectiveSettingsResolver.ResolveStored(…, "connection", connectionId, …)`.

### 6. A client enables connections

- `allowed_connections` applies to clients, lists connection names, each valid under the name rules, and merges with
  `Intersect`. Unset, it allows every enabled connection; set, it allows only those it lists, fail-closed. It is a
  client's only allow-list of the authorities it accepts, and subject validation, the interactive surface and the
  password grant all read it
  ([ADR-1010](1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md)).
- `default_connection` applies to tenants and clients, merges by replacement and defaults to `local`. It names the
  connection used when a request names none.
- An authorization or token request selects a connection with the `connection` parameter or the first `idp:<name>` value
  of `acr_values`. `IConnectionResolver.SelectAsync` takes the requested name, which must name an enabled connection
  with a registered strategy that the client's `allowed_connections` allows; otherwise the request is refused with
  `invalid_request` before any credential is read. Without a requested name, the client's `default_connection` is used
  when it is usable, and otherwise no connection is preselected.
- The interactive surface offers the client's allowed connections whose strategy's sign-in method is `password`,
  narrowed to the requested one when the request names one, and preselects the default. The login form posts
  `connection`: the server renders a `<select id="connection">` when more than one connection is offered and a hidden
  input when one is. The forgot-password form carries `connection` too.
- The password grant takes `connection` and otherwise uses the default. It accepts only a connection whose sign-in
  method is `password`.

### 7. People in several connections

One person signing in through two connections has two identities. `IFederatedIdentityLinkingPolicy` decides whether the
second attaches to the first one's principal. Its settings apply at the connection scope, so a tenant that keeps
employees and customers apart sets `federated_identity_explicit_only` on one of them, and a tenant whose users sign in
through Google and a local account keeps linking by verified email. `sub` is the `PrincipalId` on every path.

The tenant transfer carries connections in its `core` section, imported before resource servers, and groups the
local-accounts section's records by connection id.

## Options considered

| Force                                                                | A. One namespace per tenant | B. Local databases only                           | C. Connections keyed by issuer  | D. Connections keyed by connection |
| -------------------------------------------------------------------- | --------------------------- | ------------------------------------------------- | ------------------------------- | ---------------------------------- |
| Groups with their own policy                                         | Only as separate tenants    | Yes                                               | Yes                             | Yes                                |
| Several backends in a tenant                                         | No                          | Yes                                               | Yes                             | Yes                                |
| Enablement per client                                                | No                          | A second allow-list beside the connections'       | Yes                             | Yes                                |
| Two connections to one upstream issuer                               | n/a                         | n/a                                               | Merge, or need a made-up issuer | Separate                           |
| [#64](https://github.com/NCodeGroup/identity/issues/64) builds on it | Needs its own model         | Needs its own model                               | Yes                             | Yes                                |
| Connection read from an upstream claim                               | n/a                         | n/a                                               | The issuer claim chooses it     | Never                              |
| Configuration for a one-group tenant                                 | None                        | None, with a default                              | None, with a default            | None, with a default               |

- **A. Keep one namespace per tenant.** Rejected. A second tenant does separate two groups, but it duplicates every
  client, resource server, role and key the groups share, gives no per-client choice, and leaves
  [#64](https://github.com/NCodeGroup/identity/issues/64) to invent the connection resource alone.
- **B. A local-database resource for accounts alone.** Rejected. It covers the local cases, but
  [#64](https://github.com/NCodeGroup/identity/issues/64)'s connection model would sit beside it with its own enablement
  and linkage, and the two would have to be joined later.
- **C. Connections, with identities still keyed by `(issuer, subject)`.** Rejected. A local connection can take a
  derived issuer, but two upstream connections to one issuer collide, and avoiding that means inventing an issuer, which
  is the connection key under another name. The issuer claim would also keep choosing the connection.
- **D. Connections, with identities keyed by `(connection, subject)`.** Chosen. Every authority is one resource, the key
  is what the server knows, and local accounts gain namespaces, backends and policies without a construct of their own.

## Consequences

- A tenant can keep groups of users apart, give each its own store and policy, and let each client choose among them. A
  one-group tenant configures nothing.
- A local account's email is unique within its connection, and a connection can sign users in by email.
- [#64](https://github.com/NCodeGroup/identity/issues/64) adds strategies to a resource that exists, with enablement,
  linkage, management routes and settings in place.
- Every sign-in path attaches a connection: the interactive verifier, the password grant, token exchange's subject
  validation and every host-registered scheme. A path that does not provisions nothing, which fails closed.
- The model is larger: one more resource, one more scope and one more request parameter. The default connection keeps it
  out of sight for a tenant that needs one.
- A smell that this is wrong: strategies that need a second resource of their own for configuration, or clients that
  routinely need connection rules beyond a list and a default.

## References

- [#931](https://github.com/NCodeGroup/identity/issues/931): building this record's decision.
  [#64](https://github.com/NCodeGroup/identity/issues/64): enterprise connections built on it.
- [ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md):
  principals, identities, resolution and linking.
- [ADR-1051](1051-a-local-account-is-a-self-issued-connection-behind-one-pluggable-store-seam.md): local accounts and
  `ILocalAccountStore`.
- [ADR-1010](1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md),
  [ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md),
  [ADR-1150](1150-vocabularies-are-open-unless-provably-closed.md): setting merges, registered scopes and open
  vocabularies.
- [ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md),
  [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md):
  resource identifiers and management authorization.
- [ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md),
  [ADR-1236](1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md),
  [ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md),
  [ADR-1239](1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md): account status,
  password policy and credential verification.
- Auth0 connections: database, social and enterprise connections, enabled per application; the `connection`
  authorization parameter and the `password-realm` grant.
