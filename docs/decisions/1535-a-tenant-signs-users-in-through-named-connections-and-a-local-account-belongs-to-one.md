# 1535. A tenant signs users in through named connections, and a local account belongs to one

- **Status:** Proposed
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

An end user reaches a tenant through some authority: this server's own credential check, a social provider, an
enterprise IdP or a scheme the host registers. Each sign-in yields an identity that resolves to one tenant-scoped
principal
([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)), and
a local account is the identity whose issuer is this server
([ADR-1051](1051-a-local-account-is-a-self-issued-connection-behind-one-pluggable-store-seam.md)). The tenant has no
record of the authorities it accepts. The local issuer is one constant, so a tenant holds one namespace of local
accounts behind the one `ILocalAccountStore` its host registers. A user name is unique in that namespace and an email is
not unique at all. The client setting `allowed_identity_providers` names the identity providers a client accepts, but
nothing defines those names.

The forces:

- **Separate groups of users in one tenant.** A tenant serves employees and customers, or consumers and partners, with a
  password policy, sign-up rules and identifier rules for each. The same email can belong to a person in each group. A
  second tenant also separates them, but splits the clients, resource servers, roles and keys they share.
- **Several backends.** One group lives in the server's own store and another in an LDAP directory, an ASP.NET Core
  Identity database or a legacy API, each behind the `ILocalAccountStore` seam.
- **Enablement per client.** A client accepts the groups and upstream IdPs it was built for, fail-closed, as
  `allowed_identity_providers` already merges
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

| Member         | Meaning                                                                                                                                                                                                                        |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ConnectionId` | Opaque, server-minted with `ICryptoService.GenerateResourceId()` ([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)), never reissued                                 |
| `Name`         | Operator-chosen, unique per tenant, validated like a resource identifier; what clients, requests and the sign-in page name                                                                                                     |
| `Strategy`     | A registered strategy name: `local`, and [#64](https://github.com/NCodeGroup/identity/issues/64)'s `oidc`, `saml` and `wsfed`                                                                                                  |
| `IsEnabled`    | A disabled connection signs no one in and its identities fail subject validation, as a disabled account does ([ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md)) |
| `Options`      | The strategy's own payload, validated by its descriptor and stored with the schema-versioned serializer                                                                                                                        |
| `Settings`     | A settings document at the `connection` scope (see 5)                                                                                                                                                                          |

A strategy is an `IConnectionStrategy` descriptor, contributed through `ICollectionDataSource<>` like settings and
scopes. It names its kind, validates its `Options`, and says how a sign-in through it is verified. Composition rejects
duplicate strategy names. A host that delegates to its own ASP.NET Core authentication scheme registers a strategy whose
options name that scheme, so every sign-in comes through a connection.

Every tenant is created with one enabled local connection named `local`, which every client accepts by default.

The management API serves `/tenants/{tenantId}/connections` and `/tenants/{tenantId}/connections/{connectionId}`,
authorized at the tenant's node
([ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)).
A connection that still holds identities or local accounts is not removed.

### 2. An identity is keyed by its connection

`FederatedIdentity` is unique on `(tenant, ConnectionId, subject)`. It records the upstream issuer of an external
connection for audit and display, and that issuer is not part of the key. A local connection's subject is the local
account's id.

The connection comes from the path that authenticated the user, never from a claim. The interactive verifier, the
password grant and each strategy's callback attach the `ConnectionId` to the `ClaimsPrincipal` they produce, under a
claim type the server owns, and strip any claim of that type the upstream supplied. Principal resolution looks up
`(ConnectionId, subject)`. A `ClaimsPrincipal` with no connection claim is not provisioned, as a principal without a
subject or issuer is not provisioned today
([ADR-1035](1035-an-end-user-is-one-tenant-scoped-principal-that-every-authentication-entry-point-resolves-to.md)). The
`idp` that subject validation checks is the connection's `Name`.

### 3. A local account belongs to one local connection

`LocalAccountEntity` carries its connection, and its indexes are:

| Index                                      | Unique | Purpose                                                          |
| ------------------------------------------ | ------ | ---------------------------------------------------------------- |
| `(tenant, LocalAccountId)`                 | yes    | The opaque id is the subject and the management address          |
| `(tenant, connection, NormalizedUserName)` | yes    | Sign-in by user name within the connection                       |
| `(tenant, connection, NormalizedEmail)`    | yes    | Sign-in by email and one account per email within the connection |

Both name columns are nullable and a null never conflicts, so a connection may do without user names or without emails.
The connection setting `local_account_identifier` is `email`, `user_name` or `email_or_user_name`, and decides which
identifier an account must have and which one sign-in looks up. `ILocalAccountStore` gains `GetByEmailOrDefaultAsync`.
Credential verification keeps its constant cost whether or not the account exists
([ADR-1239](1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md)) and its per-account
attempt limits
([ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md)).

### 4. Each local connection names its store

A store registration (`AddLocalAccountEntityFramework()`, `AddAspNetIdentityLocalAccountStore<TUser>()`, a custom one)
registers a named `ILocalAccountStore`, and a local connection's options name one. Each operation of the store runs for
one connection, which the store receives with its context. Registration still returns
`IServiceBuilder<LocalAccountLibrary>`, so a capability without a store remains a compile error
([ADR-1069](1069-di-registration-proves-every-prerequisite-at-compile-time-through-marker-builders.md)), and a
connection that names an unregistered store is refused when it is written. The ASP.NET Core Identity adapter's lookup
normalizer prefixes `{normalizedTenantId}|{normalizedConnectionId}|`, so Identity's own unique index on
`NormalizedUserName` is unique per connection.

### 5. `connection` is a setting scope beneath `tenant`

The accounts package registers the `connection` scope with `tenant` as its parent
([ADR-1149](1149-settings-declare-the-scopes-they-apply-to-and-the-scopes-are-a-registered-cascade.md)). The password
policy ([ADR-1236](1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md)), attempt limits, sign-up, identifier
and federated-identity linking settings apply to it, so a connection narrows the tenant's values through each setting's
merge. A sign-in reads its connection's effective settings.

### 6. A client enables connections

- `allowed_connections` applies to clients, lists connection names and merges with `Intersect`. Unset, it allows every
  enabled connection; set, it allows only those it lists, fail-closed. It is a client's only allow-list of the
  authorities it accepts: no other setting names identity providers, and subject validation, the interactive surface and
  the password grant all read it
  ([ADR-1010](1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md)).
- `default_connection` applies to tenants and clients and names the connection used when a request names none.
- An authorization request selects a connection with the `connection` parameter or an `idp:<name>` value in
  `acr_values`. Without one, the interactive surface offers every connection the client allows and preselects the
  default.
- The password grant takes `connection` and otherwise uses the default. It accepts only a local connection.
- A connection the client does not allow is refused with `invalid_request` before any credential is read.

### 7. People in several connections

One person signing in through two connections has two identities. `IFederatedIdentityLinkingPolicy` decides whether the
second attaches to the first one's principal. Its settings apply at the connection scope, so a tenant that keeps
employees and customers apart sets `federated_identity_explicit_only` on one of them, and a tenant whose users sign in
through Google and a local account keeps linking by verified email. `sub` is the `PrincipalId` on every path.

## Options considered

| Force                                                                | A. One namespace per tenant | B. Local databases only                           | C. Connections keyed by issuer  | D. Connections keyed by connection |
| -------------------------------------------------------------------- | --------------------------- | ------------------------------------------------- | ------------------------------- | ---------------------------------- |
| Groups with their own policy                                         | Only as separate tenants    | Yes                                               | Yes                             | Yes                                |
| Several backends in a tenant                                         | No                          | Yes                                               | Yes                             | Yes                                |
| Enablement per client                                                | No                          | A second list beside `allowed_identity_providers` | Yes                             | Yes                                |
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
- ADR-1051 and ADR-1035 are rewritten in place to match this record when it is accepted.
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
