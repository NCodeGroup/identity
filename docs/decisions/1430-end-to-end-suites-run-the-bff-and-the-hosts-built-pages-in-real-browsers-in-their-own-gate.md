# 1430. End-to-end suites run the BFF and the hosts' built pages in real browsers in their own gate

- **Status:** Accepted
- **Date:** 2026-11-05
- **Deciders:** NCode Group
- **Refines:** [project-template ADR-0008](project-template/0008-external-inputs-are-pinned-and-kept-current.md)

## Context

The backend-for-frontend (BFF) design (§10) asks for a browser suite and for an OpenID Provider (OP) that is not NCode
(goal G3). The browser suite checks what only a browser enforces: the session cookie's attributes, `SameSite` on
cross-site navigation and form posts, CORS preflight denial and Fetch Metadata rejection. The interop suite checks
login (PAR, PKCE `S256`, `private_key_jwt`, RFC 9207, PS256 ID tokens), DPoP, refresh-token rotation, RP-initiated
logout and back-channel logout against an OP built by someone else, using only its discovery metadata.

The OP host and the management console's host serve the built `apps/auth`, `apps/setup` and `apps/mgmt` bundles under a
strict Content-Security-Policy
([ADR-1256](1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md)),
and each bundle loads NCode Brand's typefaces from its own assets
([ADR-1516](1516-the-frontends-are-themed-with-ncode-brands-instrument-design-system-taken-from-a-vendored-release.md)).
Only a browser applies a page's policy to what its bundle loads: a unit test reads the policy and the built files, never
the two together.

The suites need real browsers, a browser download is roughly 500 MB, and the BFF's suite needs Docker. The Definition of
Done ([`build/dod.ps1`](../../build/dod.ps1)) is the one gate that developers, agents and CI run, and a container
runtime must not become a prerequisite of it, for the reason given in
[ADR-1071](1071-the-aspire-apphost-runs-the-local-stack-in-health-gated-order-after-checking-the-frontend-toolchain.md).
The suites therefore run in their own gate, which has its own required check
([ADR-1261](1261-the-main-branch-takes-only-reviewed-pull-requests-squash-merged-by-the-merge-queue-past-required-checks.md)).

## Decision

**End-to-end suites are test projects marked by name, run by one entrypoint in their own required CI check. They run the
BFF in Chromium, Firefox and WebKit against a containerized Keycloak, and the OP and management console hosts with their
built frontend bundles in the same browsers.**

1. **A tier, marked by name.** An end-to-end suite is a test project under `tests/` whose name ends in
   `.EndToEnd.Tests`. [`build/test-projects.ps1`](../../build/test-projects.ps1) is the one place that holds this rule.
   The solution compiles these projects. `./build/dod.ps1` never runs them: it tests only the other test projects,
   through `artifacts/tests.slnf`, in full and in `-Affected` mode alike. A suite under `tests/` keeps every test
   convention, the editorconfig `[tests/**/*.cs]` block and `tests/Directory.Build.props`.

2. **A support project is compiled and never tested.** A project under `tests/` whose name does not end in `.Tests`,
   such as `NCode.Identity.Bff.EndToEnd.BlazorApp`, is a support project that a suite references. The solution
   compiles it and no gate runs it.

3. **One entrypoint, `./build/e2e.ps1`, for CI and developers.** It builds the suites, installs the Playwright browsers
   and runs `dotnet test`. A suite whose project file sets `EndToEndContainers` to `true` starts containers: when the
   suites it runs include one, the entrypoint checks for a running Docker engine before it builds anything, and a run of
   suites that start none needs no Docker. With `-Affected` it runs only the suites in
   [`build/affected-tests.ps1`](../../build/affected-tests.ps1)'s `EndToEndProjects` output, and it passes without
   building when none is affected. It writes JUnit reports to `artifacts/e2e-results` and ends with
   `End-to-end: PASSED`. `-Suite <name>` runs only the named suites. A suite whose project file sets
   `EndToEndFrontendPackages` names the frontend packages whose built bundles it serves: the entrypoint installs the
   pnpm workspace with `--frozen-lockfile` and builds those packages with `turbo` before it builds the suite, so it
   needs pnpm on `PATH`. [`build/test-projects.ps1`](../../build/test-projects.ps1) reads both properties, and
   `affected-tests.ps1` selects such a suite for every change under `frontend/`.

4. **A CI job, `End-to-end`, is a required check.** [`.github/workflows/e2e.yml`](../../.github/workflows/e2e.yml) has
   the triggers and tiers of `ci.yml`
   ([ADR-1263](1263-branches-and-pull-requests-run-an-affected-gate-and-the-merge-queue-runs-the-full-gate.md)): the
   affected suites on a pull request into `main`, every suite in the merge queue, on `main` and on the release lines. It
   reports on every pull request, and it is a required check of the `main` and `release-lines` rulesets. The job sets up
   the Node major `frontend/.node-version` pins and the pnpm `frontend/package.json` pins with the actions and pins of
   Frontend CI, and caches the pnpm store.

5. **Playwright for .NET, driven by xUnit.** The BFF, the origin pages and the fixtures run in the test process, in
   one language and one runner, with the existing JUnit logger. The suites do not use Node Playwright in
   `frontend`, which is the console's workspace.

6. **Browsers.** Tests are theories over `BrowserNames.All`, in the support project
   `NCode.Identity.EndToEnd.TestSupport`: Chromium and Firefox on every operating system, and WebKit except on Windows.
   Playwright's Windows WebKit port does not use Safari's cookie stack and mishandles `SameSite` and `Secure`
   (microsoft/playwright#20370). CI runs on Linux, so it covers all three engines. The interop suite uses Chromium only,
   because what it tests is the OP, not the browser.

7. **The BFF's OP is Keycloak 26.** The image is pinned by digest in
   `tests/NCode.Identity.Bff.EndToEnd.Tests/Keycloak/Dockerfile`, beside the checked-in `bff-e2e-realm.json`, and
   Dependabot's own `docker` entry keeps it current, ignoring majors
   ([project-template ADR-0008](project-template/0008-external-inputs-are-pinned-and-kept-current.md)). Keycloak
   implements every standard the BFF uses and can require PAR, PKCE, DPoP and the ID-token algorithm per client, so a
   login that succeeds proves the BFF used them. The BFF's browser assertions are about the BFF, so they need no NCode
   OP, and the NCode OP stays the BFF's in-process oracle in `NCode.Identity.OpenId.Integration.Tests`.

8. **Testcontainers starts Keycloak.** The BFF suite's project file sets `EndToEndContainers`. The fixture owns the
   container's lifecycle and the port wiring. The realm uses Keycloak's `${ENV}` placeholders for ports and per-run
   credentials, so the repository holds no static secret. `TestcontainersSettings.ExposeHostPortsAsync` lets Keycloak
   reach the BFF at `host.testcontainers.internal` for `jwks_uri` and back-channel logout, without binding the BFF
   beyond loopback.

9. **Sites and TLS.** The BFFs listen on `https://localhost:<port>`. Keycloak and the cross-site pages are reached at
   `https://127.0.0.1:<port>`, and the same-site sibling pages at other `localhost` ports. One self-signed certificate
   per run covers `localhost`, `host.testcontainers.internal`, `127.0.0.1` and `::1`. Kestrel serves it, Keycloak
   serves it and trusts it, the BFF's provider `HttpClient` trusts exactly it, and browsers ignore certificate errors.
   Every host is HTTPS, because `__Host-` cookies need `Secure`.

10. **Two BFF instances share one realm.** `bff-strict` uses the default `SameSite=Strict` and `bff-lax` uses `Lax`.
    Each has the same deliberately broad CORS policy: it admits the two same-site siblings and one cross-site origin,
    with credentials and the `X-CSRF` header. Only the trusted sibling is in `Bff:Frontends:web:TrustedOrigins`, so the
    tests show that Fetch Metadata refuses what CORS admits.

11. **Every token request refreshes.** The realm's 60-second access-token lifespan and the default 60-second
    `Bff:Frontends:web:Tokens:RefreshBeforeExpiry` make every `/api/token` call refresh. The realm rotates refresh
    tokens and revokes on reuse, so two calls with different token fingerprints prove rotation.

12. **An imported user is granted its default roles.** Keycloak gives a user imported from a realm file no default
    roles, so the realm grants `alice` `default-roles-bff-e2e` explicitly. She then holds what a registered user
    holds.

13. **A form post waits for the response, not a navigation.** `BrowserFlows.SubmitFormAsync` waits for the response to
    its `POST`. Firefox turns an error response with an empty body into a failed navigation, so a navigation wait
    cannot observe a refusal in every browser.

14. **The login requests `offline_access` only when the deployment lists it.** The interop suite runs the BFF as
    [ADR-1291](1291-the-bff-refreshes-a-sessions-token-single-flight-under-a-lease-its-winner-releases-by-owner-token.md)
    describes, so Keycloak's logout at the OP reaches the BFF as a back-channel logout token.

15. **The pages suite runs the hosts' own composition.** `NCode.Identity.Frontend.EndToEnd.Tests` starts
    `NCode.Identity.OpenId.Host` and `NCode.Identity.ManagementConsole.Host` through
    `WebApplicationFactory<TEntryPoint>.UseKestrel()` on loopback over the run's certificate, in the local-development
    posture, with ephemeral Data Protection and no database. Without a connection the OP keeps its data in one in-memory
    database per process, so the suite starts one OP host per run. The console reaches the OP only when a user signs in,
    which the suite never does. The hosts serve the bundles at `frontend/apps/<app>/dist`, and the suite fails, never
    skips, when one is not built. It starts no container, so it sets no `EndToEndContainers` and runs without Docker.

16. **A page proves its policy in each browser.** The suite opens the login page, the setup page and the management
    console's page, waits for the bundle to render, and loads both typefaces through `document.fonts`. Each page loads
    one face of each family, answers every font request from its own assets with `200` and `font/woff2`, and reports no
    `securitypolicyviolation` event and no Content-Security-Policy console message. An init script records the events
    before the page's own scripts run.

17. **The suites share their plumbing.** `NCode.Identity.EndToEnd.TestSupport` holds `BrowserNames`, `BrowserPool` (one
    headless browser per engine per run, and a fresh context per test that ignores certificate errors), `LoopbackPort`,
    `TestCertificate` and `Teardown` (which stops what a suite started, every stop even when one throws).

## Options considered

- **A trait filter in the Definition of Done.** Rejected: the gate loads the end-to-end assembly even when a filter
  excludes its tests.
- **Running the suites in the Definition of Done.** Rejected: Docker and about 500 MB of browsers become prerequisites
  of the gate every developer, agent and CI run uses.
- **Node Playwright in `frontend`.** Rejected: it splits the suites across two languages and two runners, and the
  BFF host, the origin pages and the container fixtures would have to start from Node.
- **The NCode OP as the BFF's provider in the tier.** Rejected: an NCode OP proves the BFF only against its own
  implementation of the standards.
- **The pages checked in `NCode.Identity.OpenId.Integration.Tests`.** Rejected: its hosts run on `TestServer`, which no
  browser reaches, and the Definition of Done would need the browsers.
- **A by-hand check of the pages in a browser.** Rejected: nothing fails a change whose policy refuses what its bundle
  loads.
- **The pages suite against the host image in a container.** Rejected: the image checks build the image, and the suite
  needs only the hosts' composition with the built bundles.
- **The bundles built by the CI job instead of the entrypoint.** Rejected: a developer's `./build/e2e.ps1` would test
  whatever `dist` is on disk.
- **Docker checked before every run.** Rejected: a developer who runs only the pages suite would need a container engine
  that none of its tests uses.
- **Containers inferred from a suite's `Testcontainers` reference.** Rejected: a package reference says nothing certain
  about what a suite starts; the property states the need in the suite's own project file, as `EndToEndFrontendPackages`
  states the bundles it serves.
- **Docker Compose.** Rejected: the fixture owns the lifecycle and the port wiring, which Testcontainers expresses in
  code.
- **Other OPs.** Rejected:
  - node-oidc-provider is a library, with no official server image.
  - Ory Hydra needs a separate login app.
  - Dex has no PAR or DPoP.
  - ZITADEL needs a database.
- **Keycloak in a container (chosen).** It has an official image, needs no database in development mode, and implements
  PAR, PKCE, DPoP, `private_key_jwt`, RFC 9207, RP-initiated logout and back-channel logout.

## Consequences

- The `End-to-end` check gates merges, and a change to a suite, to the BFF's projects or to a global build input runs it
  in a pull request.
- A Keycloak bump arrives as a Dependabot pull request that the check validates.
- WebKit is proven only off Windows; CI covers it on Linux.
- A test that shows the BFF failing against Keycloak or a browser is a finding about the BFF.
- A change under `frontend/`, to the OP or console host or to a package either composes runs the pages suite in a pull
  request, and every run of it builds the three bundles first.
- The `End-to-end` job and a developer who runs the pages suite need Node and pnpm.
- A developer runs the pages suite without Docker; a run that includes the BFF suite needs it.

## References

- [ADR-1256](1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md),
  [ADR-1261](1261-the-main-branch-takes-only-reviewed-pull-requests-squash-merged-by-the-merge-queue-past-required-checks.md),
  [ADR-1263](1263-branches-and-pull-requests-run-an-affected-gate-and-the-merge-queue-runs-the-full-gate.md),
  [ADR-1291](1291-the-bff-refreshes-a-sessions-token-single-flight-under-a-lease-its-winner-releases-by-owner-token.md),
  [ADR-1516](1516-the-frontends-are-themed-with-ncode-brands-instrument-design-system-taken-from-a-vendored-release.md),
  [project-template ADR-0008](project-template/0008-external-inputs-are-pinned-and-kept-current.md)
- Issues [#418](https://github.com/NCodeGroup/identity/issues/418),
  [#419](https://github.com/NCodeGroup/identity/issues/419) and
  [#862](https://github.com/NCodeGroup/identity/issues/862)
- `tests/NCode.Identity.Bff.EndToEnd.Tests/`, `tests/NCode.Identity.Frontend.EndToEnd.Tests/`,
  `tests/NCode.Identity.EndToEnd.TestSupport/`, `build/e2e.ps1`, `build/test-projects.ps1`, `.github/workflows/e2e.yml`
