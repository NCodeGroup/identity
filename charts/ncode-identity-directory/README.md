# ncode-identity-directory

The Helm chart for `NCode.Identity.OpenId.GlobalDirectory.Host`, the global directory of a platform of cells. The
directory reserves tenant and team identifiers platform-wide, places each tenant in a cell, publishes team documents and
issues the cells' tokens itself. The chart runs the directory image as a single-replica, non-root deployment that meets
the restricted Pod Security Standard, serves HTTPS only, probes `/health/live` and `/health/ready` over HTTPS, and
drains the pod before stopping it. A Job applies the directory's own database migrations before each rollout. It
requires Kubernetes 1.30 or later.

The directory is a release of its own, with a database of its own that no cell shares. The host chart
(`ncode-identity-host`) installs a cell and reaches the directory as a client. The order in which a platform's releases
are installed is in [Run the cells of a platform](../../docs/platform-bootstrap.md#9-run-the-cells-of-a-platform).

## Before the first install

The chart takes no secret values. It references Secrets you create in the release's namespace:

- `keys.pemSecret`, or the keys of a key management service in `keys.remoteKeys`: the private key that signs the
  directory's tokens. The Secret holds a PEM private key under the key `<keyRing>.pem`, and the key's id is the value of
  `keyRing`. The key ring has at least one key.
- `database.existingSecret` or `database.parts.existingSecret`: the directory's Postgres database as the runtime role's
  connection, the one the pod reads, as a Secret holding the finished Npgsql connection string under the
  `connection-string` key, or as the Secret your Postgres operator writes.
- `migrations.database.existingSecret` or `migrations.database.parts.existingSecret`: the schema owner's connection,
  read only by the migration Job, in the same two forms. The role has the right to create and change the schema; the
  runtime role does not.
- `database.runtimeRole`: the name of the runtime role, a value rather than a Secret. The migration Job grants the role
  access to the directory's tables.
- cert-manager, unless `backendTls.existingSecret` names a Secret: the chart creates the `Certificate` (and, without
  `backendTls.certManager.issuerRef.name`, a private CA) that issues the pod's serving certificate.
- A router that reaches the pod over HTTPS: a Gateway API implementation with `BackendTLSPolicy`, Traefik, or another
  Ingress controller you configure.

Create the database Secrets with the Npgsql connection string of your database; the placeholders are yours:

```bash
kubectl create secret generic directory-database \
  --from-literal=connection-string='Host=<host>;Database=<database>;Username=<runtime role>;Password=<password>'
kubectl create secret generic directory-database-owner \
  --from-literal=connection-string='Host=<host>;Database=<database>;Username=<owner>;Password=<password>'
```

Create the signing key's Secret from a PEM private key, with the key ring's name in the file name:

```bash
kubectl create secret generic directory-key --from-file=directory.pem=<private key file>
```

## The anchor and the control-plane client

The directory pins the platform's trust anchor: the control-plane cell, which is dedicated to the control-plane tenant.
The `controlPlane` values name it, and the directory accepts that cell as the only writer of the platform and the teams.

- `controlPlane.cell`, `controlPlane.region` and `controlPlane.kind` name and place the cell.
- `controlPlane.tenantId` is the identifier of the cell's control-plane tenant, which the directory holds for that cell
  so no other cell reserves it.
- `controlPlane.issuer` is the issuer of the cell, whose bearer tokens the directory accepts.
- `controlPlane.clientJwks` is the JSON Web Key Set, as JSON text, of the control-plane cell's directory client, public
  keys only. The client authenticates to the directory with a key of that set.

Print the key set with the host image's `directory-client-jwks` command, with the control-plane cell's
`Directory:KeyRing` configured and the key ring's Secret mounted or its key management service reachable:

```bash
docker run <host image> directory-client-jwks
```

Put its output into `controlPlane.clientJwks`. The directory initializes the anchor's record and placement before it
serves, and answers `503` until then.

## Install

```bash
helm install directory charts/ncode-identity-directory \
  --set issuer=https://directory.example.com --set keyRing=directory --set keys.pemSecret=directory-key \
  --set controlPlane.cell=control-plane --set controlPlane.region=eu --set controlPlane.tenantId=control-plane \
  --set controlPlane.issuer=https://id.example.com --set-file controlPlane.clientJwks=client-jwks.json \
  --set 'allowedHosts={directory.example.com}' \
  --set database.existingSecret=directory-database --set database.runtimeRole=ncode_directory_app \
  --set migrations.database.existingSecret=directory-database-owner \
  --set ingress.enabled=true --set ingress.controller=traefik --set ingress.className=traefik
```

The files in `ci/` show one release per supported shape, and each is a whole release that CI lints and renders on its
own: `default-values.yaml` (a PEM key from a Secret), `kms-values.yaml` (an AWS KMS key), `gateway-api-values.yaml` (a
Gateway API route), `database-parts-values.yaml` (a Postgres operator's Secret) and `kind-values.yaml` (a kind cluster,
with the image pull policy `Never`).

## One replica

The directory keeps its single-use state in process, so the chart runs exactly one replica with the `Recreate`
strategy: an update replaces the pod with downtime, and two pods never run at once. The chart has no `replicaCount`
value, and the schema refuses a value it does not know.

## What the chart refuses

Rendering fails, before anything is applied, when:

- `allowedHosts` is empty or contains `*`, or its first host is a wildcard, because the probes send the first host;
- `issuer` or `controlPlane.issuer` is empty, is not an `https` URL (an `http` URL on a loopback host is allowed), or
  has user information, a query or a fragment;
- `keyRing` is empty;
- `keys` names neither `pemSecret` nor a remote key, or two keys of the ring share an id, compared without case;
- a remote key's `keyId` is not an AWS KMS key or alias ARN, a Key Vault key version identifier or a Cloud KMS
  crypto key version name, as its `provider` says;
- `controlPlane.clientJwks` is empty or is not a JSON Web Key Set, or `controlPlane.tenantId` is not a tenant
  identifier of 1 to 63 lowercase letters, digits or hyphens;
- `database.runtimeRole` is empty or is not a lower-case Postgres role name;
- neither `database.existingSecret` nor `database.parts.existingSecret` is set, or neither
  `migrations.database.existingSecret` nor `migrations.database.parts.existingSecret` is set, or a connection sets
  both forms, or leaves a required key empty;
- an Ingress is enabled and `ingress.controller` is empty, or `httpRoute.enabled` is `true` and `httpRoute.parentRefs`
  is empty or there is no CA ConfigMap;
- `azure.workloadIdentity` names only one of `tenantId` and `clientId`, or `google.workloadIdentityFederation.provider`
  is not a workload identity pool provider's resource name.

## Migrations

A pre-install and pre-upgrade Job applies the directory's migrations before the rollout, with the schema owner's
credentials from `migrations.database`, and grants `database.runtimeRole` access to the tables. The pod connects as the
runtime role, which owns no table, is no superuser and bypasses no row-level security. A failed Job fails the release
before the rollout starts.

## Transport security

The pod serves HTTPS only, because the chart never sets `LocalDevelopment:Enabled`. The chart mounts the serving
certificate at `/etc/ncode-identity/tls`, and the probes, the Service and the route use the pod's HTTPS port.

- `backendTls.existingSecret` names a `kubernetes.io/tls` Secret with `tls.crt`, `tls.key` and `ca.crt` whose
  certificate names the Service's DNS names. Empty makes the chart create a cert-manager `Certificate`, from
  `backendTls.certManager.issuerRef` or, with no name, from a private CA the chart creates.
- `backendTls.ingressSecret` names the Secret the ingress controller reads, with `ca.crt` to verify the pod's
  certificate and a client certificate. Empty makes the chart issue `<release>-ingress` when a route uses it.
- `backendTls.clientCertificate.required` makes the pod refuse a request whose connection carries no certificate with
  the subject `CN=<backendTls.clientCertificate.commonName>`, except the liveness and readiness probes.
- `backendTls.caConfigMap` names a ConfigMap that holds only `ca.crt`, which the Gateway API mode's `BackendTLSPolicy`
  verifies the pod against. `backendTls.caPublisher.enabled` adds a hook Job that publishes the private CA's `ca.crt` to
  it.

## Routing to the pod

`ingress.enabled` routes every path of each host in `allowedHosts` to the directory. `ingress.controller` is `traefik`,
which adds a `ServersTransport` and the Service annotations, or `other`, which adds nothing: you wire HTTPS to the
Service and the trust in the CA. `httpRoute.enabled` makes a Gateway API `HTTPRoute` and a `BackendTLSPolicy` whose
hostnames are `allowedHosts`, attached to the Gateways in `httpRoute.parentRefs`.

## Values

| Value | Meaning |
| --- | --- |
| `image.repository`, `image.tag`, `image.digest`, `image.pullPolicy` | The directory image. The tag defaults to the chart's `appVersion`; a digest wins over the tag. |
| `imagePullSecrets` | Pull Secrets of the pod. |
| `nameOverride`, `fullnameOverride` | Names of the release's objects. |
| `environment` | `ASPNETCORE_ENVIRONMENT`; the directory runs its deployment posture under every value. |
| `issuer` | `DirectoryServer:Issuer`, the `iss` of the tokens the directory issues. Required. |
| `keyRing` | `DirectoryServer:KeyRing`, the key ring that signs the tokens. Required. |
| `keys.pemSecret` | Secret holding the private key under `<keyRing>.pem`. |
| `keys.remoteKeys` | Keys a key management service holds, by key id: `provider`, `keyId`, `algorithm`, `secretType`, `keySizeBits` and, for an elliptic-curve key, `curve`. |
| `tokenLifetime` | `DirectoryServer:TokenLifetime`, such as `5m`. |
| `controlPlane.cell`, `.region`, `.kind`, `.tenantId`, `.issuer`, `.clientJwks` | The anchor, as [The anchor and the control-plane client](#the-anchor-and-the-control-plane-client) says. All required. |
| `allowedHosts` | `AllowedHosts`; the probes send the first. Required; `*` is refused. |
| `backup.location` | The `ncode.io/backup-location` annotation of the pods that own a database. The chart starts no backup. |
| `telemetry.otlpEndpoint` | `OTEL_EXPORTER_OTLP_ENDPOINT` of every pod. |
| `database.existingSecret`, `.connectionStringKey`, `.parts.*`, `.runtimeRole` | The runtime role's connection, as a connection string or as the parts of a Postgres operator's Secret, and the role's name. |
| `migrations.database.*` | The schema owner's connection, in the same two forms; an empty key falls back to the one under `database`. |
| `migrations.backoffLimit`, `.activeDeadlineSeconds`, `.resources` | The migration Job. |
| `aws.webIdentity.*` | The AWS identity through which the pod reaches AWS KMS keys. |
| `azure.workloadIdentity.*` | The Azure workload identity through which the pod reaches Key Vault keys. |
| `google.workloadIdentityFederation.*` | The Google Cloud workload identity through which the pod reaches Cloud KMS keys. |
| `backendTls.*` | Transport security, as [Transport security](#transport-security) says. |
| `service.type`, `service.port` | The Service. |
| `ingress.enabled`, `.controller`, `.className`, `.annotations`, `.tls` | The Ingress. |
| `httpRoute.enabled`, `.parentRefs`, `.annotations` | The Gateway API route. |
| `serviceAccount.create`, `.name`, `.annotations` | The pod's ServiceAccount, which mounts no token. |
| `preStopSleepSeconds`, `terminationGracePeriodSeconds` | The drain before the directory stops. |
| `probes.startup.*`, `.liveness.*`, `.readiness.*` | Probe periods, timeouts and thresholds. |
| `resources` | Requests and limits of the pod. |
| `nodeSelector`, `tolerations`, `affinity`, `priorityClassName` | Scheduling. |
| `extraEnv`, `extraEnvFrom`, `extraVolumes`, `extraVolumeMounts` | Any other directory setting, as environment variables, and extra volumes. The migration Job receives the environment variables too. |

## Published artifacts

Every merge to `main` and every release tag `v*.*.*` publishes the directory image and this chart at one version through
the Publish artifacts workflow:

- `ghcr.io/ncodegroup/ncode-identity-directory:<version>`, the directory image.
- `oci://ghcr.io/ncodegroup/charts/ncode-identity-directory`, this chart, at version `<version>`.

The chart pins its image tag to its own `appVersion`, so following the chart follows the image.
