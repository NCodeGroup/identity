{{/*
The chart name, overridable by nameOverride.
*/}}
{{- define "identity-host.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
The name of the migration Job. The fully qualified name is shortened first so the name, with its suffix, fits the
63-character label value Kubernetes copies it into (batch.kubernetes.io/job-name).
*/}}
{{- define "identity-host.migrationsJobName" -}}
{{- printf "%s-migrations" (include "identity-host.fullname" . | trunc 52 | trimSuffix "-") }}
{{- end }}

{{/*
The name of the secret protection Job, which carries the release revision so each upgrade can run its own. The fully
qualified name is shortened first so the name, with its suffix, fits the 63-character label value Kubernetes copies it
into (batch.kubernetes.io/job-name).
*/}}
{{- define "identity-host.secretProtectionJobName" -}}
{{- printf "%s-secret-protection-%d" (include "identity-host.fullname" . | trunc 38 | trimSuffix "-") (int .Release.Revision) }}
{{- end }}

{{/*
The name of the setup Job, which carries the release revision so each upgrade can run its own. The fully qualified name
is shortened first so the name, with its suffix, fits the 63-character label value Kubernetes copies it into
(batch.kubernetes.io/job-name).
*/}}
{{- define "identity-host.setupJobName" -}}
{{- printf "%s-setup-%d" (include "identity-host.fullname" . | trunc 49 | trimSuffix "-") (int .Release.Revision) }}
{{- end }}

{{/*
The fully qualified name of the release's resources.
*/}}
{{- define "identity-host.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "identity-host.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
The labels every resource of the release carries.
*/}}
{{- define "identity-host.commonLabels" -}}
helm.sh/chart: {{ include "identity-host.chart" . }}
app.kubernetes.io/name: {{ include "identity-host.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
The labels that select the server's pods. The component keeps the migration Job's pod out of the Service and the
PodDisruptionBudget.
*/}}
{{- define "identity-host.selectorLabels" -}}
app.kubernetes.io/name: {{ include "identity-host.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: server
{{- end }}

{{- define "identity-host.labels" -}}
{{ include "identity-host.commonLabels" . }}
app.kubernetes.io/component: server
{{- end }}

{{- define "identity-host.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "identity-host.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{- define "identity-host.image" -}}
{{- if .Values.image.digest }}
{{- printf "%s@%s" .Values.image.repository .Values.image.digest }}
{{- else }}
{{- printf "%s:%s" .Values.image.repository (default .Chart.AppVersion .Values.image.tag) }}
{{- end }}
{{- end }}

{{/*
The Host header the probes send: probes.host, or else the first allowed host.
*/}}
{{- define "identity-host.probeHost" -}}
{{- .Values.probes.host | default (first .Values.allowedHosts) }}
{{- end }}

{{/*
Whether the release shares single-use state through Valkey.
*/}}
{{- define "identity-host.valkeyEnabled" -}}
{{- if or .Values.valkey.existingSecret .Values.valkey.parts.existingSecret }}true{{ end }}
{{- end }}

{{/*
The Valkey environment entries of a host container: the parts of a Valkey operator's Secret, each from its own key, or
the finished connection string. The scope holds the settings of one connection, and its empty keys fall back to the
defaults, which are the values of the Valkey the host itself uses (ADR-1510).
*/}}
{{- define "identity-host.valkeyEnv" -}}
{{- $scope := .scope }}
{{- $defaults := .defaults }}
{{- if $scope.parts.existingSecret }}
{{- range $part := list (list "Host" "hostKey") (list "Port" "portKey") (list "Username" "usernameKey") (list "Password" "passwordKey") }}
{{- $key := (index $scope.parts (last $part)) | default (index $defaults.parts (last $part)) }}
{{- if $key }}
- name: ValkeyConnection__{{ first $part }}
  valueFrom:
    secretKeyRef:
      name: {{ $scope.parts.existingSecret }}
      key: {{ $key | quote }}
{{- end }}
{{- end }}
{{- if $scope.parts.ssl }}
- name: ValkeyConnection__Ssl
  value: "true"
{{- end }}
{{- else }}
- name: ConnectionStrings__valkey
  valueFrom:
    secretKeyRef:
      name: {{ $scope.existingSecret }}
      key: {{ $scope.connectionStringKey | default $defaults.connectionStringKey | quote }}
{{- end }}
{{- end }}

{{/*
Refuses a Valkey connection that names both of its forms, a finished connection string without its key, or a parts Secret
without a host key. Rendered for its side effect only.
*/}}
{{- define "identity-host.valkeyValidate" -}}
{{- $scope := .scope }}
{{- if and $scope.existingSecret $scope.parts.existingSecret }}
{{- fail (printf "%s.existingSecret and %s.parts.existingSecret are both set: name the Secret that holds the finished connection string or the Secret that holds the server's address and password, not both." .path .path) }}
{{- end }}
{{- if and $scope.existingSecret (not ($scope.connectionStringKey | default .defaults.connectionStringKey)) }}
{{- fail (printf "%s.connectionStringKey must name the key that holds the connection string in %s.existingSecret." .path .path) }}
{{- end }}
{{- if and $scope.parts.existingSecret (not ($scope.parts.hostKey | default .defaults.parts.hostKey)) }}
{{- fail (printf "%s.parts.hostKey must name the key that holds the host in the Secret." .path) }}
{{- end }}
{{- end }}

{{/*
The database environment entries of a host container: the parts of a Postgres operator's Secret, each from its own
key, or the finished connection string. The scope holds the settings of one connection, and its empty keys fall back to
the defaults, which are the values of the database the host itself uses (ADR-1509).
*/}}
{{- define "identity-host.databaseEnv" -}}
{{- $scope := .scope }}
{{- $defaults := .defaults }}
{{- if $scope.parts.existingSecret }}
{{- range $part := list (list "Host" "hostKey") (list "Port" "portKey") (list "Name" "databaseKey") (list "Username" "usernameKey") (list "Password" "passwordKey") }}
{{- $key := (index $scope.parts (last $part)) | default (index $defaults.parts (last $part)) }}
{{- if $key }}
- name: OpenIdDatabase__{{ first $part }}
  valueFrom:
    secretKeyRef:
      name: {{ $scope.parts.existingSecret }}
      key: {{ $key | quote }}
{{- end }}
{{- end }}
{{- else }}
- name: ConnectionStrings__openiddb
  valueFrom:
    secretKeyRef:
      name: {{ $scope.existingSecret }}
      key: {{ $scope.connectionStringKey | default $defaults.connectionStringKey | quote }}
{{- end }}
{{- end }}

{{/*
Refuses a database connection that names both of its forms, or none when it is required, a finished connection string
without its key, or a parts Secret whose required key is empty. Rendered for its side effect only.
*/}}
{{- define "identity-host.databaseValidate" -}}
{{- $scope := .scope }}
{{- if and $scope.existingSecret $scope.parts.existingSecret }}
{{- fail (printf "%s.existingSecret and %s.parts.existingSecret are both set: name the Secret that holds the finished connection string or the Secret that holds its parts, not both." .path .path) }}
{{- end }}
{{- if and .required (not $scope.existingSecret) (not $scope.parts.existingSecret) }}
{{- fail (printf "%s.existingSecret must name the Secret holding the openiddb connection string, or %s.parts.existingSecret the Secret holding its host, database, user and password." .path .path) }}
{{- end }}
{{- if and $scope.existingSecret (not ($scope.connectionStringKey | default .defaults.connectionStringKey)) }}
{{- fail (printf "%s.connectionStringKey must name the key that holds the connection string in %s.existingSecret." .path .path) }}
{{- end }}
{{- if $scope.parts.existingSecret }}
{{- range $part := list (list "host" "hostKey") (list "database" "databaseKey") (list "user" "usernameKey") (list "password" "passwordKey") }}
{{- $keyName := last $part }}
{{- if not ((index $scope.parts $keyName) | default (index $.defaults.parts $keyName)) }}
{{- fail (printf "%s.parts.%s must name the key that holds the %s in the Secret." $.path $keyName (first $part)) }}
{{- end }}
{{- end }}
{{- end }}
{{- end }}

{{/*
The pattern of the ARN of an AWS KMS key or alias.
*/}}
{{- define "identity-host.awsKmsArnPattern" -}}
^arn:aws[a-z-]*:kms:[a-z0-9-]+:[0-9]{12}:(key/[A-Za-z0-9-]+|alias/[A-Za-z0-9/_-]+)$
{{- end }}

{{/*
The pattern of a service URL: https, or http on a loopback host, written with its authority and with no user
information, query or fragment.
*/}}
{{- define "identity-host.serviceUrlPattern" -}}
(?i)^(https://[^/?#@\s]+|http://(localhost|127\.0\.0\.1|\[::1\])(:[0-9]+)?)(/[^?#\s]*)?$
{{- end }}

{{/*
The pattern of a service URL that must be https: written with its authority and with no user information, query or
fragment.
*/}}
{{- define "identity-host.httpsServiceUrlPattern" -}}
(?i)^https://[^/?#@\s]+(/[^?#\s]*)?$
{{- end }}

{{/*
Refuses a service URL that is set and is not a valid one. Takes the path of the value (path), the value (value) and
whether it must be https (httpsOnly), which otherwise also allows http on a loopback host. Rendered for its side effect
only.
*/}}
{{- define "identity-host.serviceUrlValidate" -}}
{{- if .value }}
{{- if .httpsOnly }}
{{- if not (regexMatch (include "identity-host.httpsServiceUrlPattern" .) .value) }}
{{- fail (printf "%s must be an absolute https URL with no user information, query or fragment." .path) }}
{{- end }}
{{- else if not (regexMatch (include "identity-host.serviceUrlPattern" .) .value) }}
{{- fail (printf "%s must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment." .path) }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Refuses a remote key id that does not name a key version the provider can sign with. Takes the path of the value, the
provider, the key id and an example of the key's name (keyName) and Google Cloud KMS key ring (keyRing). Rendered for its
side effect only.
*/}}
{{- define "identity-host.remoteKeyIdValidate" -}}
{{- if eq .provider "aws-kms" }}
{{- if not (regexMatch (include "identity-host.awsKmsArnPattern" .) .keyId) }}
{{- fail (printf "%s must be the ARN of an AWS KMS key or alias, such as arn:aws:kms:us-west-2:111122223333:key/1234abcd-12ab-34cd-56ef-1234567890ab." .path) }}
{{- end }}
{{- else if eq .provider "azure-key-vault" }}
{{- if not (regexMatch "^https://[A-Za-z0-9-]+([.][A-Za-z0-9-]+)+/keys/[A-Za-z0-9-]{1,127}/[A-Za-z0-9]{32}$" .keyId) }}
{{- fail (printf "%s must be the identifier of a Key Vault or Managed HSM key version, such as https://contoso.vault.azure.net/keys/%s/0123456789abcdef0123456789abcdef." .path .example.keyName) }}
{{- end }}
{{- else if eq .provider "google-cloud-kms" }}
{{- if not (regexMatch "^projects/[a-z0-9.:-]+/locations/[a-z0-9-]+/keyRings/[A-Za-z0-9_-]{1,63}/cryptoKeys/[A-Za-z0-9_-]{1,63}/cryptoKeyVersions/[0-9]{1,19}$" .keyId) }}
{{- fail (printf "%s must be the resource name of a Cloud KMS crypto key version, such as projects/my-project/locations/us-central1/keyRings/%s/cryptoKeys/%s/cryptoKeyVersions/1." .path .example.keyRing .example.keyName) }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Refuses a release the host would refuse, or one that would run replicas without shared state, before anything is
applied. Rendered for its side effect only.
*/}}
{{- define "identity-host.validate" -}}
{{- if not .Values.allowedHosts }}
{{- fail "allowedHosts must list the host names the host answers to." }}
{{- end }}
{{- if has "*" .Values.allowedHosts }}
{{- fail "allowedHosts must not contain '*': it accepts every Host header." }}
{{- end }}
{{- if contains "*" (include "identity-host.probeHost" .) }}
{{- fail "probes.host must name a concrete host that allowedHosts accepts: the first allowed host is a wildcard." }}
{{- end }}
{{- include "identity-host.databaseValidate" (dict "path" "database" "scope" .Values.database "defaults" .Values.database "required" true) }}
{{- include "identity-host.databaseValidate" (dict "path" "migrations.database" "scope" .Values.migrations.database "defaults" .Values.database "required" .Values.migrations.enabled) }}
{{- if and .Values.migrations.enabled (not .Values.database.runtimeRole) }}
{{- fail "database.runtimeRole must name the Postgres role the host connects as when migrations.enabled is true: the migration Job grants it access to the tables." }}
{{- end }}
{{- if and .Values.database.runtimeRole (not (regexMatch "^[a-z_][a-z0-9_]*$" .Values.database.runtimeRole)) }}
{{- fail "database.runtimeRole must be a lower-case Postgres role name of letters, digits and underscores that does not start with a digit." }}
{{- end }}
{{- include "identity-host.valkeyValidate" (dict "path" "valkey" "scope" .Values.valkey "defaults" .Values.valkey) }}
{{- if and .Values.directory.url (not .Values.directory.clientId) }}
{{- fail "directory.clientId must name the cell's machine client when directory.url is set." }}
{{- end }}
{{- if and .Values.directory.url (not .Values.directory.keyRing) }}
{{- fail "directory.keyRing must name the key ring that signs the cell's client assertions when directory.url is set." }}
{{- end }}
{{- if and (not .Values.directory.url) .Values.directory.clientId }}
{{- fail "directory.clientId is set and directory.url is empty: set directory.url, or clear directory.clientId." }}
{{- end }}
{{- if and (not .Values.directory.url) .Values.directory.keyRing }}
{{- fail "directory.keyRing is set and directory.url is empty: set directory.url, or clear directory.keyRing." }}
{{- end }}
{{- if and (not .Values.directory.url) .Values.directory.keys.pemSecret }}
{{- fail "directory.keys.pemSecret is set and directory.url is empty: set directory.url, or clear directory.keys.pemSecret." }}
{{- end }}
{{- if and (not .Values.directory.url) .Values.directory.keys.remoteKeys }}
{{- fail "directory.keys.remoteKeys is set and directory.url is empty: set directory.url, or clear directory.keys.remoteKeys." }}
{{- end }}
{{- include "identity-host.serviceUrlValidate" (dict "path" "directory.url" "value" .Values.directory.url "httpsOnly" false) }}
{{- include "identity-host.serviceUrlValidate" (dict "path" "directory.issuer" "value" .Values.directory.issuer "httpsOnly" false) }}
{{- include "identity-host.serviceUrlValidate" (dict "path" "cell.managementUrl" "value" .Values.cell.managementUrl "httpsOnly" true) }}
{{- if not .Values.cell.isControlPlane }}
{{- include "identity-host.serviceUrlValidate" (dict "path" "cell.controlPlaneIssuer" "value" .Values.cell.controlPlaneIssuer "httpsOnly" false) }}
{{- end }}
{{- $directory := .Values.directory }}
{{- if and (not $directory.enabled) (or $directory.issuer $directory.controlPlaneCell $directory.allowedHosts $directory.database.runtimeRole $directory.database.existingSecret $directory.migrations.database.existingSecret $directory.tls.existingSecret $directory.extraEnv $directory.extraEnvFrom $directory.extraVolumes $directory.extraVolumeMounts $directory.serviceAccount.name $directory.serviceAccount.annotations $directory.image.tag $directory.image.digest) }}
{{- fail "directory.issuer, directory.controlPlaneCell, directory.allowedHosts, directory.database, directory.migrations.database, directory.tls, directory.serviceAccount, directory.image and directory.extra* configure the directory host, which runs only when directory.enabled is true: set directory.enabled, or clear them." }}
{{- end }}
{{- if and .Values.directory.url .Values.directory.keyRing }}
{{- $directoryKeys := .Values.directory.keys }}
{{- if and (not $directoryKeys.pemSecret) (not $directoryKeys.remoteKeys) }}
{{- fail (printf "directory.keys must name pemSecret or list a remote key: the directory client's key ring %s needs at least one key." .Values.directory.keyRing) }}
{{- end }}
{{- $directoryKeyIds := list }}
{{- if $directoryKeys.pemSecret }}
{{- $directoryKeyIds = append $directoryKeyIds (lower .Values.directory.keyRing) }}
{{- end }}
{{- range $kid, $key := $directoryKeys.remoteKeys }}
{{- $directoryKeyIds = append $directoryKeyIds (lower $kid) }}
{{- $path := printf "directory.keys.remoteKeys.%s.keyId" $kid }}
{{- include "identity-host.remoteKeyIdValidate" (dict "path" $path "provider" $key.provider "keyId" $key.keyId "example" (dict "keyName" "cell-client" "keyRing" "ncode-identity-cell")) }}
{{- end }}
{{- if ne (len $directoryKeyIds) (len (uniq $directoryKeyIds)) }}
{{- fail (printf "directory.keys holds two keys with the same id: each remote key's id differs from every other key's in the ring, compared without case, and from %s when pemSecret is set." .Values.directory.keyRing) }}
{{- end }}
{{- end }}
{{- if and (eq .Values.tenantResolution.strategyCode "StaticSingle") (not .Values.tenantResolution.staticSingle.issuer) }}
{{- fail "tenantResolution.staticSingle.issuer must be the public issuer URL." }}
{{- end }}
{{- if and .Values.apiReference.pageEnabled (not .Values.apiReference.documentEnabled) }}
{{- fail "apiReference.pageEnabled requires apiReference.documentEnabled: the page renders the OpenAPI document the host serves." }}
{{- end }}
{{- if and (eq .Values.secretProtection.rootKeyProvider "aws-kms") (not .Values.secretProtection.awsKms.keyArn) }}
{{- fail "secretProtection.awsKms.keyArn must name the KMS key or alias that holds the root key." }}
{{- end }}
{{- if and (eq .Values.secretProtection.rootKeyProvider "aws-kms") .Values.secretProtection.awsKms.keyArn (not (regexMatch (include "identity-host.awsKmsArnPattern" .) .Values.secretProtection.awsKms.keyArn)) }}
{{- fail "secretProtection.awsKms.keyArn must be the ARN of a KMS key or alias, such as arn:aws:kms:us-west-2:111122223333:alias/ncode-identity-root." }}
{{- end }}
{{- if eq .Values.secretProtection.rootKeyProvider "azure-key-vault" }}
{{- $azureKeyVault := .Values.secretProtection.azureKeyVault }}
{{- if not (regexMatch "^https://[A-Za-z0-9-]+([.][A-Za-z0-9-]+)+/keys/[A-Za-z0-9-]{1,127}(/[A-Za-z0-9]{32})?$" $azureKeyVault.keyId) }}
{{- fail "secretProtection.azureKeyVault.keyId must be the identifier of a Key Vault or Managed HSM key, such as https://contoso.vault.azure.net/keys/ncode-identity-root." }}
{{- end }}
{{- end }}
{{- include "identity-host.kmsCredentials.validate" (dict "credentials" (include "identity-host.kmsCredentials.server" . | fromYaml) "path" "secretProtection") }}
{{- if eq .Values.secretProtection.rootKeyProvider "google-cloud-kms" }}
{{- $googleCloudKms := .Values.secretProtection.googleCloudKms }}
{{- if not (regexMatch "^projects/[a-z0-9.:-]+/locations/[a-z0-9-]+/keyRings/[A-Za-z0-9_-]{1,63}/cryptoKeys/[A-Za-z0-9_-]{1,63}$" $googleCloudKms.keyName) }}
{{- fail "secretProtection.googleCloudKms.keyName must be the resource name of a Cloud KMS crypto key, such as projects/my-project/locations/us-central1/keyRings/ncode-identity/cryptoKeys/root." }}
{{- end }}
{{- end }}
{{- $remoteKeyProviders := .Values.secretProtection.remoteKeyProviders }}
{{- range $vendor, $location := dict "awsKms" "region" "azureKeyVault" "vaultUri" "googleCloudKms" "keyRing" }}
{{- $provider := index $remoteKeyProviders $vendor }}
{{- if and (index $provider $location) (not $provider.enabled) }}
{{- fail (printf "secretProtection.remoteKeyProviders.%s.%s is set and %s.enabled is false: set enabled to register the provider, or clear %s." $vendor $location $vendor $location) }}
{{- end }}
{{- end }}
{{- if .Values.secretProtection.rewrapJob.enabled }}
{{- if and (eq .Values.secretProtection.rewrapJob.command "rewrap-recovery") (not .Values.secretProtection.recovery.publicKeyConfigMap) }}
{{- fail "secretProtection.rewrapJob.command rewrap-recovery replaces the recovery copies, so secretProtection.recovery.publicKeyConfigMap must name the recovery public key ConfigMap." }}
{{- end }}
{{- end }}
{{- if and .Values.setup.token.existingSecret (not .Values.setup.token.key) }}
{{- fail "setup.token.key must name the key that holds the setup token in setup.token.existingSecret." }}
{{- end }}
{{- if and .Values.setup.job.enabled (not .Values.setup.job.existingSecret) }}
{{- fail "setup.job.existingSecret must name the Secret holding the root administrator's user name and password when setup.job.enabled is true." }}
{{- end }}
{{- if and .Values.setup.job.enabled (not .Values.setup.job.userNameKey) }}
{{- fail "setup.job.userNameKey must name the key that holds the root administrator's user name in setup.job.existingSecret." }}
{{- end }}
{{- if and .Values.setup.job.enabled (not .Values.setup.job.passwordKey) }}
{{- fail "setup.job.passwordKey must name the key that holds the root administrator's password in setup.job.existingSecret." }}
{{- end }}
{{- if and .Values.setup.job.enabled .Values.setup.managementConsole.baseUrl (not .Values.setup.managementConsole.jwks) }}
{{- fail "setup.managementConsole.jwks must hold the console's public JWK Set when setup.managementConsole.baseUrl is set and setup.job.enabled is true." }}
{{- end }}
{{- if and .Values.setup.job.enabled .Values.setup.managementConsole.baseUrl (not .Values.setup.managementConsole.clientId) }}
{{- fail "setup.managementConsole.clientId must name the console's client_id when setup.managementConsole.baseUrl is set and setup.job.enabled is true." }}
{{- end }}
{{- if and .Values.managementConsole.enabled (not .Values.setup.managementConsole.clientId) }}
{{- fail "setup.managementConsole.clientId must name the console's client_id when managementConsole.enabled is true: the setup wizard hints it, and the host refuses an empty one." }}
{{- end }}
{{- if and .Values.setup.job.enabled .Values.setup.automation.clientId (not .Values.setup.automation.jwks) }}
{{- fail "setup.automation.jwks must hold the automation client's public JWK Set when setup.automation.clientId is set and setup.job.enabled is true." }}
{{- end }}
{{- /*
The chart never sets LocalDevelopment:Enabled, and the image carries no local-development overlay, so the host runs the
deployment posture under every environment value. The checks below are the chart's own refusals at render time.
*/}}
{{- if not .Values.secretProtection.rootKeyProvider }}
{{- fail "secretProtection.rootKeyProvider is required: local, aws-kms, azure-key-vault or google-cloud-kms." }}
{{- end }}
{{- $secretProtection := .Values.secretProtection }}
{{- if eq $secretProtection.rootKeyProvider "ephemeral" }}
{{- fail (print "secretProtection.rootKeyProvider must name a root key held outside the pod: "
    "local, aws-kms, azure-key-vault or google-cloud-kms.") }}
{{- end }}
{{- if and (eq $secretProtection.rootKeyProvider "local") (not $secretProtection.local.keySecret) }}
{{- fail "secretProtection.local.keySecret must name the Secret holding the local root key." }}
{{- end }}
{{- if and (not $secretProtection.recovery.publicKeyConfigMap) (not $secretProtection.recovery.disabled) }}
{{- fail "secretProtection.recovery must name the recovery public key ConfigMap, or set disabled to accept that losing the root key loses every stored secret." }}
{{- end }}
{{- if and (gt (int .Values.replicaCount) 1) (not (include "identity-host.valkeyEnabled" .)) }}
{{- fail "replicaCount above 1 requires valkey.existingSecret or valkey.parts.existingSecret: replicas share single-use state through Valkey." }}
{{- end }}
{{- end }}

{{/*
The pod security context of every pod the chart runs: the restricted Pod Security Standard as the image's non-root
'app' user, whose group can read the mounted secrets.
*/}}
{{- define "identity-host.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: 1654
runAsGroup: 1654
fsGroup: 1654
seccompProfile:
  type: RuntimeDefault
{{- end }}

{{/*
The security context of every container the chart runs.
*/}}
{{- define "identity-host.containerSecurityContext" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop:
    - ALL
{{- end }}

{{/*
The image pull secrets and scheduling constraints of every pod the chart runs, as pod spec fields.
*/}}
{{- define "identity-host.imagePullSecrets" -}}
{{- with .Values.imagePullSecrets }}
imagePullSecrets:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- end }}

{{- define "identity-host.scheduling" -}}
{{- with .Values.nodeSelector }}
nodeSelector:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- with .Values.affinity }}
affinity:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- with .Values.tolerations }}
tolerations:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- end }}

{{/*
Whether the release uses the named key management service, as the root key provider, as a provider a tenant may bring a
root key from (secretProtection.ownerRootKeys.providers), or as a provider that holds tenants' keys
(enabled under secretProtection.remoteKeyProviders), so that the pod carries that provider's workload credentials. Takes
a dict with the chart context as root and the key management service name as name; renders "true" or nothing.
*/}}
{{- define "identity-host.usesKeyManagementService" -}}
{{- $remoteKeyProviders := .root.Values.secretProtection.remoteKeyProviders }}
{{- $remoteKeyProvider := or (and (eq .name "aws-kms") $remoteKeyProviders.awsKms.enabled) (and (eq .name "azure-key-vault") $remoteKeyProviders.azureKeyVault.enabled) (and (eq .name "google-cloud-kms") $remoteKeyProviders.googleCloudKms.enabled) }}
{{- if or (eq .root.Values.secretProtection.rootKeyProvider .name) (has .name .root.Values.secretProtection.ownerRootKeys.providers) $remoteKeyProvider }}true{{ end }}
{{- end }}

{{/*
The workload credentials of the host, as YAML that the kmsCredentials helpers take as a dict (after fromYaml): name is
the component's full name, awsKms, azureKeyVault and googleCloudKms are "true" or empty by whether the component uses
that service, and webIdentity, workloadIdentity and workloadIdentityFederation are its identity values in the shapes of
secretProtection.awsKms.webIdentity, secretProtection.azureKeyVault.workloadIdentity and
secretProtection.googleCloudKms.workloadIdentityFederation. Takes the chart context.
*/}}
{{- define "identity-host.kmsCredentials.server" -}}
name: {{ include "identity-host.fullname" . | quote }}
awsKms: {{ include "identity-host.usesKeyManagementService" (dict "root" . "name" "aws-kms") | quote }}
azureKeyVault: {{ include "identity-host.usesKeyManagementService" (dict "root" . "name" "azure-key-vault") | quote }}
googleCloudKms: {{ include "identity-host.usesKeyManagementService" (dict "root" . "name" "google-cloud-kms") | quote }}
webIdentity:
  {{- toYaml .Values.secretProtection.awsKms.webIdentity | nindent 2 }}
workloadIdentity:
  {{- toYaml .Values.secretProtection.azureKeyVault.workloadIdentity | nindent 2 }}
workloadIdentityFederation:
  {{- toYaml .Values.secretProtection.googleCloudKms.workloadIdentityFederation | nindent 2 }}
{{- end }}

{{/*
The external_account credential configuration that Google Application Default Credentials read: it exchanges the
projected service-account token with Google STS, and impersonates a service account when one is named. Empty unless the
component uses Google Cloud KMS through workload identity federation. The token path must match the volume that
kmsCredentials.volumes projects. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-host.kmsCredentials.googleCloudCredentialConfiguration" -}}
{{- $federation := .workloadIdentityFederation }}
{{- if and .googleCloudKms .workloadIdentityFederation.provider }}
{{- $configuration := dict "type" "external_account" "audience" (printf "//iam.googleapis.com/%s" $federation.provider) "subject_token_type" "urn:ietf:params:oauth:token-type:jwt" "token_url" "https://sts.googleapis.com/v1/token" "credential_source" (dict "file" "/var/run/secrets/tokens/google/token" "format" (dict "type" "text")) }}
{{- if $federation.serviceAccountEmail }}
{{- $_ := set $configuration "service_account_impersonation_url" (printf "https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/%s:generateAccessToken" $federation.serviceAccountEmail) }}
{{- end }}
{{- $configuration | toPrettyJson }}
{{- end }}
{{- end }}

{{/*
The AWS web identity environment entries: the SDK exchanges the projected token for short-lived credentials of the role.
Empty unless the component uses AWS KMS and names a role. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-host.kmsCredentials.awsEnv" -}}
{{- $awsWebIdentityRoot := "/var/run/secrets/tokens/aws" }}
{{- /* A pod that uses AWS KMS neither for the root key, nor for a tenant's root key, nor for a tenant's keys never carries AWS credentials, whatever the web identity values say. */}}
{{- if and .awsKms .webIdentity.roleArn }}
# The SDK exchanges the projected token for short-lived credentials of this role.
- name: AWS_ROLE_ARN
  value: {{ .webIdentity.roleArn | quote }}
- name: AWS_WEB_IDENTITY_TOKEN_FILE
  value: {{ printf "%s/token" $awsWebIdentityRoot }}
- name: AWS_ROLE_SESSION_NAME
  value: {{ .name | quote }}
{{- end }}
{{- end }}

{{/*
The Azure workload identity environment entries: Azure.Identity exchanges the projected token for an Entra token of the
identity. Empty unless the component uses Azure Key Vault and names a client. Takes the dict that kmsCredentials.server
renders.
*/}}
{{- define "identity-host.kmsCredentials.azureEnv" -}}
{{- $azureWorkloadIdentityRoot := "/var/run/secrets/tokens/azure" }}
{{- /* A pod that uses Azure Key Vault neither for the root key, nor for a tenant's root key, nor for a tenant's keys never carries Azure credentials, whatever the workload identity values say. */}}
{{- if and .azureKeyVault .workloadIdentity.clientId }}
# Azure.Identity exchanges the projected token for an Entra token of this identity, and tries no other
# credential.
- name: AZURE_TENANT_ID
  value: {{ .workloadIdentity.tenantId | quote }}
- name: AZURE_CLIENT_ID
  value: {{ .workloadIdentity.clientId | quote }}
- name: AZURE_FEDERATED_TOKEN_FILE
  value: {{ printf "%s/token" $azureWorkloadIdentityRoot }}
- name: AZURE_TOKEN_CREDENTIALS
  value: WorkloadIdentityCredential
{{- end }}
{{- end }}

{{/*
The Google Application Default Credentials environment entry, which names the credential configuration that exchanges
the projected token with Google STS. Empty unless the component uses Google Cloud KMS through workload identity
federation. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-host.kmsCredentials.googleEnv" -}}
{{- $googleCloudTokenRoot := "/var/run/secrets/tokens/google" }}
{{- if include "identity-host.kmsCredentials.googleCloudCredentialConfiguration" . }}
# Application Default Credentials read this configuration, which exchanges the projected token with Google
# STS.
- name: GOOGLE_APPLICATION_CREDENTIALS
  value: {{ printf "%s/credential-configuration.json" $googleCloudTokenRoot }}
{{- end }}
{{- end }}

{{/*
The volume mounts of the AWS, Azure and Google workload credentials, each present only when the component uses that
service with an identity. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-host.kmsCredentials.volumeMounts" -}}
{{- $awsWebIdentityRoot := "/var/run/secrets/tokens/aws" }}
{{- $azureWorkloadIdentityRoot := "/var/run/secrets/tokens/azure" }}
{{- $googleCloudTokenRoot := "/var/run/secrets/tokens/google" }}
{{- if and .awsKms .webIdentity.roleArn }}
- name: aws-web-identity
  mountPath: {{ $awsWebIdentityRoot }}
  readOnly: true
{{- end }}
{{- if and .azureKeyVault .workloadIdentity.clientId }}
- name: azure-workload-identity
  mountPath: {{ $azureWorkloadIdentityRoot }}
  readOnly: true
{{- end }}
{{- if include "identity-host.kmsCredentials.googleCloudCredentialConfiguration" . }}
- name: google-cloud-credentials
  mountPath: {{ $googleCloudTokenRoot }}
  readOnly: true
{{- end }}
{{- end }}

{{/*
The volumes of the AWS, Azure and Google workload credentials, each present only when the component uses that service
with an identity. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-host.kmsCredentials.volumes" -}}
{{- if and .awsKms .webIdentity.roleArn }}
# The pod does not mount the default service account token, so this one is projected explicitly, for AWS only.
- name: aws-web-identity
  projected:
    sources:
      - serviceAccountToken:
          audience: {{ .webIdentity.audience | quote }}
          expirationSeconds: {{ .webIdentity.tokenExpirationSeconds }}
          path: token
{{- end }}
{{- if and .azureKeyVault .workloadIdentity.clientId }}
# The pod does not mount the default service account token, so this one is projected explicitly, for Azure only.
- name: azure-workload-identity
  projected:
    sources:
      - serviceAccountToken:
          audience: {{ .workloadIdentity.audience | quote }}
          expirationSeconds: {{ .workloadIdentity.tokenExpirationSeconds }}
          path: token
{{- end }}
{{- if include "identity-host.kmsCredentials.googleCloudCredentialConfiguration" . }}
# The token and the configuration that exchanges it, projected together, for Google Cloud only.
- name: google-cloud-credentials
  projected:
    sources:
      - serviceAccountToken:
          audience: {{ printf "https://iam.googleapis.com/%s" .workloadIdentityFederation.provider | quote }}
          expirationSeconds: {{ .workloadIdentityFederation.tokenExpirationSeconds }}
          path: token
      - configMap:
          name: {{ printf "%s-google-cloud" .name }}
          items:
            - key: credential-configuration.json
              path: credential-configuration.json
{{- end }}
{{- end }}

{{/*
Refuses workload credentials the component could not use: an Azure identity with only one of tenantId and clientId, or a
Google workload identity pool provider that is not a resource name. Checks only the services the component uses.
Rendered for its side effect only. Takes a dict with the dict that kmsCredentials.server renders as credentials and the
values path of the identity values, such as secretProtection, as path.
*/}}
{{- define "identity-host.kmsCredentials.validate" -}}
{{- $c := .credentials }}
{{- if $c.azureKeyVault }}
{{- if ne (empty $c.workloadIdentity.tenantId) (empty $c.workloadIdentity.clientId) }}
{{- fail (printf "%s.azureKeyVault.workloadIdentity needs both tenantId and clientId, or neither." .path) }}
{{- end }}
{{- end }}
{{- if and $c.googleCloudKms $c.workloadIdentityFederation.provider }}
{{- if not (regexMatch "^projects/[0-9]+/locations/global/workloadIdentityPools/[a-z0-9-]+/providers/[a-z0-9-]+$" $c.workloadIdentityFederation.provider) }}
{{- fail (printf "%s.googleCloudKms.workloadIdentityFederation.provider must be the resource name of a workload identity pool provider, such as projects/123456789012/locations/global/workloadIdentityPools/ncode-identity/providers/cluster." .path) }}
{{- end }}
{{- end }}
{{- end }}

{{/*
The OpenTelemetry environment of every container the chart runs: the collector's endpoint when one is set, and the
cell's identity as resource attributes.
*/}}
{{- define "identity-host.telemetryEnv" -}}
{{- with .Values.telemetry.otlpEndpoint }}
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: {{ . | quote }}
{{- end }}
- name: OTEL_RESOURCE_ATTRIBUTES
  value: {{ printf "cell.name=%s,cell.region=%s" .Values.cell.name .Values.cell.region | quote }}
{{- end }}

{{/*
The pod annotation that names where the cell's backups are kept, for the pods that own the database. Renders nothing
when backup.location is empty.
*/}}
{{- define "identity-host.backupAnnotations" -}}
{{- with .Values.backup.location }}
ncode.io/backup-location: {{ . | quote }}
{{- end }}
{{- end }}

{{/*
The environment of the host container, which the Deployment and the secret protection Job share, so a Job reads the
same configuration the pods it rewraps keys for read.
*/}}
{{- define "identity-host.hostEnv" -}}
{{- $valkey := include "identity-host.valkeyEnabled" . }}
{{- $secretProtectionRoot := "/etc/ncode-identity/secret-protection" }}
{{- $credentials := include "identity-host.kmsCredentials.server" . | fromYaml }}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.environment | quote }}
- name: AllowedHosts
  value: {{ join ";" .Values.allowedHosts | quote }}
- name: Cell__Name
  value: {{ .Values.cell.name | quote }}
- name: Cell__Region
  value: {{ .Values.cell.region | quote }}
- name: Cell__Kind
  value: {{ .Values.cell.kind | quote }}
{{- range $index, $cloudRegion := .Values.cell.cloudRegions }}
- name: Cell__CloudRegions__{{ $index }}
  value: {{ $cloudRegion | quote }}
{{- end }}
{{- with .Values.cell.managementUrl }}
- name: Cell__ManagementUrl
  value: {{ . | quote }}
{{- end }}
- name: Cell__IsControlPlane
  value: {{ .Values.cell.isControlPlane | quote }}
{{- with .Values.cell.controlPlaneIssuer }}
- name: Cell__ControlPlane__Issuer
  value: {{ . | quote }}
{{- end }}
{{- $directory := .Values.directory }}
{{- if $directory.url }}
- name: Directory__Url
  value: {{ $directory.url | quote }}
{{- with $directory.clientId }}
- name: Directory__ClientId
  value: {{ . | quote }}
{{- end }}
{{- with $directory.keyRing }}
- name: Directory__KeyRing
  value: {{ . | quote }}
{{- end }}
{{- if $directory.keys.pemSecret }}
- name: {{ printf "KeyRings__%s__PemFiles__0" $directory.keyRing }}
  value: {{ printf "/etc/ncode-identity/directory-key/%s.pem" $directory.keyRing | quote }}
{{- end }}
{{- range $kid, $key := $directory.keys.remoteKeys }}
{{- $prefix := printf "KeyRings__%s__RemoteKeys__%s" $directory.keyRing $kid }}
- name: {{ $prefix }}__Provider
  value: {{ $key.provider | quote }}
- name: {{ $prefix }}__KeyId
  value: {{ $key.keyId | quote }}
- name: {{ $prefix }}__Algorithm
  value: {{ $key.algorithm | quote }}
- name: {{ $prefix }}__SecretType
  value: {{ $key.secretType | quote }}
- name: {{ $prefix }}__KeySizeBits
  value: {{ $key.keySizeBits | quote }}
{{- with $key.curve }}
- name: {{ $prefix }}__Curve
  value: {{ . | quote }}
{{- end }}
{{- end }}
{{- end }}
{{ include "identity-host.telemetryEnv" . | trim }}
- name: OpenId__TenantResolution__StrategyCode
  value: {{ .Values.tenantResolution.strategyCode | quote }}
{{- with .Values.tenantResolution.staticSingle.issuer }}
- name: OpenId__TenantResolution__StaticSingle__Issuer
  value: {{ . | quote }}
{{- end }}
{{- range $index, $network := .Values.forwardedHeaders.knownNetworks }}
- name: ForwardedHeaders__KnownNetworks__{{ $index }}
  value: {{ $network | quote }}
{{- end }}
{{- range $index, $proxy := .Values.forwardedHeaders.knownProxies }}
- name: ForwardedHeaders__KnownProxies__{{ $index }}
  value: {{ $proxy | quote }}
{{- end }}
- name: ForwardedHeaders__ForwardLimit
  value: {{ .Values.forwardedHeaders.forwardLimit | quote }}
- name: ApiReference__DocumentEnabled
  value: {{ .Values.apiReference.documentEnabled | quote }}
- name: ApiReference__PageEnabled
  value: {{ .Values.apiReference.pageEnabled | quote }}
{{ include "identity-host.databaseEnv" (dict "scope" .Values.database "defaults" .Values.database) | trim }}
{{- if $valkey }}
{{ include "identity-host.valkeyEnv" (dict "scope" .Values.valkey "defaults" .Values.valkey) | trim }}
{{- end }}
{{- with .Values.secretProtection.rootKeyProvider }}
- name: SecretProtection__RootKeyProvider
  value: {{ . | quote }}
{{- end }}
{{- if .Values.secretProtection.local.keySecret }}
- name: SecretProtection__RootKeyProviders__Local__KeyPath
  value: {{ printf "%s/root-key/key" $secretProtectionRoot }}
{{- end }}
{{- range $index, $secret := .Values.secretProtection.local.previousKeySecrets }}
- name: SecretProtection__RootKeyProviders__Local__PreviousKeyPaths__{{ $index }}
  value: {{ printf "%s/previous-%d/key" $secretProtectionRoot $index }}
{{- end }}
{{- if eq .Values.secretProtection.rootKeyProvider "aws-kms" }}
- name: SecretProtection__RootKeyProviders__AwsKms__KeyArn
  value: {{ .Values.secretProtection.awsKms.keyArn | quote }}
# The SDK reads the region from the environment; the key's ARN names it.
- name: AWS_REGION
  value: {{ index (splitList ":" .Values.secretProtection.awsKms.keyArn) 3 | quote }}
{{- end }}
{{- include "identity-host.kmsCredentials.awsEnv" $credentials }}
{{- if eq .Values.secretProtection.rootKeyProvider "azure-key-vault" }}
- name: SecretProtection__RootKeyProviders__AzureKeyVault__KeyId
  value: {{ .Values.secretProtection.azureKeyVault.keyId | quote }}
{{- end }}
{{- include "identity-host.kmsCredentials.azureEnv" $credentials }}
{{- if eq .Values.secretProtection.rootKeyProvider "google-cloud-kms" }}
- name: SecretProtection__RootKeyProviders__GoogleCloudKms__KeyName
  value: {{ .Values.secretProtection.googleCloudKms.keyName | quote }}
{{- end }}
{{- include "identity-host.kmsCredentials.googleEnv" $credentials }}
{{- range $index, $provider := .Values.secretProtection.ownerRootKeys.providers }}
- name: SecretProtection__OwnerRootKeys__Providers__{{ $index }}
  value: {{ $provider | quote }}
{{- end }}
{{- $remoteKeyProviders := .Values.secretProtection.remoteKeyProviders }}
{{- if $remoteKeyProviders.awsKms.enabled }}
{{- with $remoteKeyProviders.awsKms.region }}
- name: SecretProtection__RemoteKeyProviders__AwsKms__Region
  value: {{ . | quote }}
{{- end }}
- name: SecretProtection__RemoteKeyProviders__AwsKms__PendingWindowInDays
  value: {{ $remoteKeyProviders.awsKms.pendingWindowInDays | quote }}
{{- end }}
{{- if $remoteKeyProviders.azureKeyVault.enabled }}
{{- with $remoteKeyProviders.azureKeyVault.vaultUri }}
- name: SecretProtection__RemoteKeyProviders__AzureKeyVault__VaultUri
  value: {{ . | quote }}
{{- end }}
- name: SecretProtection__RemoteKeyProviders__AzureKeyVault__HardwareProtected
  value: {{ $remoteKeyProviders.azureKeyVault.hardwareProtected | quote }}
{{- end }}
{{- if $remoteKeyProviders.googleCloudKms.enabled }}
{{- with $remoteKeyProviders.googleCloudKms.keyRing }}
- name: SecretProtection__RemoteKeyProviders__GoogleCloudKms__KeyRing
  value: {{ . | quote }}
{{- end }}
- name: SecretProtection__RemoteKeyProviders__GoogleCloudKms__HardwareProtected
  value: {{ $remoteKeyProviders.googleCloudKms.hardwareProtected | quote }}
{{- end }}
{{- $remoteKeys := .Values.secretProtection.remoteKeys }}
- name: SecretProtection__RemoteKeys__SweepEnabled
  value: {{ $remoteKeys.sweepEnabled | quote }}
- name: SecretProtection__RemoteKeys__SweepInterval
  value: {{ $remoteKeys.sweepInterval | quote }}
- name: SecretProtection__RemoteKeys__SweepGracePeriod
  value: {{ $remoteKeys.sweepGracePeriod | quote }}
{{- if .Values.secretProtection.recovery.publicKeyConfigMap }}
- name: SecretProtection__Recovery__PublicKeyPath
  value: {{ printf "%s/recovery/recovery-public.pem" $secretProtectionRoot }}
{{- end }}
{{- if .Values.secretProtection.recovery.disabled }}
- name: SecretProtection__Recovery__Disabled
  value: "true"
{{- end }}
{{- range $index, $grant := .Values.platform.grantTypesEnabled }}
- name: OpenId__Platform__Settings__grant_types_enabled__{{ $index }}
  value: {{ $grant | quote }}
{{- end }}
{{- with .Values.extraEnv }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
The volume mounts of the host container, which the Deployment and the host's Jobs share.
*/}}
{{- define "identity-host.hostVolumeMounts" -}}
{{- $secretProtectionRoot := "/etc/ncode-identity/secret-protection" }}
{{- $credentials := include "identity-host.kmsCredentials.server" . | fromYaml }}
# The root filesystem is read-only; the runtime's diagnostics socket and temporary files go here.
- name: tmp
  mountPath: /tmp
{{- if .Values.secretProtection.local.keySecret }}
- name: secret-protection-root-key
  mountPath: {{ printf "%s/root-key" $secretProtectionRoot }}
  readOnly: true
{{- end }}
{{- range $index, $secret := .Values.secretProtection.local.previousKeySecrets }}
- name: secret-protection-previous-{{ $index }}
  mountPath: {{ printf "%s/previous-%d" $secretProtectionRoot $index }}
  readOnly: true
{{- end }}
{{- include "identity-host.kmsCredentials.volumeMounts" $credentials }}
{{- if .Values.secretProtection.recovery.publicKeyConfigMap }}
- name: secret-protection-recovery
  mountPath: {{ printf "%s/recovery" $secretProtectionRoot }}
  readOnly: true
{{- end }}
{{- if and .Values.directory.url .Values.directory.keys.pemSecret }}
- name: directory-key
  mountPath: /etc/ncode-identity/directory-key
  readOnly: true
{{- end }}
{{- with .Values.extraVolumeMounts }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
The volumes of the host pod, which the Deployment and the host's Jobs share.
*/}}
{{- define "identity-host.hostVolumes" -}}
{{- $credentials := include "identity-host.kmsCredentials.server" . | fromYaml }}
- name: tmp
  emptyDir:
    sizeLimit: 256Mi
{{- with .Values.secretProtection.local.keySecret }}
- name: secret-protection-root-key
  secret:
    secretName: {{ . }}
    defaultMode: 0440
{{- end }}
{{- range $index, $secret := .Values.secretProtection.local.previousKeySecrets }}
- name: secret-protection-previous-{{ $index }}
  secret:
    secretName: {{ $secret }}
    defaultMode: 0440
{{- end }}
{{- include "identity-host.kmsCredentials.volumes" $credentials }}
{{- with .Values.secretProtection.recovery.publicKeyConfigMap }}
- name: secret-protection-recovery
  configMap:
    name: {{ . }}
    defaultMode: 0440
{{- end }}
{{- if and .Values.directory.url .Values.directory.keys.pemSecret }}
- name: directory-key
  secret:
    secretName: {{ .Values.directory.keys.pemSecret }}
    items:
      - key: {{ printf "%s.pem" .Values.directory.keyRing }}
        path: {{ printf "%s.pem" .Values.directory.keyRing }}
    defaultMode: 0440
{{- end }}
{{- with .Values.extraVolumes }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
The fully qualified name of the management console's resources. The release's fully qualified name is shortened first so
the name, with its suffix, fits the 63-character limit.
*/}}
{{- define "identity-host.managementConsole.fullname" -}}
{{- printf "%s-management-console" (include "identity-host.fullname" . | trunc 44 | trimSuffix "-") }}
{{- end }}

{{/*
The labels that select the management console's pods.
*/}}
{{- define "identity-host.managementConsole.selectorLabels" -}}
app.kubernetes.io/name: {{ include "identity-host.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: management-console
{{- end }}

{{/*
The labels every resource of the management console carries.
*/}}
{{- define "identity-host.managementConsole.labels" -}}
{{ include "identity-host.commonLabels" . }}
app.kubernetes.io/component: management-console
{{- end }}

{{/*
The name of the management console's ServiceAccount.
*/}}
{{- define "identity-host.managementConsole.serviceAccountName" -}}
{{- if .Values.managementConsole.serviceAccount.create }}
{{- default (include "identity-host.managementConsole.fullname" .) .Values.managementConsole.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.managementConsole.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
The management console's container image: the digest when one is set, else the tag, which defaults to the chart's
appVersion.
*/}}
{{- define "identity-host.managementConsole.image" -}}
{{- if .Values.managementConsole.image.digest }}
{{- printf "%s@%s" .Values.managementConsole.image.repository .Values.managementConsole.image.digest }}
{{- else }}
{{- printf "%s:%s" .Values.managementConsole.image.repository (default .Chart.AppVersion .Values.managementConsole.image.tag) }}
{{- end }}
{{- end }}

{{/*
The authority of the console's backend-for-frontend: managementConsole.authority, else the server's issuer when the
release resolves its tenant with StaticSingle, else empty.
*/}}
{{- define "identity-host.managementConsole.authority" -}}
{{- if .Values.managementConsole.authority }}
{{- .Values.managementConsole.authority }}
{{- else if eq .Values.tenantResolution.strategyCode "StaticSingle" }}
{{- .Values.tenantResolution.staticSingle.issuer }}
{{- end }}
{{- end }}

{{/*
The URL the console calls the server's management API through: /api on the origin of the console's authority, the URL
the server compares the console's DPoP proofs with.
*/}}
{{- define "identity-host.managementConsole.managementApiUrl" -}}
{{- printf "%s/api" (regexFind "^https://[^/]+" (include "identity-host.managementConsole.authority" .)) }}
{{- end }}

{{/*
Whether the management console shares its single-use state through its own Valkey.
*/}}
{{- define "identity-host.managementConsole.valkeyEnabled" -}}
{{- if or .Values.managementConsole.valkey.existingSecret .Values.managementConsole.valkey.parts.existingSecret }}true{{ end }}
{{- end }}

{{/*
The identifier of the first of the console's remote keys that the named key management service holds, the client key
ring before the DPoP key ring and key ids in sorted order; empty when no console remote key names the service. Takes a
dict with the chart context as root and the provider name (aws-kms, azure-key-vault or google-cloud-kms) as name.
*/}}
{{- define "identity-host.managementConsole.remoteKeyId" -}}
{{- $found := "" }}
{{- range $ring := list "client" "dpop" }}
{{- range $kid, $key := (index $.root.Values.managementConsole.keyRings $ring).remoteKeys }}
{{- if and (not $found) (eq $key.provider $.name) }}
{{- $found = $key.keyId }}
{{- end }}
{{- end }}
{{- end }}
{{- $found }}
{{- end }}

{{/*
Whether a remote key of the management console names the key management service, so that the console's pods carry that
service's workload credentials. Takes a dict with the chart context as root and the provider name as name; renders
"true" or nothing.
*/}}
{{- define "identity-host.managementConsole.usesKeyManagementService" -}}
{{- if include "identity-host.managementConsole.remoteKeyId" . }}true{{ end }}
{{- end }}

{{/*
The workload credentials of the management console, as YAML in the shape that kmsCredentials.server renders: the
console's full name, whether a remote key of the console names each service, and the console's own identity values from
managementConsole.secretProtection. Takes the chart context.
*/}}
{{- define "identity-host.kmsCredentials.managementConsole" -}}
name: {{ include "identity-host.managementConsole.fullname" . | quote }}
awsKms: {{ include "identity-host.managementConsole.usesKeyManagementService" (dict "root" . "name" "aws-kms") | quote }}
azureKeyVault: {{ include "identity-host.managementConsole.usesKeyManagementService" (dict "root" . "name" "azure-key-vault") | quote }}
googleCloudKms: {{ include "identity-host.managementConsole.usesKeyManagementService" (dict "root" . "name" "google-cloud-kms") | quote }}
webIdentity:
  {{- toYaml .Values.managementConsole.secretProtection.awsKms.webIdentity | nindent 2 }}
workloadIdentity:
  {{- toYaml .Values.managementConsole.secretProtection.azureKeyVault.workloadIdentity | nindent 2 }}
workloadIdentityFederation:
  {{- toYaml .Values.managementConsole.secretProtection.googleCloudKms.workloadIdentityFederation | nindent 2 }}
{{- end }}

{{/*
Refuses a management console the host would refuse, or one that would run replicas without shared state, before anything
is applied. Rendered for its side effect only.
*/}}
{{- define "identity-host.managementConsole.validate" -}}
{{- $console := .Values.managementConsole }}
{{- if not $console.host }}
{{- fail "managementConsole.host must be the console's public host name, such as manage.example.com." }}
{{- end }}
{{- if contains "*" $console.host }}
{{- fail "managementConsole.host must be a concrete host name, not a wildcard." }}
{{- end }}
{{- $authority := include "identity-host.managementConsole.authority" . }}
{{- if not (hasPrefix "https://" $authority) }}
{{- fail "managementConsole.authority must be the issuer's https URL; it defaults to tenantResolution.staticSingle.issuer when tenantResolution.strategyCode is StaticSingle." }}
{{- end }}
{{- range $ring := list "client" "dpop" }}
{{- $keyRing := index $console.keyRings $ring }}
{{- if and (not $keyRing.pemSecret) (not $keyRing.remoteKeys) }}
{{- fail (printf "managementConsole.keyRings.%s must name pemSecret or list a remote key: the console's mgmt-%s key ring needs at least one key." $ring $ring) }}
{{- end }}
{{- $ids := list }}
{{- if $keyRing.pemSecret }}
{{- $ids = append $ids (printf "mgmt-%s" $ring) }}
{{- end }}
{{- range $kid, $key := $keyRing.remoteKeys }}
{{- $ids = append $ids (lower $kid) }}
{{- $path := printf "managementConsole.keyRings.%s.remoteKeys.%s.keyId" $ring $kid }}
{{- include "identity-host.remoteKeyIdValidate" (dict "path" $path "provider" $key.provider "keyId" $key.keyId "example" (dict "keyName" "mgmt-client" "keyRing" "ncode-identity-console")) }}
{{- end }}
{{- if ne (len $ids) (len (uniq $ids)) }}
{{- fail (printf "managementConsole.keyRings.%s holds two keys with the same id: each remote key's id differs from every other key's in the ring, compared without case, and from mgmt-%s when pemSecret is set." $ring $ring) }}
{{- end }}
{{- end }}
{{- if and (gt (int $console.replicaCount) 1) (not (include "identity-host.managementConsole.valkeyEnabled" .)) }}
{{- fail "managementConsole.replicaCount above 1 requires managementConsole.valkey.existingSecret or managementConsole.valkey.parts.existingSecret: replicas share their sessions through Valkey." }}
{{- end }}
{{- include "identity-host.valkeyValidate" (dict "path" "managementConsole.valkey" "scope" $console.valkey "defaults" .Values.valkey) }}
{{- include "identity-host.kmsCredentials.validate" (dict "credentials" (include "identity-host.kmsCredentials.managementConsole" . | fromYaml) "path" "managementConsole.secretProtection") }}
{{- end }}

{{/*
The environment of the management console container.
*/}}
{{- define "identity-host.managementConsole.env" -}}
{{- $console := .Values.managementConsole }}
{{- $keyRoot := "/etc/ncode-identity/management-console" }}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.environment | quote }}
- name: AllowedHosts
  value: {{ $console.host | quote }}
{{- range $index, $network := .Values.forwardedHeaders.knownNetworks }}
- name: ForwardedHeaders__KnownNetworks__{{ $index }}
  value: {{ $network | quote }}
{{- end }}
{{- range $index, $proxy := .Values.forwardedHeaders.knownProxies }}
- name: ForwardedHeaders__KnownProxies__{{ $index }}
  value: {{ $proxy | quote }}
{{- end }}
- name: ForwardedHeaders__ForwardLimit
  value: {{ .Values.forwardedHeaders.forwardLimit | quote }}
- name: Bff__Frontends__mgmt__Authority
  value: {{ include "identity-host.managementConsole.authority" . | quote }}
- name: Bff__Frontends__mgmt__PublicOrigin
  value: {{ printf "https://%s" $console.host | quote }}
{{- if $console.keyRings.client.pemSecret }}
- name: KeyRings__mgmt-client__PemFiles__0
  value: {{ printf "%s/client-key/mgmt-client.pem" $keyRoot | quote }}
{{- end }}
{{- if $console.keyRings.dpop.pemSecret }}
- name: KeyRings__mgmt-dpop__PemFiles__0
  value: {{ printf "%s/dpop-key/mgmt-dpop.pem" $keyRoot | quote }}
{{- end }}
{{- range $ring := list "client" "dpop" }}
{{- range $kid, $key := (index $console.keyRings $ring).remoteKeys }}
{{- $prefix := printf "KeyRings__mgmt-%s__RemoteKeys__%s" $ring $kid }}
- name: {{ $prefix }}__Provider
  value: {{ $key.provider | quote }}
- name: {{ $prefix }}__KeyId
  value: {{ $key.keyId | quote }}
- name: {{ $prefix }}__Algorithm
  value: {{ $key.algorithm | quote }}
- name: {{ $prefix }}__SecretType
  value: {{ $key.secretType | quote }}
- name: {{ $prefix }}__KeySizeBits
  value: {{ $key.keySizeBits | quote }}
{{- with $key.curve }}
- name: {{ $prefix }}__Curve
  value: {{ . | quote }}
{{- end }}
{{- end }}
{{- end }}
{{- $credentials := include "identity-host.kmsCredentials.managementConsole" $ | fromYaml }}
{{- include "identity-host.kmsCredentials.awsEnv" $credentials }}
{{- include "identity-host.kmsCredentials.azureEnv" $credentials }}
{{- include "identity-host.kmsCredentials.googleEnv" $credentials }}
- name: ManagementConsole__ManagementApiUrl
  value: {{ include "identity-host.managementConsole.managementApiUrl" . | quote }}
{{- if include "identity-host.managementConsole.valkeyEnabled" . }}
{{ include "identity-host.valkeyEnv" (dict "scope" $console.valkey "defaults" .Values.valkey) | trim }}
{{- end }}
{{- with $console.extraEnv }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
The fully qualified name of the directory's resources. The release's fully qualified name is shortened first so the name,
with its suffix, fits the 63-character limit.
*/}}
{{- define "identity-host.directory.fullname" -}}
{{- printf "%s-directory" (include "identity-host.fullname" . | trunc 54 | trimSuffix "-") }}
{{- end }}

{{/*
The name of the directory's migration Job. The fully qualified name is shortened first so the name, with its suffix, fits
the 63-character label value Kubernetes copies it into (batch.kubernetes.io/job-name).
*/}}
{{- define "identity-host.directory.migrationsJobName" -}}
{{- printf "%s-directory-migrations" (include "identity-host.fullname" . | trunc 40 | trimSuffix "-") }}
{{- end }}

{{/*
The labels that select the directory's pods. The component keeps the migration Job's pod out of the Service.
*/}}
{{- define "identity-host.directory.selectorLabels" -}}
app.kubernetes.io/name: {{ include "identity-host.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: directory
{{- end }}

{{/*
The labels every resource of the directory carries.
*/}}
{{- define "identity-host.directory.labels" -}}
{{ include "identity-host.commonLabels" . }}
app.kubernetes.io/component: directory
{{- end }}

{{/*
The name of the directory's ServiceAccount.
*/}}
{{- define "identity-host.directory.serviceAccountName" -}}
{{- if .Values.directory.serviceAccount.create }}
{{- default (include "identity-host.directory.fullname" .) .Values.directory.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.directory.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
The directory's container image: the digest when one is set, else the tag, which defaults to the chart's appVersion.
*/}}
{{- define "identity-host.directory.image" -}}
{{- if .Values.directory.image.digest }}
{{- printf "%s@%s" .Values.directory.image.repository .Values.directory.image.digest }}
{{- else }}
{{- printf "%s:%s" .Values.directory.image.repository (default .Chart.AppVersion .Values.directory.image.tag) }}
{{- end }}
{{- end }}

{{/*
The environment of the directory container, which the Deployment and the migration Job share, so the Job reads the same
configuration the pods it migrates the database for read.
*/}}
{{- define "identity-host.directory.env" -}}
{{- $directory := .Values.directory }}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.environment | quote }}
- name: AllowedHosts
  value: {{ join ";" $directory.allowedHosts | quote }}
- name: DirectoryServer__Issuer
  value: {{ $directory.issuer | quote }}
- name: DirectoryServer__ControlPlaneCell
  value: {{ $directory.controlPlaneCell | quote }}
- name: ConnectionStrings__directory
  valueFrom:
    secretKeyRef:
      name: {{ $directory.database.existingSecret }}
      key: {{ $directory.database.connectionStringKey | quote }}
{{ include "identity-host.telemetryEnv" . | trim }}
{{- with $directory.extraEnv }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
Refuses a directory the host would refuse, before anything is applied. Rendered for its side effect only.
*/}}
{{- define "identity-host.directory.validate" -}}
{{- $directory := .Values.directory }}
{{- if not $directory.allowedHosts }}
{{- fail "directory.allowedHosts must list the host names the directory answers to when directory.enabled is true." }}
{{- end }}
{{- if has "*" $directory.allowedHosts }}
{{- fail "directory.allowedHosts must not contain '*': it accepts every Host header." }}
{{- end }}
{{- if contains "*" (first $directory.allowedHosts) }}
{{- fail "directory.allowedHosts must start with a concrete host that the probes can send: the first host is a wildcard." }}
{{- end }}
{{- if not $directory.issuer }}
{{- fail "directory.issuer must be the issuer of the control-plane cell when directory.enabled is true: the directory accepts bearer tokens from it." }}
{{- end }}
{{- if not $directory.controlPlaneCell }}
{{- fail "directory.controlPlaneCell must name the control-plane cell when directory.enabled is true: the directory takes platform and team writes only from it." }}
{{- end }}
{{- if not $directory.database.existingSecret }}
{{- fail "directory.database.existingSecret must name the Secret holding the directory's connection string when directory.enabled is true." }}
{{- end }}
{{- if not $directory.database.connectionStringKey }}
{{- fail "directory.database.connectionStringKey must name the key that holds the connection string in directory.database.existingSecret." }}
{{- end }}
{{- if not $directory.database.runtimeRole }}
{{- fail "directory.database.runtimeRole must name the Postgres role the directory connects as when directory.enabled is true: the migration Job grants it access to the tables." }}
{{- end }}
{{- if not (regexMatch "^[a-z_][a-z0-9_]*$" $directory.database.runtimeRole) }}
{{- fail "directory.database.runtimeRole must be a lower-case Postgres role name of letters, digits and underscores that does not start with a digit." }}
{{- end }}
{{- if not $directory.migrations.database.existingSecret }}
{{- fail "directory.migrations.database.existingSecret must name the Secret holding the connection string of the role that owns the directory's schema when directory.enabled is true." }}
{{- end }}
{{- if not $directory.migrations.database.connectionStringKey }}
{{- fail "directory.migrations.database.connectionStringKey must name the key that holds the connection string in directory.migrations.database.existingSecret." }}
{{- end }}
{{- if not $directory.tls.existingSecret }}
{{- fail "directory.tls.existingSecret must name the Secret holding the directory's serving certificate when directory.enabled is true: the directory serves HTTPS only." }}
{{- end }}
{{- end }}
