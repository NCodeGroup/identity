# 1509. The host chart passes a Postgres operator's Secret as the connection's parts, and the host assembles the Npgsql connection

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** NCode Group

## Context

A Postgres operator such as CloudNativePG writes the parts of a connection to a Secret of its own, `<cluster>-app` for
CloudNativePG: `host`, `port`, `dbname`, `user`, `password`, `uri` and `jdbc-uri`. It writes no Npgsql connection
string. Two facts follow for a host that reads one finished connection string (issue
[#780](https://github.com/NCodeGroup/identity/issues/780)):

- **A hand-built copy goes stale.** A deployer who assembles an Npgsql string from the operator's parts and stores it in
  a Secret of their own holds a second copy of the password, and a rotation by the operator leaves the copy behind.
- **A hand-built copy cannot be declared in GitOps.** The string carries the password, so the deployer cannot commit it,
  and the chart cannot read the operator's Secret at render time, because `lookup` returns nothing under
  `helm template` and Argo CD.

The password is also a value that may hold `;`, `'` or `"`, characters that change the meaning of an Npgsql connection
string when they are spliced into one as text.

Two hosts of the family have a Postgres database of their own: the reference OP host, whose database is the cell's
`openiddb`, and the global directory host, whose database holds the directory alone
([ADR-1073](1073-one-postgresql-database-holds-the-hosts-schema-and-key-ring-migrated-by-a-worker-and-drift-checked.md)).
Each is deployed by the same chart and reads its connection the same way.

## Decision

**The chart passes each part of a connection to the pods from the operator's Secret, and each host assembles the
connection string from the parts in one shared rule, `PostgresConnectionRules`, with `DbConnectionStringBuilder`, so a
password stays a password whatever characters it holds.**

1. **Two forms of one connection.** Each host's database is one connection in two forms: a finished string under
   `ConnectionStrings:<name>`, and the parts under a configuration section (`Host`, `Port`, `Name`, `Username`,
   `Password`). The reference OP host reads `ConnectionStrings:openiddb` or the `OpenIdDatabase` parts through
   `HostDatabaseConnection`, which the host and its migration worker both use. The directory host reads
   `ConnectionStrings:directory` or the `DirectoryDatabase` parts through `DirectoryDatabase.GetConnectionString`, which
   its replicas and its `migrate` command both use. Both call `PostgresConnectionRules.Resolve` in
   `NCode.Identity.Persistence.Abstractions` with the connection string's name and the section's name, so the two forms
   are judged and assembled in one place. The rule resolves the connection from whichever form is configured. It
   refuses both forms at once, a parts set that lacks `Host`, `Name`, `Username` or `Password`, and a `Port` outside
   1-65535, and its messages name configuration paths and never a configured value. The section binds strictly, so a
   misspelled part stops the host
   ([ADR-1137](1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md)).
   It assembles the parts with `DbConnectionStringBuilder` under the keys `Host`, `Database`, `Username`, `Password` and
   `Port`, the base class that `NpgsqlConnectionStringBuilder` parses with, so Npgsql reads back the values that were
   configured. `Port` is optional and Npgsql defaults it to 5432. The directory host stops at start when neither form is
   configured.
2. **The chart passes the parts as environment variables.** `database.parts` and `migrations.database.parts` each name
   a Secret (`existingSecret`) and the keys that hold the host, port, database, user name and password. The chart
   renders each part as an `OpenIdDatabase__*` environment variable from a `secretKeyRef`, and a finished string as
   `ConnectionStrings__openiddb`. The keys of `database.parts` default to CloudNativePG's (`host`, `port`, `dbname`,
   `user`, `password`), so a CloudNativePG cluster needs only the Secret's name. The keys of `migrations.database.parts`
   fall back to those of `database.parts`. `directory.database` and `directory.migrations.database` have the same
   shape and render as `DirectoryDatabase__*` or `ConnectionStrings__directory`: the keys of `directory.database.parts`
   default to CloudNativePG's, and the keys of `directory.migrations.database` fall back to those of
   `directory.database`.
3. **Each workload gets one connection with its own role's credentials.** The OP's `database` is the connection of the
   runtime role, which the pods read, and `migrations.database` is the connection of the schema owner, which only the
   migration Job reads. The pods and the Job both read `ConnectionStrings:openiddb` or the `OpenIdDatabase` parts, each
   from its own Secret. The runtime role cannot change the schema, and the migration Job grants it access to the tables,
   so `database.runtimeRole` names the role and feeds `HostDatabase__RuntimeRole` (`HostDatabase:RuntimeRole`) in the
   Job. `database` is always required, `migrations.database` is required while `migrations.enabled` is true, and the
   Job never reads `database`. The migration worker applies row-level security under the owner, and the host connects
   as the runtime role, which does not bypass it. The directory has the same two credentials: `directory.database` is
   the connection of the runtime role, which the Deployment reads, and `directory.migrations.database` is the
   connection of the schema owner, which only the `migrate` Job reads, each as `ConnectionStrings:directory` or the
   `DirectoryDatabase` parts. `directory.database.runtimeRole` names the role and feeds
   `DirectoryMigration__RuntimeRole` (`DirectoryMigration:RuntimeRole`) in the Job. Both directory connections are
   required while `directory.enabled` is true, because the Job always runs, and the Job never receives the runtime
   role's connection.
4. **The chart refuses an unusable release.** For `database`, and for `migrations.database` while migrations are
   enabled, it refuses both `existingSecret` and `parts.existingSecret`, a connection that names neither form, and a
   parts Secret whose host, database, user name or password key is empty. It refuses an empty `database.runtimeRole`
   while migrations are enabled, and one that is not a lower-case Postgres role name. For `directory.database` and
   `directory.migrations.database` it refuses the same while `directory.enabled` is true, and an empty or malformed
   `directory.database.runtimeRole`. The migration Job and the server may use different forms.
5. **A rotation reaches the pods at the next pod start.** A `secretKeyRef` environment variable is read when a
   container starts, so a password the operator rotates reaches the pods when they restart.

## Options considered

- **Kubernetes `$(VAR)` expansion into a finished string.** Rejected: the expansion splices the password into the string
  as text, and a password with `;`, `'` or `"` changes the string's meaning.
- **Npgsql's `PG*` environment defaults.** Rejected: the host reads an empty connection string as "no database", so the
  defaults would reach Npgsql only when something else supplied a string, and nothing would refuse a release that mixes
  the two.
- **A chart-made Secret copy, through `lookup` or a hook.** Rejected: `lookup` returns nothing under `helm template`
  and in GitOps, so the chart would render differently from how it installs, and a copy goes stale when the operator
  rotates the password.
- **The host assembles the connection from the parts.** Chosen: the Secret the operator owns stays the only copy, the
  builder quotes every value, and `PostgresConnectionRules` refuses a mixed or partial configuration at host start as
  the chart does at render time.
- **Each host judges its own connection.** Rejected: the two hosts would judge the same two forms in two places, and
  their refusals and messages would drift apart. One rule in `NCode.Identity.Persistence.Abstractions`, which already
  holds the Postgres role-name and grant rules, serves both.

## Consequences

- No Secret is hand-built for the database: a deployer names the operator's Secret for the owner and a Secret of the
  same shape for the runtime role, and a deployer without an operator keeps the finished-string form in
  `database.existingSecret` and `migrations.database.existingSecret`, or in `directory.database.existingSecret` and
  `directory.migrations.database.existingSecret` for the directory.
- TLS settings the connection needs, such as `SSL Mode`, arrive through `extraEnv`.
- The `cluster` job of the `Host chart` workflow creates a Secret of the shape an operator writes for the owner and one
  for the runtime role, creates the role in the kind Postgres, and installs the kind release with the parts form, so the
  form is proven against a real database and the migration Job.
- The `Host chart` workflow refuses a directory that names both forms or a parts Secret with an empty key, and renders
  the directory's Deployment and Job with each form, so the environment each workload receives is checked.
- The Aspire AppHost supplies the owner's finished `ConnectionStrings:openiddb` to the migration worker and the runtime
  role's `OpenIdDatabase` parts to the hosts, so each host assembles its connection with the password quoted.

## References

- Issue [#780](https://github.com/NCodeGroup/identity/issues/780)
- [ADR-1458](1458-the-host-chart-deploys-the-server-and-an-opt-in-management-console-and-refuses-unsafe-releases.md)
- [ADR-1180](1180-each-host-contexts-migrations-run-before-the-rollout-and-only-expand-the-schema.md)
- [ADR-1073](1073-one-postgresql-database-holds-the-hosts-schema-and-key-ring-migrated-by-a-worker-and-drift-checked.md)
- `charts/ncode-identity-host`
- `src/NCode.Identity.Persistence.Abstractions/PostgresConnectionRules.cs`
