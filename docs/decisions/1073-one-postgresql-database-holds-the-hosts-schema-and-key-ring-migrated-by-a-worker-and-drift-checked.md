# 1073. One PostgreSQL database holds the host's schema and key ring, migrated by a worker and drift-checked

- **Status:** Accepted
- **Date:** 2026-10-11
- **Deciders:** NCode Group
- **Refines:** [project-template ADR-0143](project-template/0143-migrations-run-before-the-rollout-and-only-expand.md)

## Context

The reference host persists the OpenID model through Entity Framework Core. The same database holds the signing keys,
encrypted under the tenants' master keys, and the ASP.NET Core Data Protection key ring, which protects cookies and
antiforgery tokens. Several forces shape where the schema lives and who applies it:

- The published `NCode.Identity.OpenId.Persistence.EntityFramework` package is provider-neutral. Migrations written in
  it would couple every consumer to one provider and one schema history.
- Developer state is more than one file. The relational data, the tenants' master keys and the seeded signing keys they
  encrypt, and the Data Protection key ring, encrypted under the platform master key, must reset together; delete one
  without the others and a stored key becomes undecryptable. The root key that wraps the master keys stays outside the
  database ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)).
- A migration that fails inside the `api` process crashes the host. A dedicated resource makes the failure visible and
  isolated.
- A model change without its migration breaks the first deployment that needs it, so the build has to catch drift.
- [project-template ADR-0143](project-template/0143-migrations-run-before-the-rollout-and-only-expand.md) fixes the
  generic rules (a design-time factory, no migration at startup outside development, a drift check). This record
  places them in the family's projects. The rollout and expand-and-contract rules are
  [ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md)'s.

The standing posture is one cohesive story, not provider sprawl patched over time.

## Decision

PostgreSQL through Npgsql is the host's only relational provider, and a cell's one database, `openiddb`, holds the schema
and the key ring. The global directory has its own database. A worker applies the migrations as the schema owner, the
host never does and serves as a separate runtime role, and the build fails on drift.

- **One provider.** `NCode.Identity.OpenId.Host` resolves its database connection, the `openiddb` connection string or
  the `OpenIdDatabase` parts (`HostDatabaseConnection`,
  [ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md)),
  registers `OpenIdDbContext` with `UseOpenIdNpgsql`, and tags an `openiddb` `AddDbContextCheck<OpenIdDbContext>` with
  `HealthCheckTags.Readiness`. Without a database connection, as in hermetic test hosts that replace the context, the
  host uses an in-memory `OpenIdDbContext` and configures no Data Protection repository, which a deployment refuses.
- **One database per cell, and the directory's own.** A cell is a host, its database, its Valkey and its KMS keys
  ([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)),
  so every cell has its own `openiddb`, which holds every tenant of the cell. The global directory's host,
  `hosts/NCode.Identity.OpenId.GlobalDirectory.Host`, has its own Postgres database, `DirectoryDbContext`, with its own
  migrations and image. It holds cells, placements, team documents and retired identifiers, and no personal data. Its
  host's `migrate` command, which the chart's directory migration Job runs, migrates as the schema owner through
  `ConnectionStrings:directory-migration` and grants the runtime role that `DirectoryDatabase:RuntimeRole` names the
  access that `RuntimeRoleGrants` creates for a cell's database too (`DirectoryDatabaseGrants`); serving connects
  through `ConnectionStrings:directory` as that role, which owns nothing and can only read and write rows.
- **A migration role and a runtime role.** The two roles have separate connection strings: `ConnectionStrings:openid`
  is the runtime role's, and `ConnectionStrings:openid-migration` is the schema owner's, which only the worker uses.
  `HostDatabase:RuntimeRole` names the runtime role for the grants the worker applies
  ([ADR-1018](1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md)). The
  runtime role is not a superuser, has no `BYPASSRLS` and owns no table, so row-level security binds it.
- **A host-owned database project.** The published `NCode.Identity.OpenId.Persistence.EntityFramework` ships the model
  only. The unpacked `NCode.Identity.OpenId.Host.Database` holds the Npgsql wiring (`HostDatabase`, which points
  `MigrationsAssembly` at itself), the design-time factories `OpenIdDbContextDesignTimeFactory` and
  `DeveloperDataProtectionDbContextDesignTimeFactory`, so `dotnet ef` never boots the host, and both contexts'
  migrations.
- **The worker migrates, the host never does.** The host does not migrate on startup, in any environment.
  `NCode.Identity.OpenId.Host.MigrationService` applies both contexts' migrations through `MigrationRunner` as the
  schema owner, then applies row-level security and the runtime role's grants, and exits.
  The AppHost holds `api` with `WaitForCompletion(migrations)`, so a failed migration is a failed dashboard resource, and
  the chart's Job runs the same worker
  ([ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md)). Seeding stays in the
  host as idempotent seed handlers.
- **A persistent local database.** Locally the AppHost runs Postgres with the stable named volume
  `ncode-identity-openiddb-data`, so data survives restarts. `build/dev-db-reset.ps1`, which detects Docker or Podman,
  removes that volume; the next run recreates the schema and reseeds.
- **The key ring lives in the same database.** It is persisted with `PersistKeysToDbContext<DeveloperDataProtectionDbContext>()`
  under the application name `NCode.Identity.OpenId.Host`, in a host-owned context with its own migrations history in
  `openiddb`. One volume purge resets the data, the master keys and the ring together. The ring is never in
  `OpenIdDbContext`, so the published library imposes no `DataProtectionKeys` table.
- **A drift gate.** The `ef model drift` step of `build/dod.ps1` runs
  `dotnet ef migrations has-pending-model-changes --no-build` against the Database project, as project and startup
  project, for `OpenIdDbContext` and `DeveloperDataProtectionDbContext`. A model change without its migration fails
  pre-push and in CI.
- **Migrations are generated code.** Everything under `Migrations/` is `generated_code = true` in `.editorconfig` and
  listed in `.csharpierignore`. Hand-written types such as `ExpandOnlyRule` stay outside that folder.

## Options considered

- **Migrations in the published persistence library.** It couples the library to one provider. Rejected.
- **`EnsureCreated`.** It never evolves a schema. Rejected.
- **SQLite, or several providers.** SQLite is not a production-fidelity engine, and its per-process file cannot be
  shared with a dedicated migration process. Several providers mean a branch per provider. Rejected.
- **An ephemeral Postgres without a volume.** It resets every run, where developers want data to persist and to purge on
  demand. Rejected.
- **Data Protection keys in a file, or in `OpenIdDbContext`.** A file leaves two storage locations that a purge must keep
  in sync. A table in the shared context imposes `DataProtectionKeys` on every consumer of the library. Rejected.
- **A `HasPendingModelChanges` unit test.** The EF test project holds no model snapshot to compare. Rejected for the
  `dotnet ef` step in the Definition of Done.
- **One role for migrations and serving.** The host would hold the right to change its own schema and to drop the
  policy that binds it. Rejected.
- **One Postgres database, a host-owned Database project, a migration worker, a key ring in a host-owned context and a
  drift step in the gate.** Chosen.

## Consequences

- One production-fidelity local database; data persists across restarts; a single script purges data, signing keys and
  key ring consistently; a migration failure is a visible, isolated resource; and the drift gate keeps the migrations
  authoritative.
- A deployment holds two credentials for a cell's database, the owner's for the worker and the runtime role's for the
  host, so a host that is compromised cannot change its schema or step around row-level security. The directory's
  database has its own two credentials, the owner's for its migration Job and the runtime role's for its Deployment, so
  a compromised directory cannot change its schema either.
- The AppHost needs a container runtime (Docker or Podman). The host run standalone, without the orchestrator, uses an
  ephemeral in-memory database, so persistent local state requires the AppHost. A Postgres major-version upgrade can
  strand an old data volume; purge it with the reset script.
- Smells of a wrong choice: a provider branch returning to the host, a `DataProtectionKeys` table in `OpenIdDbContext`,
  or developer state under `App_Data/`.

## References

- [project-template ADR-0143](project-template/0143-migrations-run-before-the-rollout-and-only-expand.md), items 1, 2
  and 7, which this record implements.
- [ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md): the rollout, the
  expand-and-contract rule and the initial migration.
- [ADR-1071](1071-the-aspire-apphost-runs-the-local-stack-in-health-gated-order-after-checking-the-frontend-toolchain.md):
  the AppHost that runs the database and the worker.
- [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)
  and [ADR-1018](1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md): the
  cell, and row-level security under the runtime role.
- `hosts/NCode.Identity.OpenId.Host.Database`, `hosts/NCode.Identity.OpenId.Host.MigrationService`,
  `build/dev-db-reset.ps1`, `build/dod.ps1`.
- The Npgsql EF Core provider, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` `PersistKeysToDbContext`, and
  the Aspire PostgreSQL hosting integration (`WithDataVolume`, `WaitForCompletion`).
