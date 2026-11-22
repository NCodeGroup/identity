# 1526. The platform's cluster secrets reach each cluster from AWS Parameter Store through External Secrets Operator

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** NCode Group

## Context

The hosted platform's clusters and cells need secrets: a cell's database and Valkey credentials, a cluster's
cert-manager DNS-01 token and its collector's token. Each reaches the pods that read it as a Kubernetes Secret. Forces:

- **The infrastructure repository is public**
  ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)), so
  nothing secret may sit in its history, not even encrypted.
- **OpenTofu owns cloud resources and Flux owns what runs in a cluster**
  ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)'s
  layering), so the mechanism must not make CI hold credentials to a cluster's API.
- **The chart reads every credential from a Secret by name**
  ([ADR-1458](1458-the-host-chart-deploys-the-server-and-an-opt-in-management-console-and-refuses-unsafe-releases.md),
  [ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md),
  [ADR-1510](1510-the-host-chart-passes-a-valkey-servers-address-and-password-as-parts-and-the-adapter-assembles-the-connection.md)),
  so any mechanism that produces a Secret of that name serves it.
- **Vendor tokens already live as Parameter Store `SecureString`s**, read through the GitHub OIDC roles
  ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)).
- **Each cluster reaches AWS KMS for the root key** through web identity federation, or IAM Roles Anywhere for a private
  issuer ([#177](https://github.com/NCodeGroup/identity/issues/177)).
- **A cell's operational data stays in its region**
  ([ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).

## Decision

**Each cluster's External Secrets Operator reads the cluster's and its cells' secrets from AWS Systems Manager Parameter
Store and writes them as Kubernetes Secrets; Git holds only the parameters' names.**

1. **Store.** The secrets are Parameter Store standard-tier `SecureString` parameters in the AWS region the cell's
   `Cell:CloudRegions` names, encrypted under a customer-managed KMS key of the cell's own, under a path prefix of the
   cell's own. A cluster-wide secret, such as cert-manager's DNS-01 token or the collector's token, sits under the
   cluster's prefix. The parameters are named as the platform names its AWS resources
   ([#751](https://github.com/NCodeGroup/identity/issues/751)).
2. **Reader.** The operator runs in every cluster as an in-cluster component Flux reconciles. Each cell's namespace has
   a `SecretStore` that authenticates as the namespace's service account through the federation #177 sets up, to an IAM
   role allowed only `ssm:GetParameter*` under the cell's prefix and `kms:Decrypt` on its key. An `ExternalSecret` in
   Git names each parameter and the Secret the chart reads; its `refreshInterval` is one hour.
3. **Writers.** OpenTofu writes a secret it generates through an ephemeral resource and the parameter's write-only value
   argument, so the value is in neither plan nor state. A credential a vendor issues, such as a managed database's
   administrator, is written as the provider returns it and stays only in state that OpenTofu encrypts client-side
   ([ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)).
4. **Rotation.** A changed parameter value reaches the Secret within the refresh interval, and a rollout restarts the
   pods that read it.
5. **The lab** uses the same mechanism from OCI.

## Options considered

- **SOPS with age.** Rejected: every secret's ciphertext stays in the public repository's history, readable by anyone
  who ever obtains the age key, rotation needs a commit, and the age key is itself a secret each cluster needs first.
- **Sealed Secrets.** Rejected for the same reasons, with a key per cluster.
- **OpenTofu writing Kubernetes Secrets,** as #177 sketches for the bootstrap secrets. Rejected: CI would hold
  credentials to every cluster's API, across the layering, and the values sit in state.
- **AWS Secrets Manager.** Rejected: it costs $0.40 per secret a month and adds rotation functions the platform doesn't
  need, where Parameter Store's standard tier costs nothing.
- **A vault per cloud vendor.** Rejected: Akamai has none, so the fleet would run two mechanisms.
- **HashiCorp Vault or OpenBao.** Rejected: a stateful, highly available system to run, unseal and back up.
- **Chosen:** External Secrets Operator reading Parameter Store `SecureString`s under a key and a prefix per cell.

## Consequences

- No secret, plain or encrypted, enters Git.
- A cell's secrets stay in its region under its own key.
- One mechanism serves every vendor's clusters.
- The operator and its roles are built in `NCodeGroup/identity-platform`.
- Self-hosters keep creating Secrets their own way, because the chart only names them.

## References

- [ADR-1132](1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md): the public
  infrastructure code, its layering and its state encryption.
- [ADR-1134](1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md): the operating
  principles.
- [ADR-1458](1458-the-host-chart-deploys-the-server-and-an-opt-in-management-console-and-refuses-unsafe-releases.md):
  the host chart.
- [ADR-1495](1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md):
  the platform's regions.
- [ADR-1509](1509-the-host-chart-passes-a-postgres-operators-secret-as-the-connections-parts-and-the-host-assembles-the-npgsql-connection.md):
  the Postgres Secret.
- [ADR-1510](1510-the-host-chart-passes-a-valkey-servers-address-and-password-as-parts-and-the-adapter-assembles-the-connection.md):
  the Valkey Secret.
- [ADR-1521](1521-the-hosted-platform-runs-production-on-akamai-lke-in-washington-dc-and-its-lab-on-oci-oke-basic-in-phoenix.md):
  the platform's clusters.
- Issues [#177](https://github.com/NCodeGroup/identity/issues/177),
  [#751](https://github.com/NCodeGroup/identity/issues/751) and
  [#909](https://github.com/NCodeGroup/identity/issues/909).
