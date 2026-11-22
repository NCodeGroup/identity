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
  and a request object is signed, short-lived and addressed to the issuer.
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
  [ADR-1173](decisions/1173-a-mutual-tls-client-is-authenticated-by-its-registered-certificate-chained-to-configured-trust-anchors.md),
  [ADR-1400](decisions/1400-client-certificate-revocation-is-checked-by-configured-sources-under-the-clients-revocation-mode.md)).
- **R4.** A single-use grant is spent once: a replayed authorization code ends its session, a refresh token rotates
  unless it is sender-constrained, a reused one revokes its family, and access tokens can be bound by DPoP or mutual TLS
  ([ADR-1216](decisions/1216-a-refresh-token-rotates-unless-it-is-bound-and-rotation-spends-it-with-its-successor-atomically.md),
  [ADR-1217](decisions/1217-a-reused-refresh-token-revokes-its-family-unless-it-is-a-retry-within-an-opt-in-interval.md),
  [ADR-1222](decisions/1222-a-grant-is-changed-by-a-read-modify-write-and-a-revocation-that-retry-a-lost-race.md),
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
- **R11.** Input from a remote party is parsed strictly and with bounded work: JSON into detached elements, base64url
  through one strict decoder, `NumericDate` values through strict readers
  ([ADR-1266](decisions/1266-binary-data-uses-bufferfactory-rentals-with-explicit-sensitivity-and-text-only-strict-encodings.md),
  [ADR-1123](decisions/1123-a-numericdate-and-a-whole-number-a-remote-party-sends-are-read-strictly-through-shared-readers.md)).
- **R12.** Credential guessing is bounded: failed password attempts are limited per account and failed client
  authentications per remote address, a wrong password takes as long whether or not the account exists, and a password
  is judged by one policy and hashed in its NFC form
  ([ADR-1237](decisions/1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md),
  [ADR-1246](decisions/1246-rate-limits-are-registered-descriptors-counted-in-a-shared-expiring-counter-set.md),
  [ADR-1239](decisions/1239-a-password-is-verified-in-the-same-time-whether-or-not-its-account-exists.md),
  [ADR-1236](decisions/1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md),
  [ADR-1238](decisions/1238-a-password-is-hashed-and-verified-in-its-nfc-form-on-every-path.md)).

Users must not rely on certification, which is in progress ([#124](https://github.com/NCodeGroup/identity/issues/124));
a setting an operator widens (an implicit or hybrid response type, the opt-in password grant, anonymous dynamic client
registration), which lowers this posture; the security of what the host runs on (database, KMS, reverse proxy), which is
the operator's; or a development-only convenience, inert unless `LocalDevelopment:Enabled` is set.

## Threat Model

[//]: # "Maintainers: add a table (Threat, In scope, Countered by) and state the trust boundary."

Not written yet.

## Secure Design Principles Applied

[//]: # "Maintainers: say how the design applies economy of mechanism, fail-safe defaults, complete mediation, least
privilege and open design."

Not written yet.

## Common Weaknesses Countered

[//]: # "Maintainers: list the CWE ids the design counters, each with the requirement that counters it."

Not written yet.

## Evidence

[//]: # "Maintainers: add the tests and the coverage the project keeps."

- **Gate:** `./build/dod.ps1` builds in Release with warnings as errors
  ([project-template ADR-0100](decisions/project-template/0100-warnings-are-errors-in-every-project.md)), runs the
  analyzers and the banned-API list in `build/BannedSymbols.txt`
  ([project-template ADR-0102](decisions/project-template/0102-disallowed-apis-are-banned-at-build-time.md)), and runs
  the tests; CI runs the commands on every pull request.
- **Static analysis:** with CodeQL's default setup configured and analyzing at least one language, the repository
  ruleset blocks a pull request that adds a CodeQL alert at warning level or above, or a security alert of any severity
  ([project-template ADR-0005](decisions/project-template/0005-the-default-branch-accepts-changes-only-through-pull-requests.md),
  [project-template ADR-0030](decisions/project-template/0030-the-code-scanning-rule-is-added-only-where-codeql-analyzes-a-language.md)).
- **Review:** every change reaches the default branch through a pull request with passing CI and every conversation
  resolved, and the ruleset refuses force pushes and deletion
  ([project-template ADR-0005](decisions/project-template/0005-the-default-branch-accepts-changes-only-through-pull-requests.md),
  [project-template ADR-0014](decisions/project-template/0014-every-pull-request-discussion-is-remediated-and-replied-to-before-it-merges.md)).
- **Dependencies:** pinned, and moved by reviewed updates
  ([project-template ADR-0008](decisions/project-template/0008-external-inputs-are-pinned-and-kept-current.md)) that
  wait three days for a new version
  ([project-template ADR-0009](decisions/project-template/0009-new-dependency-versions-wait-a-cooldown.md)). Dependabot
  proposes the updates. Central package management keeps one version of each package
  ([project-template ADR-0101](decisions/project-template/0101-central-package-management.md)).
- **Response:** [SECURITY.md](../SECURITY.md) defines private reporting, coordinated disclosure and credit.

## Security Reviews

[//]: # "Maintainers: record each security review, newest first: its date, who did it, what it covered (the security requirements and the trust boundary above) and what it found. The OpenSSF Best Practices gold level asks for one in the last five years."

None recorded yet.
