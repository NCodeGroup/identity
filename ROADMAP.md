# Roadmap

What NCode.Identity intends to do over the next year. Open issues hold the detail and the current state; this page is
the direction. It is reviewed at least once a year and when a release changes it.

## Scope

NCode.Identity covers what [README.md](README.md) describes. A feature outside that starts as an issue, where the
maintainers say whether it fits.

## Next Twelve Months

- **First stable release.** Retire the pre-release posture ([#535](https://github.com/NCodeGroup/identity/issues/535)),
  publish signed packages from a release tag ([#184](https://github.com/NCodeGroup/identity/issues/184)), and give every
  published package a readme ([#849](https://github.com/NCodeGroup/identity/issues/849)).
- **Certification.** OpenID Foundation conformance for the OpenID Connect and FAPI profiles
  ([#124](https://github.com/NCodeGroup/identity/issues/124),
  [#761](https://github.com/NCodeGroup/identity/issues/761)), on an ephemeral test environment
  ([#132](https://github.com/NCodeGroup/identity/issues/132)).
- **Hosted platform.** The deployment artifacts the platform runs
  ([#87](https://github.com/NCodeGroup/identity/issues/87)), its KMS root key
  ([#177](https://github.com/NCodeGroup/identity/issues/177)), tenant hosts under wildcard DNS
  ([#755](https://github.com/NCodeGroup/identity/issues/755)), managing a fleet of cells
  ([#845](https://github.com/NCodeGroup/identity/issues/845)), and a second region
  ([#837](https://github.com/NCodeGroup/identity/issues/837)).
- **Keys and secrets.** More root key providers ([#173](https://github.com/NCodeGroup/identity/issues/173)), on-demand
  rotation of a tenant's keys ([#877](https://github.com/NCodeGroup/identity/issues/877)), and a bound on how long a
  backup keeps a deleted tenant crypto-shreddable ([#878](https://github.com/NCodeGroup/identity/issues/878)).
- **Extensibility.** A claim-shaping pipeline ([#43](https://github.com/NCodeGroup/identity/issues/43)), enterprise
  connections to upstream identity providers ([#64](https://github.com/NCodeGroup/identity/issues/64)), rate limits for
  every endpoint group ([#834](https://github.com/NCodeGroup/identity/issues/834)), and a durable job dispatcher
  ([#25](https://github.com/NCodeGroup/identity/issues/25)).
- **Cryptography.** Algorithms beyond RFC 7518 ([#66](https://github.com/NCodeGroup/identity/issues/66)), wider COSE and
  CWT coverage ([#871](https://github.com/NCodeGroup/identity/issues/871)), and post-quantum key management as its
  standards settle ([#338](https://github.com/NCodeGroup/identity/issues/338),
  [#178](https://github.com/NCodeGroup/identity/issues/178)).
- **Supply chain.** Keep external inputs pinned and current
  ([project-template ADR-0008](docs/decisions/project-template/0008-external-inputs-are-pinned-and-kept-current.md)),
  resume Dependabot's version updates ([#36](https://github.com/NCodeGroup/identity/issues/36)), and raise the OpenSSF
  Scorecard score where the repository controls it
  ([project-template ADR-0020](docs/decisions/project-template/0020-github-projects-are-scored-by-openssf-scorecard.md)).
- **OpenSSF Best Practices.** Reach the silver level, which needs a second owner for continuity
  ([GOVERNANCE.md](GOVERNANCE.md#continuity), [docs/best-practices-badge.md](docs/best-practices-badge.md)).

## Not Planned

- A public API that weakens a security control below its safe baseline, such as turning off issuer, audience, signature,
  lifetime or algorithm validation ([SECURITY.md](SECURITY.md#scope)).
- A browser-side token library, or implicit, hybrid or password flows in the BFF
  ([docs/backend-for-frontend.md](docs/backend-for-frontend.md#32-non-goals)).
- The managed offering's proprietary parts and the platform's infrastructure, which NCodeGroup/identity-cloud and
  NCodeGroup/identity-platform hold
  ([ADR-1502](docs/decisions/1502-code-a-self-hoster-could-use-is-public-and-identity-holds-the-products-and-platforms-decisions.md)).
