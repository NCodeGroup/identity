# Architecture

[//]: # "Maintainers: describe the components, as a diagram and a table (Component, Role), and the rules the design
keeps."

How NCode.Identity is built. [assurance-case.md](assurance-case.md) explains why the design meets its security
requirements.

## Components

The packages, hosts, analyzers and web apps, and the folder each lives in, are listed in
[AGENTS.md](../AGENTS.md#layout). [capabilities/](capabilities/README.md) describes each capability's architecture, with
the decisions and code behind it.

## Design Rules

- Decisions the design rests on are recorded in [decisions/](decisions/README.md).
- Warnings are errors ([ADR-0100](decisions/project-template/0100-warnings-are-errors-in-every-project.md)), and
  disallowed APIs are banned at build time
  ([ADR-0102](decisions/project-template/0102-disallowed-apis-are-banned-at-build-time.md)).

## Build and Release

CI runs the Definition of Done, `./build/dod.ps1`, on every pull request, in the merge queue and on each push to `main`
or a release line (`.github/workflows/ci.yml`), and `.github/workflows/frontend.yml` gates the web apps' workspace.
Releases are cut from `release/vX.Y` lines and published as [GOVERNANCE.md](../GOVERNANCE.md#releases-branches--tags)
describes; `AGENTS.md`'s Landing section says `release: none`, so no plan tags a release.
