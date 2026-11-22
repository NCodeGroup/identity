# 1528. The hosted platform sends its mail through Amazon SES in each cell's region from mail.ncodeid.net

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

The hosted platform
([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md))
sends mail to the end users of its tenants: verification, password reset and email multi-factor authentication each need
a sending provider. Forces:

- **Mail is a cell's personal data.** A message carries an end user's address and a code or link, so it is data of the
  cell that sends it. ADR-1495 runs every subprocessor of a cell's data in the cell's region.
- **System mail has one sender domain.** It sends only from `mail.ncodeid.net`, and every domain of the platform
  publishes DMARC `p=reject` (ADR-1495 decision 9).
- **The subprocessors** are Akamai with Aiven behind its managed PostgreSQL, Cloudflare, Grafana Labs and AWS
  ([ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md)).
- **The candidates compared** are OCI Email Delivery (3,100 emails a month free, then $0.085 per 1,000, regional),
  Cloudflare Email Sending (Workers Paid at $5 a month with 3,000 included, then $0.35 per 1,000, on Cloudflare's global
  network) and Amazon SES (regional, $0.10 per 1,000).

## Decision

**The hosted platform sends its transactional mail through Amazon SES in the AWS region of each cell, over SMTP, from
`mail.ncodeid.net`, with Easy DKIM and a custom MAIL FROM domain under DMARC `p=reject`.**

1. **Region.** Production cells send through `us-east-1`, the AWS region in the metro of `us-iad`; a cell in another
   region sends through an SES region in it. The lab sends through `us-west-2` in the SES sandbox, which delivers only
   to verified addresses.
2. **Identity.** The domain identity is `mail.ncodeid.net` with Easy DKIM's three CNAME records, the MAIL FROM domain is
   `bounce.mail.ncodeid.net` with its MX record and SPF `v=spf1 include:amazonses.com -all` so that SPF aligns as well
   as DKIM, and the DMARC policy stays `p=reject`. The platform's OpenTofu writes the records in the `ncodeid.net` zone
   ([#750](https://github.com/NCodeGroup/identity/issues/750)).
3. **Sending.** SMTP with STARTTLS on port 587 to `email-smtp.<region>.amazonaws.com`, with the SMTP credentials of an
   IAM user allowed only `ses:SendRawEmail` from addresses at `mail.ncodeid.net`. The credentials are a cell secret in
   Parameter Store
   ([ADR-1526](1526-the-platforms-cluster-secrets-reach-each-cluster-from-aws-parameter-store-through-external-secrets-operator.md))
   and are rotated yearly. SMTP keeps the host's mail sending neutral, so a self-hoster uses any provider.
4. **Reputation.** The account-level suppression list is on, and a configuration set publishes bounces and complaints to
   the platform's monitoring.
5. **Subprocessors.** AWS already processes the cells' data, so Amazon SES adds no vendor to the subprocessors.

## Options considered

- **OCI Email Delivery.** It is regional and free at this volume, but Oracle becomes a production subprocessor, and OCI
  has fewer regions than AWS, the platform's growth path. Rejected.
- **Cloudflare Email Sending.** It runs on Cloudflare's global network rather than in the cell's region, against
  ADR-1495, and costs more. Rejected.
- **A sending API through workload identity instead of SMTP credentials.** It ties the host to one provider's API, where
  SMTP serves self-hosters too. Rejected.
- **Chosen:** Amazon SES in the cell's region, over SMTP.

## Consequences

- Mail costs about $0.10 per 1,000 emails.
- The subprocessors stay Akamai with Aiven, Cloudflare, Grafana Labs and AWS.
- SES's production access is requested for `us-east-1` before the first customer.
- The mail records belong to the `ncodeid.net` zone's OpenTofu.
- The product needs a mail port with an SMTP adapter before it can send.

## References

- [ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md): the
  platform's infrastructure code, which holds the zone's OpenTofu.
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md):
  the platform's domains, DMARC policy and the regional rule for a cell's subprocessors.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the subprocessors.
- [ADR-1526](1526-the-platforms-cluster-secrets-reach-each-cluster-from-aws-parameter-store-through-external-secrets-operator.md):
  the cell secret that holds the SMTP credentials.
- Issues [#750](https://github.com/NCodeGroup/identity/issues/750),
  [#859](https://github.com/NCodeGroup/identity/issues/859) and
  [#911](https://github.com/NCodeGroup/identity/issues/911).
