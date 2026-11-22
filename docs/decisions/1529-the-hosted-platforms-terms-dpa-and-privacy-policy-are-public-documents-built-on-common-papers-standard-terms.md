# 1529. The hosted platform's terms, DPA and privacy policy are public documents built on Common Paper's standard terms

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

Before it onboards customers, the hosted platform needs terms of service, a data processing agreement and a privacy
policy, beside the per-region subprocessor list ([#859](https://github.com/NCodeGroup/identity/issues/859)) and an SLA
no higher than a single-datacenter platform supports
([ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)).
Forces:

- **`NCodeGroup/identity-cloud` holds only the managed offering's proprietary parts**
  ([ADR-1502](1502-code-a-self-hoster-could-use-is-public-and-identity-holds-the-products-and-platforms-decisions.md)),
  and these documents are published to every visitor anyway.
- **Common Paper publishes standard agreements written by a committee of attorneys under CC BY 4.0.** The Cloud Service
  Agreement 3.0's Standard Terms take every variable from a cover page, and the DPA 1.1's Standard Terms carry the EU
  and UK transfer clauses.
- **The platform's facts that bind the documents are decided elsewhere:** backups
  ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)),
  encryption at rest
  ([ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md)),
  cells and residency
  ([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
  [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)),
  TLS ([ADR-1499](1499-the-host-accepts-only-tls-1-2-and-1-3-with-aead-cipher-suites.md)), one datacenter
  ([ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md))
  and audit retention
  ([ADR-1527](1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md)).

## Decision

**The terms of service are Common Paper's Cloud Service Agreement 3.0 Standard Terms and the DPA is Common Paper's DPA
1.1 Standard Terms, each incorporated by a cover page published in this repository under `docs/legal/`, beside a privacy
policy of the platform's own; counsel reviews all three before they take effect.**

1. **The documents.** `docs/legal/terms-of-service.md` holds the Key Terms and the self-serve Order Form,
   `docs/legal/data-processing-agreement.md` the DPA cover page and its annexes, and `docs/legal/privacy-policy.md` the
   privacy policy. Their Markdown here is the source, rendered at `https://ncodeid.com/legal/…`, and Git history is
   their public record of changes.
2. **Roles.** For a tenant's end-user data the customer is the controller and NCode Group its processor (the DPA). For
   account holders, the website and support, NCode Group is the controller (the privacy policy).
3. **Commitments the documents carry.**
   - Paid plans: 99.9% monthly availability, with service credits of 10% below 99.9% and 25% below 99.0%. The free
     developer plan carries none.
   - A personal data breach is notified to the customer without undue delay and within 72 hours of NCode Group becoming
     aware of it.
   - A subprocessor the platform adds is announced 30 days ahead, and the customer may object.
   - Tenant data is deleted within 30 days after the subscription ends, audit records expire 90 days after they are
     written, and encrypted backups age out within 13 months.
   - Processing stays in the cell's region.
4. **Variables chosen.** The provider is `NCode Group`; the law is Colorado's, with the state and federal courts in
   Denver, Colorado; the general cap is the fees of the 12 months before the claim, and $100 for the free plan; the
   DPA's governing member state for the EU clauses is Ireland. Notices go to `legal@ncodeid.com`, privacy requests to
   `privacy@ncodeid.com`, support requests to `support@ncodeid.com` and security reports to `security@ncodeid.com`.
5. **Counsel and publication.** Counsel confirms the variables, the commitments and the privacy policy, sets each
   document's effective date and publishes it before the first customer is onboarded. Until then each document's status
   line reads "Draft".

## Options considered

- **Documents of the platform's own drafting.** Rejected: an unreviewed contract carries risk that a standard drafted by
  attorneys has already settled.
- **A vendor's click-through generator.** Rejected: its text is closed and can't be versioned here.
- **The documents in `identity-cloud`.** Rejected: they are public by nature, and
  [ADR-1502](1502-code-a-self-hoster-could-use-is-public-and-identity-holds-the-products-and-platforms-decisions.md)
  keeps that repository to proprietary parts.
- **The Common Paper standards (chosen).** Attorney-drafted, CC BY 4.0, and parameterized by a cover page that Git can
  version.

## Consequences

- Every change to a legal document is a reviewed pull request whose history customers can read.
- A subprocessor or a retention change updates the DPA in the same change as its decision.
- `legal@`, `privacy@` and `support@` join the `ncodeid.com` Email Routing addresses
  ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).

## References

- [ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md),
  [ADR-1235](1235-stored-secrets-and-grant-payloads-are-protected-by-a-key-hierarchy-under-an-external-root-key.md),
  [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
  [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md),
  [ADR-1499](1499-the-host-accepts-only-tls-1-2-and-1-3-with-aead-cipher-suites.md),
  [ADR-1502](1502-code-a-self-hoster-could-use-is-public-and-identity-holds-the-products-and-platforms-decisions.md),
  [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md),
  [ADR-1527](1527-the-hosted-platform-keeps-audit-records-for-90-days-and-a-customer-exports-them-through-the-management-api.md)
- Common Paper's [Cloud Service Agreement 3.0](https://commonpaper.com/standards/cloud-service-agreement/3.0) and
  [DPA 1.1](https://commonpaper.com/standards/data-processing-agreement/1.1).
- Issues [#859](https://github.com/NCodeGroup/identity/issues/859) and
  [#912](https://github.com/NCodeGroup/identity/issues/912).
