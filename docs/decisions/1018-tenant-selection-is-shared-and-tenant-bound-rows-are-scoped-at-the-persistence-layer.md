# 1018. Tenant selection is shared and tenant-bound rows are scoped at the persistence layer

- **Status:** Accepted
- **Date:** 2026-09-30
- **Deciders:** NCode Group
- **Refines:** [project-template ADR-0129](project-template/0129-browser-facing-requests-are-safe-by-construction.md)

## Context

The OpenID runtime resolves the tenant of a request as a first-class, pluggable concern: a static single tenant, or a
tenant derived from the request host or path. Request-derived tenancy doubles as a security, deployment and
infrastructure boundary, because a deployment reached at `tenant1.example.com` or `/tenant1/…` serves exactly one
tenant. The management API administers those same tenants and their children: clients and every other tenant-bound
resource.

Two forces shape the design.

- The protocol runtime and the management API must never disagree on which tenant a request is, so selection is one
  shared concern, separate from materializing the tenant's contexts
  ([ADR-1036](1036-one-factory-materializes-every-openid-context-for-a-request-an-addressed-tenant-or-a-job.md) holds materialization).
- A scope decision made after a row has been read is too late. A resource is addressed by a globally unique opaque
  identifier ([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)), so its tenant is knowable only by reading the
  row, and no check before the fetch can gate the specific resource. A logging statement, an early return or a later
  refactor between the read and the comparison can leak another tenant's data, and every call site repeats the risk.
  The fetch itself has to be scoped: "never fetch what you cannot see", not "fetch, then hide".

The management API's answer to a caller without authority is a separate rule,
[ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md). This record holds
persistence scoping only.

## Decision

**Tenant selection is one shared abstraction, and tenant-bound data access is scoped at the persistence layer so that
a cross-tenant row is never materialized.**

### Selection

`ITenantResolver` resolves a request to a `PersistedTenant`. It works over the pluggable `ITenantStrategy`
(static-single, dynamic-by-host, dynamic-by-path) and `TenantResolutionOptions`. Its contracts live in
`NCode.Identity.OpenId.Abstractions` and its default implementation in `NCode.Identity.OpenId.Core`. The protocol
runtime and the management API depend only on the abstraction and receive the implementation through dependency
injection
([ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md)),
so they cannot disagree on the tenant of a request.

### The ambient scope

`IAmbientTenantAccessor` (in `NCode.Identity.OpenId.Persistence.Abstractions`; its default,
`DefaultAmbientTenantAccessor`, in `NCode.Identity.OpenId.Core`) holds the tenant scope of the current flow.
`BeginScope(tenantId)` stores the identifier in an `AsyncLocal<T>`, so the scope flows across asynchronous calls and
reaches the `OpenIdDbContext` that an `IDbContextFactory` creates from the root provider, outside the current
dependency-injection scope.

The scope has three states.

- **Unset.** No scope is open. `TenantId` is `null` and `IsUnscoped` is `false`. This is the state of any code that
  has not chosen a scope, and it sees no tenant-bound row.
- **Tenant.** `BeginScope(tenantId)` opened a scope for one tenant. A tenant scope opened inside an unscoped one is a
  tenant scope.
- **Unscoped.** `BeginUnscoped(reason)` opened an explicit cross-tenant scope. `IsUnscoped` is `true` and
  `UnscopedReason` holds the reason, which is required and names why the code reads across tenants.

### The query filter

`OpenIdDbContext` applies, by interface, a global query filter to every `ISupportTenantEntity`. The filter is

```
IsUnscoped || (ambient != null && e.NormalizedTenantId == ambient)
```

that is, "the scope is unscoped, or the row's denormalized, indexed `NormalizedTenantId` equals the ambient tenant". It
is one indexed-column predicate, and because it is applied by interface, a newly added tenant-child entity is scoped
automatically and the framework stays decoupled from the concrete tenant entity
([ADR-1052](1052-entity-framework-persistence-is-a-reusable-framework-that-domain-slices-plug-into.md)).

With a tenant scope open, no query returns a row of another tenant, so the row is never materialized. With no scope
open, the filter matches no tenant-bound row, so a query that forgot to choose a scope returns nothing instead of every
tenant's rows. Only an unscoped scope reads across tenants.

`TenantEntity`, `TeamEntity` and `PlatformEntity` are not `ISupportTenantEntity`, so they stay unscoped and resolvable
by identifier. A tenant's child rows, its secrets included, are scoped like every other tenant child; tenant
resolution and tenant management read them inside an unscoped scope. The platform has one settings row and no secrets
([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)).

### The unscoped scope is the only cross-tenant path

`BeginUnscoped(reason)` is the only way to read or write across tenants. Every caller that does so opens it with a
reason: the background services, the tenant resolver, the tenant provisioner, migrations and design-time tooling. A
code path that needs another tenant's rows therefore says so, and the reason is visible in review and at run time.

### Opening the scope on the protocol surface

`OpenIdEnvironmentEndpointFilter`, on the protocol and interactive endpoint groups, resolves and materializes the
request's tenant inside an unscoped scope, then opens the ambient scope for that tenant for the rest of the request. Every
read on the protocol surface is therefore confined to the request's resolved tenant.

### Opening the scope in the management API

The management API's tenant-plane group (`/api/tenants/{tenantId}`) opens the scope from the route's `tenantId` value
through the internal `TenantScopeEndpointFilter`, independent of the request's resolved tenant (route-driven scope,
[ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md)). The filter answers `404` when the segment is missing.
It reads only the route value and makes no claim that the tenant exists. Control-plane routes (platform, team, caller,
capabilities and machine clients) carry no `{tenantId}`. They run under the internal `ControlPlaneScopeEndpointFilter`,
which opens `BeginUnscoped("management control plane")` for the request.

### Writes and reads of an existing resource

- A tenant-bound create takes its owning tenant from the scope, which is the route, never from the request body. The
  request carries no tenant identifier at all.
- Every read, update or delete of an existing tenant-bound resource loads it by identifier through the filter. The
  management API authorizes the caller at the route's node first
  ([ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)); to an authorized
  caller, a resource of another tenant is `404`, because the filter never loads it. The answer is given before
  validation runs, so the existence of a resource in another tenant cannot be probed.

### Failing closed

`OpenIdDbContext` requires an `IAmbientTenantAccessor`, so a missing registration fails at startup instead of running
unscoped. A scope that is unset matches no tenant-bound row, so a missed scope returns nothing and leaks nothing.
Direct construction in tests and tooling opens an explicit scope, a tenant scope or an unscoped one with a reason.

### The analyzer

The analyzer `NCID0004` (`CrossTenantQueryAnalyzer`, in `NCode.Identity.Analyzers`) reports an `IgnoreQueryFilters()`
call unless a `// Cross-tenant: <why>` comment sits on the line directly above the line that holds the call: above its
statement, or above its `.IgnoreQueryFilters()` line in a fluent chain. Bypassing the filter in a query is therefore
explained at the call site. The analyzer skips assemblies whose names end in `.Tests` or `.TestSupport`.

### Row-level security

Postgres enforces the same rule below the application. Every table of an `ISupportTenantEntity` has
`ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY` and one policy, `tenant_isolation`, for both `USING` and
`WITH CHECK`:

```sql
current_setting('app.unscoped', true) = 'on' OR "NormalizedTenantId" = current_setting('app.tenant_id', true)
```

- `RowLevelSecurity`, in the host database project, generates the statements from the EF model. The migration worker
  applies them after migrating, idempotently (it drops the policy if it exists, then creates it), and grants the runtime
  role the access that `RuntimeRoleGrants` creates for the cell's and the directory's databases alike: `USAGE` on the
  model's schemas, `SELECT, INSERT, UPDATE, DELETE` on its tables and `USAGE` on its sequences.
- `TenantSessionInterceptor`, a connection, command and transaction interceptor, sets `app.tenant_id` and
  `app.unscoped` with a `set_config` command of its own before any command whose ambient state differs from the state
  the connection holds. It forgets that state when the connection opens, because Npgsql resets a session with
  `DISCARD ALL` when its connection returns to the pool. State set inside a transaction holds only for that
  transaction's commands until the transaction commits, and a rollback, a rollback to a savepoint or a failure of the
  transaction clears it. A pooled connection never carries the previous request's tenant.
- Migrations run as the schema owner and the host runs as a separate runtime role. Both read one connection,
  `ConnectionStrings:openiddb` or the `OpenIdDatabase` parts
  ([ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md)),
  each given its own role's credentials: the runtime role's in the host's pods, and the schema owner's in the migration
  worker and the chart's Job. The worker also reads `HostDatabase:RuntimeRole`, which names the runtime role for the
  grants. The directory's Deployment and `migrate` Job read `ConnectionStrings:directory` or the `DirectoryDatabase`
  parts the same way, the Job with `DirectoryMigration:RuntimeRole`.
- `RuntimeRolePosture.Evaluate` refuses a runtime role that is a superuser, has `BYPASSRLS` or owns the tables, because
  such a role skips the policy. The host refuses to start on it under the deployment posture and warns under
  `LocalDevelopment:Enabled`
  ([ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)).

### Tenants of ASP.NET Core Identity and the BFF

- **Local accounts.** `LocalAccountEntity` and `LocalAccountClaimEntity` carry a foreign key to `TenantEntity`, shaped
  like `ClientEntity`: `TenantId`, the `Tenant` navigation and the denormalized `NormalizedTenantId`. The key restricts
  deletes, as every foreign key of `OpenIdDbContext` does, and the tenant store erases a tenant's accounts and their
  claims with it
  ([ADR-1212](1212-deleting-a-client-resource-server-principal-or-tenant-removes-what-it-owns.md)). The accounts are
  scoped by the same filter and policy.
- **Identity users.** An ASP.NET Core Identity user is bound to a tenant. `TUser` is `ITenantBoundUser` and exposes
  `TenantId`. `TenantLookupNormalizer` prefixes normalized names and emails with `{normalizedTenantId}|` and throws when
  no tenant scope is set. The host's Identity schema sizes `UserName` and `Email` to `AccountMaxLengths.UserName` and
  `AccountMaxLengths.Email`, and `NormalizedUserName` and `NormalizedEmail` to `AccountMaxLengths.NormalizedUserName`
  and `AccountMaxLengths.NormalizedEmail`, which add the 64-character tenant prefix. For an Entity Framework Core
  model, `HasTenantLookupLengths()` in the opt-in `NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore`
  package makes that one call, tested over a real `IdentityDbContext` model, while the adapter stays store-agnostic. The
  store stamps `TenantId` on create and treats a user of another tenant as not found, so one tenant's `alice` and
  another's are different users.
- **BFF client members.** A member of the `bff:clients` index is the UTC day of a session's absolute expiry and the
  client's digest, `<yyyy-MM-dd>:<digest>`, so the index names a client only by a digest. Every replica reads the same
  members. Each member has a value, `bff:client-member:<member>`, that holds the plaintext client and expires with the
  member, and retirement reads one value per member and no session. It leaves out the clients that its frontends are
  configured as, and a member whose value is missing or holds another client expires on its own.

## Options considered

- **A post-fetch boundary that compares a loaded resource's tenant to the request's tenant.** Rejected: it materializes
  the row, sensitive material included, before the scope decision, and the comparison repeats at every call site.
- **Tenant-scoped getters at each call site**, such as a lookup that takes the ambient tenant and the identifier.
  Rejected as the mechanism: it is correct only while every call site remembers the scope, and a single omission leaks.
- **A pre-fetch endpoint check on the resource's tenant.** Rejected: an opaque identifier does not reveal its tenant, so
  there is nothing to check before the row is read.
- **A blanket authorization handler over tenant-owned resources.** Rejected: it runs after the row is loaded, so the
  row, sensitive material included, is materialized before the scope decision.
- **The management API reusing the runtime's tenant provider.** Rejected: it drags the protocol runtime into the
  management API, against the one-capability package boundary of
  [ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md).
- **An ambient scope plus a global query filter by interface (chosen).** No query can return a cross-tenant row and no
  call site can forget the scope, while tenant resolution, tenant management and the control-plane routes read through
  an explicit unscoped scope.
- **A filter that is a no-op when no scope is open.** Rejected: a call site that forgets to open a scope reads every
  tenant's rows. The three-state scope makes an unset scope match nothing and makes cross-tenant code say why.
- **Query filters alone, with no database enforcement.** Rejected: a raw query, a missed `IgnoreQueryFilters()` or a
  bug in the filter is a cross-tenant read. Row-level security under a runtime role that cannot bypass it holds the
  same rule where the filter cannot be skipped, and `NCID0004` explains each deliberate bypass.
- **Sending `set_config` with the command it precedes, or once per physical connection.** Rejected: prefixed to the
  command's text, it runs in the command's implicit transaction, so a failure of the command, which can surface while
  its rows are read, undoes a setting the interceptor has recorded as applied; its row becomes the first result, which
  `ExecuteScalar` returns and a reader starts on; and a migration's statement becomes a multi-statement command, in
  which a statement such as `CREATE INDEX CONCURRENTLY` cannot run. Sent beside the command in an Npgsql batch, it
  makes the interceptor execute each such command itself and stand in for the reader, the scalar and the affected-row
  counts that EF Core reads. A physical-connection initializer or a connection-string option sets the session once per
  physical connection, so it cannot carry a request's tenant.

## Consequences

- A new tenant-bound resource family is isolated by making its entity an `ISupportTenantEntity`, with no per-endpoint
  check.
- A cross-tenant row is never read, so a later logging statement, early return or refactor has nothing to leak.
- The runtime and the management API share one selection path and cannot resolve different tenants for a request.
- A missing `IAmbientTenantAccessor` registration is a startup failure, never a silent loss of isolation.
- The filter needs the denormalized `NormalizedTenantId` column and an index on every tenant-child table.
- Code that reads tenant-bound rows without opening a scope sees nothing. The cross-tenant paths are the runtime's
  resolution, tenant management, the control-plane routes, background services and tooling, and each opens
  `BeginUnscoped` with a reason.
- Every `IgnoreQueryFilters()` call carries a `// Cross-tenant:` explanation, or the build reports `NCID0004`.
- The host needs two database credentials, the owner's for migrations and the runtime role's for serving, and it
  refuses a runtime role that bypasses row-level security under the deployment posture.
- Each pooled connection carries the ambient state of its current command, set by `TenantSessionInterceptor` at the
  cost of one round trip before the first command after the connection opens and one before a command under another
  ambient state. Outside an explicit transaction, EF Core opens the connection for each query and each `SaveChanges`,
  so each of them pays one.
- A tenant's Identity users and local accounts are isolated like every other tenant child, and the `bff:clients`
  members name each client only by a digest.

## References

- `NCode.Identity.OpenId.Abstractions`: `ITenantResolver`, `ITenantStrategy`, `TenantResolutionOptions`
- `NCode.Identity.OpenId.Persistence.Abstractions`: `IAmbientTenantAccessor`
- `NCode.Identity.OpenId.Core`: `DefaultAmbientTenantAccessor`
- `NCode.Identity.OpenId.Persistence.EntityFramework`: `OpenIdDbContext`, `ISupportTenantEntity`
- `NCode.Identity.OpenId.Management`: `TenantScopeEndpointFilter`, `ControlPlaneScopeEndpointFilter`
- `NCode.Identity.Analyzers`: `CrossTenantQueryAnalyzer` (`NCID0004`)
- `NCode.Identity.OpenId.Host.Database`: `RowLevelSecurity`, `TenantSessionInterceptor`, `RuntimeRolePosture`
- `NCode.Identity.Persistence.Abstractions`: `DatabaseRoleNameRules`, `PostgresIdentifiers`, `RuntimeRoleGrants`
- [ADR-1036](1036-one-factory-materializes-every-openid-context-for-a-request-an-addressed-tenant-or-a-job.md), [ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md),
  [ADR-1370](1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md),
  [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
  [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)
- [project-template ADR-0129](project-template/0129-browser-facing-requests-are-safe-by-construction.md), decision 5:
  the owning tenant comes from the context, never the request
