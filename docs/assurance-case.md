# Assurance Case

This page states NCode.Identity's security requirements, the threats it considers, and why the design and the way it is
built meet them. [architecture.md](architecture.md) describes the components; [README.md](../README.md) is the
user-facing summary.

## Security Requirements

- **R1.** Every authorization code is bound to an S256 PKCE challenge
  ([ADR-1144](decisions/1144-authorization-codes-are-bound-to-an-s256-pkce-challenge-unless-an-attested-client-uses-the-nonce.md)),
  `response_types_supported` is `code` alone unless an operator widens it
  ([ADR-1141](decisions/1141-a-response-type-is-a-whole-value-and-the-listed-values-alone-control-front-channel-tokens.md),
  [ADR-1010](decisions/1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md)),
  redirect URIs match exactly
  ([ADR-1203](decisions/1203-redirect-uris-are-matched-exactly-as-written-and-their-kinds-are-narrowed-by-settings.md)),
  and a request object is signed, short-lived and addressed to the issuer
  ([ADR-1187](decisions/1187-a-client-signed-jwt-is-short-lived-its-lifetime-capped-from-its-earliest-asserted-start.md)).
- **R2.** A token is accepted only as a signed token under an explicit algorithm, key, type and lifetime policy, and the
  server validates the tokens it issued under one policy
  ([ADR-1154](decisions/1154-jwts-are-validated-only-as-signed-tokens-by-requirements-and-handlers-under-the-callers-stated-policy.md),
  [ADR-1167](decisions/1167-the-server-validates-the-tokens-it-issued-under-one-policy.md),
  [ADR-1392](decisions/1392-ecdsa-and-eddsa-algorithms-are-fully-specified-and-bound-to-their-curve-and-es256k-is-opt-in.md)).
- **R3.** A client authenticates only with the method it is registered for, a compared client secret is stored as a
  one-way verifier, a client assertion or DPoP proof is accepted once across every replica, and a mutual-TLS client's
  certificate chain and revocation are checked, failing closed
  ([ADR-1391](decisions/1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md),
  [ADR-1102](decisions/1102-a-client-authenticates-with-a-single-use-jwt-assertion-signed-by-its-key-or-its-secret.md),
  [ADR-1097](decisions/1097-access-tokens-are-sender-constrained-by-dpop-or-mutual-tls-through-one-proof-of-possession-seam.md),
  [ADR-1173](decisions/1173-a-mutual-tls-client-is-authenticated-by-its-registered-certificate-chained-to-configured-trust-anchors.md),
  [ADR-1400](decisions/1400-client-certificate-revocation-is-checked-by-configured-sources-under-the-clients-revocation-mode.md)).
- **R4.** A single-use grant is spent once: a replayed authorization code ends its session, a refresh token rotates
  unless it is sender-constrained, a reused one revokes its family, and access tokens can be bound by DPoP or mutual TLS
  ([ADR-1216](decisions/1216-a-refresh-token-rotates-unless-it-is-bound-and-rotation-spends-it-with-its-successor-atomically.md),
  [ADR-1217](decisions/1217-a-reused-refresh-token-revokes-its-family-unless-it-is-a-retry-within-an-opt-in-interval.md),
  [ADR-1112](decisions/1112-every-end-of-a-session-runs-one-termination-seam-once-revoking-the-grants-issued-under-it.md),
  [ADR-1097](decisions/1097-access-tokens-are-sender-constrained-by-dpop-or-mutual-tls-through-one-proof-of-possession-seam.md)).
- **R5.** One tenant's data never reaches another: tenant-bound rows are scoped at the persistence layer, the scope
  fails closed, PostgreSQL row-level security enforces it under a role that can't bypass it, and an issuer comes from
  the tenant's stored record, never the request's `Host` header
  ([ADR-1018](decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md),
  [ADR-1165](decisions/1165-a-tenants-stored-issuer-is-its-single-origin-and-its-mtls-alias-a-routed-second-host.md)).
- **R6.** Stored secrets, private keys, grant payloads and the Data Protection key ring are encrypted under a key
  hierarchy rooted in an external KMS root key, each tenant under its own master key, and secret plaintext is held in
  buffers zeroed after use
  ([ADR-1235](decisions/1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md),
  [ADR-1247](decisions/1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md),
  [ADR-1266](decisions/1266-binary-data-uses-bufferfactory-rentals-with-explicit-sensitivity-and-text-only-strict-encodings.md)).
- **R7.** Management authority is a role assignment at a node of the platform, team and tenant hierarchy, checked before
  the route reads anything, and every management change is audited
  ([ADR-1417](decisions/1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md),
  [ADR-1424](decisions/1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md),
  [ADR-1283](decisions/1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md)).
- **R8.** Every server-initiated call to a caller-supplied URL goes through one SSRF-guarded client with bounded size
  and time
  ([ADR-1315](decisions/1315-calls-to-caller-supplied-urls-share-one-guarded-bounded-client-and-one-single-flight-helper.md)).
- **R9.** A host runs the deployment posture unless configured for local development: HTTPS only, TLS 1.2 and 1.3 with
  AEAD suites, configuration bound strictly and validated at startup, a strict Content-Security-Policy on every page and
  no committed development credential
  ([ADR-1370](decisions/1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md),
  [ADR-1504](decisions/1504-the-hosts-serve-https-only-and-verify-their-certificate-at-startup.md),
  [ADR-1499](decisions/1499-the-host-accepts-only-tls-1-2-and-1-3-with-aead-cipher-suites.md),
  [ADR-1137](decisions/1137-configuration-binds-strictly-and-every-option-timer-period-and-retention-is-validated-at-startup.md),
  [ADR-1256](decisions/1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md)).
- **R10.** A browser holds no token: the BFF and the management console keep tokens in an encrypted server-side session
  under a host-only cookie that only guarded endpoints accept, refusing cross-site requests
  ([ADR-1290](decisions/1290-the-bff-keeps-each-session-as-an-encrypted-server-side-record-under-a-cookie-handle.md),
  [ADR-1292](decisions/1292-the-bffs-cookie-authenticates-only-guarded-endpoints-which-refuse-cross-site-requests-and-reload-cookieless-navigations.md),
  [ADR-1438](decisions/1438-the-management-console-holds-no-token-and-reads-its-session-from-its-bff.md)).
- **R11.** Input from a remote party is parsed strictly: base64url through one strict decoder and `NumericDate` values
  through strict readers
  ([ADR-1266](decisions/1266-binary-data-uses-bufferfactory-rentals-with-explicit-sensitivity-and-text-only-strict-encodings.md),
  [ADR-1123](decisions/1123-a-numericdate-and-a-whole-number-a-remote-party-sends-are-read-strictly-through-shared-readers.md)),
  and an opt-in SD-JWT presentation or COSE message under bounded size, nesting and work
  ([ADR-1405](decisions/1405-selective-disclosure-jwts-are-an-opt-in-package-that-issues-presents-and-verifies-over-the-jwt-validator.md),
  [ADR-1409](decisions/1409-cose-messages-are-an-opt-in-package-family-over-system-formats-cbor-and-the-familys-keys-and-algorithms.md)).
- **R12.** Credential guessing is bounded: failed password attempts are limited per account and failed client
  authentications per remote address, a wrong password takes as long whether or not the account exists, and a password
  is judged by one policy and hashed in its NFC form
  ([ADR-1237](decisions/1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md),
  [ADR-1246](decisions/1246-rate-limits-are-registered-descriptors-counted-in-a-shared-expiring-counter-set.md),
  [ADR-1399](decisions/1399-failed-client-authentications-are-limited-per-remote-address-by-default.md),
  [ADR-1239](decisions/1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md),
  [ADR-1236](decisions/1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md),
  [ADR-1238](decisions/1238-a-password-is-hashed-and-verified-in-its-nfc-form-on-every-path.md)).

Users must not rely on certification, which is in progress ([#124](https://github.com/NCodeGroup/identity/issues/124));
a setting an operator widens (an implicit or hybrid response type, the opt-in password grant, anonymous dynamic client
registration), which lowers this posture; the security of what the host runs on (database, KMS, reverse proxy), which is
the operator's; or a development-only convenience, inert unless `LocalDevelopment:Enabled` is set.

## Threat Model

The trust boundary is the host's HTTP surface. Clients, end users' browsers, resource servers and every document fetched
from a caller-supplied URL are untrusted, and so is each tenant toward every other tenant; the host process, its
configuration, its database role and the KMS root key it names are trusted. Out of scope are a compromised cloud or KMS
account, an operator acting against the tenants, an attacker running code in the host process, a vulnerability in a
dependency itself ([SECURITY.md](../SECURITY.md)), and the platform's infrastructure, which NCodeGroup/identity-platform
holds.

| Threat | In scope | Countered by |
| --- | --- | --- |
| An authorization code intercepted or injected | Yes | R1: PKCE ([ADR-1144](decisions/1144-authorization-codes-are-bound-to-an-s256-pkce-challenge-unless-an-attested-client-uses-the-nonce.md)), exact redirect URIs ([ADR-1203](decisions/1203-redirect-uris-are-matched-exactly-as-written-and-their-kinds-are-narrowed-by-settings.md)) |
| A forged token, `alg: none`, or an algorithm or curve its key isn't for | Yes | R2 ([ADR-1154](decisions/1154-jwts-are-validated-only-as-signed-tokens-by-requirements-and-handlers-under-the-callers-stated-policy.md), [ADR-1392](decisions/1392-ecdsa-and-eddsa-algorithms-are-fully-specified-and-bound-to-their-curve-and-es256k-is-opt-in.md)) |
| A stolen access or refresh token replayed | Yes | R4: DPoP or mutual-TLS binding ([ADR-1097](decisions/1097-access-tokens-are-sender-constrained-by-dpop-or-mutual-tls-through-one-proof-of-possession-seam.md)), rotation and family revocation ([ADR-1216](decisions/1216-a-refresh-token-rotates-unless-it-is-bound-and-rotation-spends-it-with-its-successor-atomically.md), [ADR-1217](decisions/1217-a-reused-refresh-token-revokes-its-family-unless-it-is-a-retry-within-an-opt-in-interval.md)) |
| A client impersonated, or its assertion or DPoP proof replayed | Yes | R3 ([ADR-1102](decisions/1102-a-client-authenticates-with-a-single-use-jwt-assertion-signed-by-its-key-or-its-secret.md), [ADR-1391](decisions/1391-a-client-secret-is-a-one-way-verifier-unless-its-method-keys-from-its-utf-8-octets.md)) |
| One tenant reads or changes another's data | Yes | R5: persistence-layer scoping and row-level security ([ADR-1018](decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md)), and the `NCID0004` analyzer, which reports an unexplained `IgnoreQueryFilters()` |
| A stolen database or backup exposes keys and secrets | Yes | R6 ([ADR-1235](decisions/1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)) |
| A request to an internal address through a client's `jwks_uri`, `request_uri` or a revocation source | Yes | R8 ([ADR-1315](decisions/1315-calls-to-caller-supplied-urls-share-one-guarded-bounded-client-and-one-single-flight-helper.md)) |
| An administrator acts outside their scope | Yes | R7 ([ADR-1417](decisions/1417-management-authority-is-a-role-assignment-at-a-node-of-the-platform-team-and-tenant-hierarchy.md), [ADR-1424](decisions/1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)) |
| Guessing passwords or client secrets | Yes | R12 ([ADR-1237](decisions/1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md), [ADR-1246](decisions/1246-rate-limits-are-registered-descriptors-counted-in-a-shared-expiring-counter-set.md), [ADR-1399](decisions/1399-failed-client-authentications-are-limited-per-remote-address-by-default.md), [ADR-1239](decisions/1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md)) |
| Script injection into, or framing of, the sign-in pages | Yes | R9: a strict CSP and framing only by design ([ADR-1256](decisions/1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md)) |
| A cross-site request against a BFF or its logout | Yes | R10 ([ADR-1292](decisions/1292-the-bffs-cookie-authenticates-only-guarded-endpoints-which-refuse-cross-site-requests-and-reload-cookieless-navigations.md), [ADR-1293](decisions/1293-the-bff-ends-sessions-by-a-guarded-logout-form-post-and-by-back-channel-logout-through-indexes.md)) |
| A forged `Host` header changes a published URL | Yes | R5: the issuer comes from the tenant's stored record ([ADR-1165](decisions/1165-a-tenants-stored-issuer-is-its-single-origin-and-its-mtls-alias-a-routed-second-host.md)) |
| A deployment runs with development settings or an unprotected secret | Yes | R9: the host refuses to start ([ADR-1370](decisions/1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)) |
| Malformed input that a parser reads loosely, or an oversized SD-JWT presentation or COSE message, exhausts the server | Yes | R11 ([ADR-1266](decisions/1266-binary-data-uses-bufferfactory-rentals-with-explicit-sensitivity-and-text-only-strict-encodings.md), [ADR-1123](decisions/1123-a-numericdate-and-a-whole-number-a-remote-party-sends-are-read-strictly-through-shared-readers.md), [ADR-1405](decisions/1405-selective-disclosure-jwts-are-an-opt-in-package-that-issues-presents-and-verifies-over-the-jwt-validator.md), [ADR-1409](decisions/1409-cose-messages-are-an-opt-in-package-family-over-system-formats-cbor-and-the-familys-keys-and-algorithms.md)) |
| A compromised KMS root key or cloud account | No | Outside the trust boundary above |

## Secure Design Principles Applied

- **Economy of mechanism**: one seam per concern
  ([ADR-1001](decisions/1001-behavior-is-a-service-a-strategy-collection-or-a-mediator-seam-chosen-by-one-ladder.md)):
  one validator for the tokens the server issued
  ([ADR-1167](decisions/1167-the-server-validates-the-tokens-it-issued-under-one-policy.md)), one outbound client
  ([ADR-1315](decisions/1315-calls-to-caller-supplied-urls-share-one-guarded-bounded-client-and-one-single-flight-helper.md)),
  one session-termination seam
  ([ADR-1112](decisions/1112-every-end-of-a-session-runs-one-termination-seam-once-revoking-the-grants-issued-under-it.md)),
  one key policy
  ([ADR-1161](decisions/1161-every-owners-public-keys-have-one-source-and-are-imported-under-one-key-policy.md)).
- **Fail-safe defaults**: a secure settings baseline that only an operator widens
  ([ADR-1010](decisions/1010-settings-merge-as-ceilings-and-floors-unset-lists-restrict-nothing-and-a-secure-baseline-supplies-defaults.md)),
  tenant scoping and revocation checks that fail closed, a host that stops on a key it can't decrypt
  ([ADR-1247](decisions/1247-the-platform-master-key-protects-the-data-protection-ring-and-an-unreadable-key-stops-the-host.md))
  or on development settings
  ([ADR-1370](decisions/1370-a-host-runs-the-deployment-posture-unless-configured-for-local-development-and-always-names-its-root-key-provider.md)).
- **Complete mediation**: every management route authorizes at its node before it reads
  ([ADR-1424](decisions/1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)),
  and every grant that issues tokens validates its subject
  ([ADR-1225](decisions/1225-every-grant-that-issues-tokens-for-a-subject-validates-it.md)).
- **Least privilege**: the runtime database role can't bypass row-level security
  ([ADR-1018](decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md)),
  resource servers own their scopes
  ([ADR-1026](decisions/1026-resource-servers-own-scopes-settings-and-keys-and-decide-a-clients-scopes-and-token-audiences.md)),
  and the images run as non-root
  ([ADR-1248](decisions/1248-the-host-console-and-directory-images-are-chiseled-non-root-multi-arch-digest-pinned-and-log-one-line-json.md)).
- **Open design**: the code and the decision records are public
  ([ADR-1502](decisions/1502-code-a-self-hoster-could-use-is-public-and-identity-holds-the-products-and-platforms-decisions.md)),
  and the security rests on keys, not on secrecy.

## Common Weaknesses Countered

- CWE-287 and CWE-294, improper authentication and capture-replay: R3, R4.
- CWE-347 and CWE-757, unverified signature and algorithm downgrade: R2.
- CWE-601, open redirect: R1.
- CWE-639 and CWE-863, authorization bypass through a user-controlled key and incorrect authorization: R5, R7.
- CWE-918, server-side request forgery: R8.
- CWE-311 and CWE-226, missing encryption and sensitive data left in reused memory: R6.
- CWE-307 and CWE-208, excessive authentication attempts and timing discrepancy: R12.
- CWE-319, cleartext transmission: R9.
- CWE-79 and CWE-1021, cross-site scripting and UI framing: R9.
- CWE-352, cross-site request forgery: R10.
- CWE-20 and CWE-400, improper input validation and uncontrolled resource consumption: R11.
- CWE-532, sensitive information in logs: HTTP logging writes a body only for an endpoint that opts in, never a
  credential
  ([ADR-1503](decisions/1503-http-logging-writes-a-body-only-for-an-endpoint-that-opts-in-and-never-for-one-that-carries-credentials.md)).
- CWE-798, hard-coded credentials: R9.

## Evidence

- **Gate:** `./build/dod.ps1` builds in Release with warnings as errors
  ([project-template ADR-0100](decisions/project-template/0100-warnings-are-errors-in-every-project.md)), runs the
  analyzers and the banned-API list in `build/BannedSymbols.txt`
  ([project-template ADR-0102](decisions/project-template/0102-disallowed-apis-are-banned-at-build-time.md)), and runs
  the tests; CI runs the commands on every pull request.
- **Tests:** each project's `.Tests` project of unit and integration tests runs in `./build/dod.ps1`, a full run
  collects coverage of hand-written code
  ([ADR-1143](decisions/1143-ci-presents-test-results-and-hand-written-code-coverage-through-job-summaries-annotations-and-codecov.md)),
  and the `End-to-end` check runs the BFF and the built pages in real browsers
  ([ADR-1430](decisions/1430-end-to-end-suites-run-the-bff-and-the-hosts-built-pages-in-real-browsers-in-their-own-gate.md)).
- **Analyzers:** `NCID0004` reports an unexplained `IgnoreQueryFilters()`
  ([ADR-1018](decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md))
  and `NCID0005` a `Guid` on a public contract
  ([ADR-1517](decisions/1517-internal-surrogate-keys-are-uuidv7-minted-in-process-behind-one-seam-and-never-reach-a-caller.md)).
- **Static analysis:** with CodeQL's default setup configured and analyzing at least one language, the repository
  ruleset blocks a pull request that adds a CodeQL alert at warning level or above, or a security alert of any severity
  ([project-template ADR-0005](decisions/project-template/0005-the-default-branch-accepts-changes-only-through-pull-requests.md),
  [project-template ADR-0030](decisions/project-template/0030-the-code-scanning-rule-is-added-only-where-codeql-analyzes-a-language.md)).
- **Review:** every change reaches the default branch through a pull request with passing CI and every conversation
  resolved, and the ruleset refuses force pushes and deletion
  ([project-template ADR-0005](decisions/project-template/0005-the-default-branch-accepts-changes-only-through-pull-requests.md),
  [project-template ADR-0014](decisions/project-template/0014-every-pull-request-discussion-is-remediated-and-replied-to-before-it-merges.md)).
  NCode Review reviews each pull request, posts its findings as review threads resolved before the merge, and approves a
  review that leaves no blocker or should-fix finding open.
- **Dependencies:** pinned, and moved by reviewed updates
  ([project-template ADR-0008](decisions/project-template/0008-external-inputs-are-pinned-and-kept-current.md)) that
  wait three days for a new version
  ([project-template ADR-0009](decisions/project-template/0009-new-dependency-versions-wait-a-cooldown.md)). Dependabot
  proposes the updates. Central package management keeps one version of each package
  ([project-template ADR-0101](decisions/project-template/0101-central-package-management.md)).
- **Response:** [SECURITY.md](../SECURITY.md) defines private reporting, coordinated disclosure and credit.
- **Conformance:** the OpenID Foundation conformance suite runs through the spec-coverage harness, and certification is
  in progress ([#124](https://github.com/NCodeGroup/identity/issues/124)).

## Security Reviews

[//]: # "Maintainers: record each security review, newest first: its date, who did it, what it covered (the security requirements and the trust boundary above) and what it found. The OpenSSF Best Practices gold level asks for one in the last five years."

None recorded yet.
