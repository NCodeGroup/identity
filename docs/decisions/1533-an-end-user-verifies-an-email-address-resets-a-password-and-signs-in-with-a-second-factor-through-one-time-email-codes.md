# 1533. An end user verifies an email address, resets a password and signs in with a second factor through one-time email codes

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

A local account carries `Email` and `EmailVerified`
([ADR-1051](1051-a-local-account-is-a-self-issued-connection-behind-one-pluggable-store-seam.md)), and the interactive
surface defers every credential operation to a seam that a backend implements
([ADR-1062](1062-the-op-serves-an-opt-in-interactive-sign-in-and-consent-surface-with-a-pluggable-credential-verifier.md),
[ADR-1230](1230-an-end-user-changes-their-password-through-an-optional-seam-and-keeps-only-the-current-session.md)).
Forces:

- **An end user who forgets a password needs an administrator's reset.** A tenant also needs a second factor beyond a
  password, and an address is verified by nothing the end user can do.
- **Mail goes through the port of**
  [ADR-1532](1532-outbound-mail-is-a-port-that-composes-each-queued-message-at-delivery-for-a-replaceable-transport-with-smtp-and-amazon-ses-adapters.md).
  A message is queued, so a send says nothing about whether an account exists, and a code that arrives after it expires
  is useless.
- **Shared state that every replica sees holds short-lived values and counters**
  ([ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md)).
- **Email is not an authenticator at AAL2.** NIST SP 800-63B §5.1.3.1 does not accept email as an out-of-band
  authenticator.

## Decision

**A local account's end user verifies the account's email address, resets a forgotten password, and completes a second
factor at sign-in, each with a six-digit code mailed to the account's address, bound to the flow that asked for it, and
off until the tenant turns it on.**

1. **The code is six digits, hashed, short-lived and used once.** It is six decimal digits from a cryptographic random
   generator, kept in shared state only as a SHA-256 hash bound to the flow's handle, for `email_code_lifetime` (10
   minutes by default, 1 minute to 1 hour, a narrower scope may only shorten it). Five guesses end the flow, each
   counted before it is judged, and a code is used once: a flow is spent by one atomic take after every check has passed
   and before its effect, so two requests that present the right code at once never both run the effect. An account
   receives at most `email_code_sends_per_hour` codes for one purpose (5 by default, 1 to 20, a narrower scope may only
   lower it), so an attacker gets at most 25 guesses an hour at a space of a million.
2. **A 256-bit flow handle names the flow.** The handle is random and rides on the page that started the flow, so the
   code alone opens nothing. A code is also bound to the address it was mailed to and authorizes nothing once the
   account's address differs, so an administrator who changes the address while a code is outstanding leaves the old
   mailbox unable to verify the new address, reset the password or pass the second factor.
3. **A reset is `password_reset_enabled`, off by default.** The login page links "Forgot your password?", and the form
   asks for the username. Whatever the username names, the next page is the same, and a code goes only to an enabled
   account's verified address. The password policy judges the new password
   ([ADR-1236](1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md)) before the code is spent. A reset sets
   the password, ends every session of the account's principal and revokes its grants
   ([ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md),
   [ADR-1351](1351-work-that-follows-a-committed-change-runs-on-its-own-bounded-token.md)), clears its failed attempts
   ([ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md)),
   and sends the end user to the login page.
4. **A verification is `email_verification_enabled`, off by default.** The account page links the email page, which
   sends a code to the account's address and marks the address verified when the code comes back.
5. **A second factor is `email_mfa_required`, off by default and merged with `Or`.** A team can require it, and a tenant
   can't lift it. After the password and the end user's standing pass, the surface holds the verified principal for 15
   minutes from the password check, however many codes are resent, in a data-protected pending cookie,
   `__Host-NCode.Identity.Interactive.PendingSignIn` (`SameSite=Strict`), and mails a code to the account's address,
   verified or not, because the code proves the mailbox, which a right code then marks verified. An account without an
   address is refused with `access_denied`. A right code adds `otp` and `mfa` to `amr`
   ([RFC 8176](https://www.rfc-editor.org/rfc/rfc8176)) and creates the session. The resource-owner password grant is
   refused for a tenant that requires the second factor, because it cannot carry one.
6. **Seams carry the three flows.** The surface's `IInteractivePasswordResetter`, `IInteractiveEmailVerifier` and
   `IInteractiveSignInCodeChallenge` default to supporting nothing. The local-account capability implements them over
   the account store, the shared state and `IMailSender`, and mails in the locale the page renders in, under the
   tenant's branding display name.
7. **The pages have a baseline and the mail is localized.** The forgot-password, reset-password and sign-in-code pages
   have the no-JavaScript baseline the login page has
   ([ADR-1253](1253-a-tenant-brands-its-sign-in-pages-through-settings-and-the-bundle-mounts-over-the-hosts-page.md)),
   and the email page is a bundle page as the change-password page is. The pages and the mail are worded in every
   shipped locale
   ([ADR-1402](1402-the-interactive-surface-renders-in-the-locale-the-request-prefers-among-the-registered-ui-locales.md)).
8. **The flows are audited.** The change, the reset request, the reset, the verification and the second factor are
   audited as `password.change`, `password.reset_request`, `password.reset`, `email.verification` and
   `subject.second_factor` in the `authentication` category, with the account as the resource and no code, password or
   address
   ([ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md),
   [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md)).

## Options considered

- **A link instead of a code.** Rejected: mail scanners follow links and spend them, and a link opens in any browser,
  while a code is entered on the page that started its flow.
- **Eight digits.** Rejected: the attempt limits already bound guessing, and six is what end users type.
- **A reset that looks the account up by email address.** Rejected: addresses are not unique among local accounts, and
  the username is what the account signs in with.
- **A second factor sent only to a verified address.** Rejected: an account whose address was never verified could not
  finish its first sign-in, while the code itself proves the mailbox.
- **TOTP or passkeys.** Not decided here: they are stronger factors that need enrollment, and the email code is the
  factor an account already has.
- **One six-digit email code per flow.** Chosen.

## Consequences

- A tenant on a host without a mail transport keeps the three settings off.
- Because NIST SP 800-63B does not accept email as an authenticator at AAL2, the session's `acr` is not raised.
- The hosted platform writes the settings on a tier's team document.
- Every replica sees a code through shared state.

## References

- [ADR-1047](1047-observation-is-a-fault-isolated-event-seam-that-audit-specializes-and-protocol-errors-reach-once.md) —
  the event seam that audit specializes.
- [ADR-1051](1051-a-local-account-is-a-self-issued-connection-behind-one-pluggable-store-seam.md) — the local account
  and its email address.
- [ADR-1062](1062-the-op-serves-an-opt-in-interactive-sign-in-and-consent-surface-with-a-pluggable-credential-verifier.md)
  — the interactive surface and its credential seams.
- [ADR-1164](1164-replicas-share-expiring-keys-values-and-indexes-through-ports-with-in-process-and-valkey-adapters.md)
  — the shared state that holds a code.
- [ADR-1229](1229-a-blocked-disabled-or-deleted-end-user-loses-its-sessions-and-is-refused-with-access-denied.md) — the
  sessions that end with the account's standing.
- [ADR-1230](1230-an-end-user-changes-their-password-through-an-optional-seam-and-keeps-only-the-current-session.md) —
  the password change whose seam the three flows follow.
- [ADR-1236](1236-a-password-is-judged-by-one-policy-wherever-it-is-set.md) — the password policy that judges a new
  password.
- [ADR-1237](1237-failed-password-attempts-are-limited-per-account-and-a-refusal-costs-what-a-wrong-password-costs.md) —
  the limit on failed attempts that a reset clears.
- [ADR-1253](1253-a-tenant-brands-its-sign-in-pages-through-settings-and-the-bundle-mounts-over-the-hosts-page.md) — the
  pages' branding and no-JavaScript baseline.
- [ADR-1283](1283-audit-records-are-cloudtrail-shaped-json-with-an-indexable-envelope.md) — the shape of an audit
  record.
- [ADR-1351](1351-work-that-follows-a-committed-change-runs-on-its-own-bounded-token.md) — the bounded token that the
  post-commit effects run on.
- [ADR-1402](1402-the-interactive-surface-renders-in-the-locale-the-request-prefers-among-the-registered-ui-locales.md)
  — the locale the pages render in.
- [ADR-1532](1532-outbound-mail-is-a-port-that-composes-each-queued-message-at-delivery-for-a-replaceable-transport-with-smtp-and-amazon-ses-adapters.md)
  — the mail port that sends a code.
- [RFC 8176](https://www.rfc-editor.org/rfc/rfc8176) — the `otp` and `mfa` authentication method references.
- [NIST SP 800-63B](https://pages.nist.gov/800-63-4/sp800-63b.html) — email is not an out-of-band authenticator.
- Issue [#911](https://github.com/NCodeGroup/identity/issues/911).
