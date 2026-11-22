# 1519. Every Microsoft.Extensions, ASP.NET Core and EF Core package is on the target framework's release band

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group
- **Refines:** [project-template ADR-0008](project-template/0008-external-inputs-are-pinned-and-kept-current.md)

## Context

The family targets `net10.0`, the LTS release. A host runs on the `Microsoft.AspNetCore.App` 10 shared framework, which
carries the Microsoft.Extensions assemblies of the 10.0 release. Microsoft.Extensions 11.0 packages carry a `net10.0`
asset, while ASP.NET Core 11 and EF Core 11 packages target `net11.0` alone. A package above the shared framework's
version wins over it and ships app-local, so the app runs one band's assemblies beside another's, a combination
neither release tests, outside the runtime's servicing. A dependency's floor sets the version for every project that
references it, so a dependency release built against a higher band lifts its band packages onto that band in every host
that takes it; Dependabot's major group proposes the next band.

## Decision

**Every package of the band, meaning the ids starting `Microsoft.Extensions.`, `Microsoft.AspNetCore.` and
`Microsoft.EntityFrameworkCore`, and `Npgsql.EntityFrameworkCore.PostgreSQL`, `System.Formats.Cbor`,
`System.Security.Cryptography.Xml` and `System.Text.Json`, resolves at the major version of the project's target
framework, and no package is exempt.**

1. `Directory.Packages.props` pins the .NET release's packages at one patch of the band, the newest past the cooldown;
   dotnet/extensions packages keep their own minor within the major.
2. A dependency is taken from a release whose target depends on the band, never forced by a pin or a suppressed
   downgrade warning. A dependency release that depends on another band is not taken: the dependency stays at its newest
   release on the band, an `ignore` rule in `.github/dependabot.yml` holds it there with a comment that names the issue
   tracking its release on the band, and the fix is that release. NCode.PropertyBag, the family's own package, is on the
   band: its `net10.0` target depends on `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.15.
3. The `RequireRuntimeBand` target of `Directory.Build.targets` fails the build of a .NET project that compiles against
   a band package of another major, naming each package and version, and admits no exception.
4. Dependabot ignores semver-major updates of these packages: the band moves with the target framework, as one planned
   migration.

## Options considered

- **Take the 11.0 Microsoft.Extensions band on net10.0.** Rejected: ASP.NET Core and EF Core 11 need net11.0, so it
  widens the mix.
- **Suppress the downgrade and pin 10.0 under a dependency that needs 11.0.** Rejected: an assembly built against
  11.0.0.0 fails to load against 10.0.0.0.
- **Move to net11.0.** Rejected: an STS release, and a target-framework move is its own migration.
- **Admit a dependency's band packages off the band through a list of exceptions.** Rejected: a listed package ships
  app-local beside the shared framework, the mix this decision rules out, and a list is never needed: the dependency's
  newest release on the band, held by a Dependabot `ignore` rule, keeps every project on one band while its release on
  the band is made.
- **One band per target framework, enforced by the build (chosen).**

## Consequences

- A host's Microsoft.Extensions, ASP.NET Core and EF Core assemblies come from one release and its servicing.
- A dependency release that lifts a band package fails the build of every project that references it, so it is not
  taken: the dependency stays at its newest release on the band, and the fix is a release of it on the band, made in its
  own repository.
- Moving to the next band changes the target framework, the pins and the ignore rule together.

## References

- `Directory.Packages.props`, `Directory.Build.targets` (`RequireRuntimeBand`), `build/tests/build-guards.tests.ps1`,
  `.github/dependabot.yml`
- [NCodeGroup/PropertyBag ADR-1002](https://github.com/NCodeGroup/PropertyBag/blob/main/docs/decisions/1002-the-packages-target-only-supported-lts-dotnet-versions.md)
- [project-template ADR-0009](project-template/0009-new-dependency-versions-wait-a-cooldown.md)
- Issue [#895](https://github.com/NCodeGroup/identity/issues/895)
