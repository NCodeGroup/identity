# Documentation

Project documentation for the **NCode.Identity** library family.

## Capabilities

[`capabilities/`](./capabilities/README.md) describes what the product does and promises, and why it's built that way:
an overview for evaluators first, then the architecture, with links to the ADRs and code behind each capability. The
pages are [Encryption at Rest](./capabilities/encryption-at-rest.md), [Key Rollover](./capabilities/key-rollover.md),
[Selective Disclosure (SD-JWT)](./capabilities/selective-disclosure.md) and [COSE and CWT](./capabilities/cose-and-cwt.md).

## Architecture Decision Records (ADRs)

Architecturally significant decisions are captured as ADRs under [`decisions/`](./decisions), with the project
template's general decisions in [`decisions/project-template/`](./decisions/project-template/README.md). See the
[ADR index](./decisions/README.md) for the list and the process we follow.

## Architecture and assurance

[`architecture.md`](./architecture.md) and [`assurance-case.md`](./assurance-case.md) give the components and the rules
the design keeps, and why the design meets its security requirements.
[`best-practices-badge.md`](./best-practices-badge.md) says where the repository meets each OpenSSF Best Practices
criterion that needs a document, and [`best-practices-criteria.md`](./best-practices-criteria.md), which the project
template owns, maps every criterion of the three levels to its answer and evidence.

## Audit records

[`audit-records.md`](./audit-records.md) documents the audit record: its JSON fields, the vocabularies of its values, the
catalog of every audit event, and how the record reaches logs, traces and metrics.

## Metrics

[`metrics.md`](./metrics.md) catalogs every meter, instrument, and attribute the family emits, and how a host collects
them.

## Hosting

[`hosting-analysis.md`](./hosting-analysis.md) describes the hosted platform's topology — production on Akamai, the lab
on OCI, and the stages from the developer plan to paid and dedicated cells — with the record that decides each part, the
compute sizing and capacity, the prices of the platform's vendors, Traefik mTLS guidance, the AWS growth path and the
decisions still open.

[`platform-bootstrap.md`](./platform-bootstrap.md) is the step-by-step runbook for the manual operations that create the
hosted platform from scratch — accounts, the infrastructure repository, credentials, and registrar delegation.

[`incident-response.md`](./incident-response.md) is the hosted platform's incident-response runbook: severities, paging,
triage, containment playbooks, customer communication, evidence and review.

[`legal/`](./legal/README.md) holds the hosted platform's terms of service, data processing agreement and privacy
policy.

[`benchmarks/load/`](../benchmarks/load/README.md) holds the k6 load test that rates a cell's capacity.

[`first-deployed-instance.md`](./first-deployed-instance.md) walks the first use of a deployed instance: the
prerequisites, the install, the schema, the root administrator that setup creates, the management console, the grants
and the removal of the setup Secrets.

## Designs

[`backend-for-frontend.md`](./backend-for-frontend.md) is the proposed design for a Backend for Frontend (BFF) library
— browser cookie sessions with server-side token custody per RFC 10017 — contrasting it with the bearer-token pattern
(issue [#123](https://github.com/NCodeGroup/identity/issues/123)).
