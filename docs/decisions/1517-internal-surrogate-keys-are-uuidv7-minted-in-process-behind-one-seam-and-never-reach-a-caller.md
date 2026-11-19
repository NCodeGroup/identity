# 1517. Internal surrogate keys are UUIDv7 minted in-process behind one seam, and never reach a caller

- **Status:** Accepted
- **Date:** 2026-11-18
- **Deciders:** NCode Group
- **Refines:** [project-template ADR-0123](project-template/0123-resource-ids-are-server-generated-and-opaque.md), [project-template ADR-0124](project-template/0124-collection-endpoints-use-keyset-pagination.md)

## Context

Every Entity Framework entity of a cell and every record of the global directory needs an internal surrogate key that
stays unique across every process that writes rows, an offline minter included: an import job, a seed script, a
disconnected node. The key is minted without coordination and without a database round trip. A time, node and sequence
key needs a registry of node identifiers and caps the number of minters, and a database-generated key needs the
database.

Public identifiers come from `ICryptoService.GenerateResourceId()`
([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)), and an
identifier is never a capability
([ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md); the
tenant query filter). Keyset pages order by the key, and PostgreSQL compares `uuid` values bytewise. A UUIDv7 carries
the Unix millisecond it was minted, so a key that escapes through a cursor, a log line, an exception message or a
response dates its row, a person's included.

## Decision

**Every internal surrogate key is a `Guid` holding a UUIDv7 that any process mints in-process through one replaceable
seam, `ISurrogateKeyGenerator`; a surrogate key never reaches a caller, because every keyset cursor is encrypted and
authenticated, an analyzer fails a contract type that carries a `Guid`, and log and exception messages name public
identifiers only.**

1. **One key type and scope.** Every internal surrogate key is a `Guid` holding a UUIDv7, person-linked tables included
   (`LocalAccountEntity`, `FederatedIdentityEntity`, `FederatedPrincipalEntity`), with no UUIDv4 exception. The scope is
   every Entity Framework entity of a cell that carries `[SurrogateKey]`, `LocalAccountEntity`,
   `LocalAccountClaimEntity`, every foreign key to them, and the directory's `TenantRecord`, `TeamRecord`,
   `TenantMoveOfferRecord` and `RetiredIssuerRecord`. These values are not surrogate keys and stay `long`:
   `PlatformRecord.Id` and `DirectorySequenceRecord.Id` (the constant `SingletonId` of a one-row table),
   `DirectoryChangeRecord.Sequence`, every `DirectorySequence` and `AppliedSequence` (the published change-feed
   position), `TenantUsageCheckpointEntity.LastBatchNumber` and `TenantUsageEntity.Count`.
2. **One replaceable seam.** `ISurrogateKeyGenerator` has one member, `Guid CreateKey()`, and lives in
   `NCode.Identity.Persistence.Abstractions`, namespace `NCode.Identity.Persistence`. Its `internal sealed`
   `DefaultSurrogateKeyGenerator` returns `Guid.CreateVersion7()`. The public `AddSurrogateKeyGenerator()` extension of
   `IServiceCollection` registers it with `TryAddSingleton`, and both `AddEntityFrameworkPersistenceServices` and
   `AddOpenIdDirectoryServer` call it. A host or a test replaces the generator with
   `Replace(ServiceDescriptor.Singleton<ISurrogateKeyGenerator, T>())` or by registering one first. The default lives in
   the abstractions package because the two implementations that mint keys, the Entity Framework framework and the
   directory server, must not reference each other
   ([ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md),
   decision 3), and the default is the form rule of the key. A key's time is the wall clock, so no `TimeProvider`
   takes part. PostgreSQL 18's `uuidv7()` is not the generator.
3. **Entity Framework mints through the seam.** The public `SurrogateKeyAttribute` marks a `Guid` key property. An
   internal `SurrogateKeyConvention`, added in `OpenIdDbContext.ConfigureConventions`, sets `ValueGenerated.Never` and a
   value generator from data annotation, and throws `InvalidOperationException` when the marked property is not `Guid`.
   The internal `SurrogateKeyValueGenerator` returns the key of the `ISurrogateKeyGenerator` it resolves from the
   context's services each time it runs, because Entity Framework caches the model across service providers and a
   convention or generator never captures a service. Stores mint keys explicitly: `BaseStore.NextId()` returns
   `KeyGenerator.CreateKey()`.
4. **Keys order by their bytes.** Queries keep `entity.Id > afterId` and `OrderBy(entity => entity.Id)` over `Guid`.
   .NET's `Guid.CompareTo`, Entity Framework InMemory, Entity Framework SQLite and PostgreSQL `uuid` all order UUIDv7
   keys by their big-endian bytes, so the millisecond leads the order. Keys minted in one millisecond carry random
   bits, so they are not in insertion order; the order is total and stable, which keyset paging needs. A test that
   asserts insertion order uses a generator whose timestamp advances one millisecond per key.
5. **A cursor is encrypted and authenticated.** `IPageCursorProtector`, in `NCode.Identity.Persistence.Abstractions`,
   namespace `NCode.Identity.Persistence.Stores`, protects the position of a page as text with `Protect` and
   `TryUnprotect`, each taking the collection name. `TryUnprotect` writes the position into the caller's `Span<byte>`,
   reports its length through `bytesWritten` and returns `false` when the cursor does not unprotect or the position
   does not fit. Its extensions `ProtectKey` and `UnprotectKey` carry a `Guid` as 16 big-endian bytes, and
   `UnprotectKey` returns `null` for a null or empty cursor, a failed unprotect or a position that is not 16 bytes.
   `ProtectKey` and `UnprotectKey` are the form rule of a surrogate key's cursor position and live beside the
   contract, in `PageCursorProtectorExtensions`. The default, `DefaultPageCursorProtector`, uses ASP.NET Core Data
   Protection with the purpose `NCode.Identity.Persistence.PageCursor` and the collection name, base64url-encodes the
   protected bytes and keeps one protector per collection. A collection name is the entity type name; `AuditRecordStore`
   carries a 24-byte position, the 8-byte big-endian UTC ticks and then the 16-byte big-endian key. The framework
   registers the protector with `TryAddSingleton` and never calls `AddDataProtection()`: the key ring is the host's,
   shared by every replica and encrypted at rest under the platform master key
   ([ADR-1247](1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md)),
   and a ring the framework created would differ per process. Data Protection is built for short-lived opaque payloads
   and rotates; the secret protector is asynchronous and owner-scoped for stored secrets. A cursor that is malformed,
   tampered with, protected for another collection or under another key ring decodes to nothing, and the list restarts
   ([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)).
6. **An analyzer fails a contract that carries a `Guid`.** `NCID0005`, `SurrogateKeyExposureAnalyzer` in
   `analyzers/NCode.Identity.Analyzers`, reports as an error a public property or field, on a type whose containing
   types are all public, in a namespace with a segment equal to `Contracts` or `DataContracts`, whose type is or
   contains `System.Guid` (nullable, array element or generic argument). Every build loads the repository's analyzers
   ([ADR-1486](1486-the-repositorys-own-analyzer-project-fails-the-build-on-a-field-an-auto-property-could-replace.md)),
   so a contract fails the build and shows in the IDE where it is written; an architecture test runs only in the test
   stage and only over the assemblies its project references. The analyzer skips assemblies whose names end in `.Tests`
   or `.TestSupport`. Every surrogate key is a `Guid` and every resource identifier an opaque string, so a `Guid` on a
   contract is a surrogate key unless it says otherwise: the four `Guid` public identifiers
   (`AuditRecordResource.EventId`, `PersistedAuditRecord.EventId`, `EncryptionKeyResource.KeyId` and
   `PersistedMasterKey.KeyId`) carry a `[SuppressMessage]` for `NCID0005` with a justification.
7. **Logs and exception messages name public identifiers only.** No log message or exception message of a persistence
   slice or the directory store interpolates a surrogate key. A review rule and an `/audit-conventions` sweep keep it
   so.
8. **Public identifiers are unchanged.** `ICryptoService.GenerateResourceId()` mints every public resource identifier
   ([ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md)). A
   client-minted identifier is never trusted as a resource identifier; offline minting applies to internal keys only.

## Options considered

- **A snowflake with assigned node identifiers.** It needs a node registry or a lease, caps the minters at 1024, mints
  silent duplicates when a node identifier is wrong, and leaks the millisecond, the node and the sequence, which make
  neighbouring keys guessable. Rejected.
- **UUIDv4.** Random inserts fragment the B-tree. Rejected.
- **UUIDv4 for person-linked tables only.** Two policies for one concern; a creation time is usually already in audit
  records and created-at columns, and the key never leaves the persistence layer. Rejected.
- **Database-generated keys, an identity column or PostgreSQL 18's `uuidv7()`.** No offline minting. Rejected.
- **A cursor that carries the key in clear, or signed only.** It discloses the key and its time. Rejected.
- **An architecture test instead of an analyzer.** It runs only in the test stage and only over the assemblies its
  project references, where the analyzer fails every build and shows in the IDE. Rejected.
- **The chosen design.** UUIDv7 minted in-process behind one seam, with the exposure guards.

## Consequences

- Any process mints keys offline, without a registry, a lease or a database round trip.
- A key is 16 bytes.
- A cursor is opaque, about 130 characters, bound to its collection and readable only under the host's key ring. A
  cursor whose ring key was revoked restarts the list.
- Every Entity Framework store that pages needs Data Protection registered, which a server's secret protection does.
- A replacement generator must keep keys time-ordered UUIDv7.
- Smells: a `long` key, a `Guid` on a contract without a justified suppression, a surrogate key in a log or exception
  message.

## References

- [project-template ADR-0123](project-template/0123-resource-ids-are-server-generated-and-opaque.md) — server-generated
  opaque resource identifiers
- [project-template ADR-0124](project-template/0124-collection-endpoints-use-keyset-pagination.md) — keyset pagination
- Issue [#843](https://github.com/NCodeGroup/identity/issues/843)
- [ADR-1014](1014-management-resources-carry-opaque-server-identifiers-and-list-one-keyset-page-at-a-time.md) — public
  identifiers and pages
- [ADR-1052](1052-entity-framework-persistence-is-a-reusable-framework-that-domain-slices-plug-into.md) — the Entity
  Framework persistence framework
- [ADR-1247](1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md) —
  the key ring that protects cursors
- [ADR-1417](1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md) —
  an identifier is never a capability
- [ADR-1500](1500-every-package-names-one-capability-under-the-ncode-identity-prefix-as-an-abstractions-and-implementation-pair.md)
  — the abstractions and implementation pair
- [RFC 9562](https://www.rfc-editor.org/rfc/rfc9562) — UUID version 7
- `ISurrogateKeyGenerator`, `AddSurrogateKeyGenerator()`, `SurrogateKeyAttribute`, `SurrogateKeyConvention`,
  `IPageCursorProtector`, `DefaultPageCursorProtector`, `SurrogateKeyExposureAnalyzer` (`NCID0005`)
