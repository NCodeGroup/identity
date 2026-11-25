{{/*
The chart name, overridable by nameOverride.
*/}}
{{- define "identity-directory.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
The fully qualified name of the release's resources.
*/}}
{{- define "identity-directory.fullname" -}}
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

{{- define "identity-directory.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
The name of the migration Job. The fully qualified name is shortened first so the name, with its suffix, fits the
63-character label value Kubernetes copies it into (batch.kubernetes.io/job-name).
*/}}
{{- define "identity-directory.migrationsJobName" -}}
{{- printf "%s-migrations" (include "identity-directory.fullname" . | trunc 52 | trimSuffix "-") }}
{{- end }}

{{/*
The labels every resource of the release carries.
*/}}
{{- define "identity-directory.commonLabels" -}}
helm.sh/chart: {{ include "identity-directory.chart" . }}
app.kubernetes.io/name: {{ include "identity-directory.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
The labels that select the directory's pods. The component keeps the migration Job's pod out of the Service.
*/}}
{{- define "identity-directory.selectorLabels" -}}
app.kubernetes.io/name: {{ include "identity-directory.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: directory
{{- end }}

{{/*
The labels every resource of the directory carries.
*/}}
{{- define "identity-directory.labels" -}}
{{ include "identity-directory.commonLabels" . }}
app.kubernetes.io/component: directory
{{- end }}

{{/*
The name of the directory's ServiceAccount.
*/}}
{{- define "identity-directory.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "identity-directory.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
The directory's container image: the digest when one is set, else the tag, which defaults to the chart's appVersion.
*/}}
{{- define "identity-directory.image" -}}
{{- if .Values.image.digest }}
{{- printf "%s@%s" .Values.image.repository .Values.image.digest }}
{{- else }}
{{- printf "%s:%s" .Values.image.repository (default .Chart.AppVersion .Values.image.tag) }}
{{- end }}
{{- end }}

{{/*
The pattern of the ARN of an AWS KMS key or alias.
*/}}
{{- define "identity-directory.awsKmsArnPattern" -}}
^arn:aws[a-z-]*:kms:[a-z0-9-]+:[0-9]{12}:(key/[A-Za-z0-9-]+|alias/[A-Za-z0-9/_-]+)$
{{- end }}

{{/*
The pattern of a service URL: https, or http on a loopback host, written with its authority and with no user
information, query, fragment or white space. The directory applies the rule itself when it starts; the pattern is the
chart's check before anything is applied.
*/}}
{{- define "identity-directory.serviceUrlPattern" -}}
(?i)^(https://[^/?#@\s\v\x{85}\p{Z}]+|http://(localhost|127(\.[0-9]{1,3}){3}|\[::1\])(:[0-9]+)?)(/[^?#\s\v\x{85}\p{Z}]*)?$
{{- end }}

{{/*
The pattern of a service URL that must be https: written with its authority and with no user information, query,
fragment or white space. The directory applies the rule itself when it starts; the pattern is the chart's check before
anything is applied.
*/}}
{{- define "identity-directory.httpsServiceUrlPattern" -}}
(?i)^https://[^/?#@\s\v\x{85}\p{Z}]+(/[^?#\s\v\x{85}\p{Z}]*)?$
{{- end }}

{{/*
Refuses a service URL that is set and is not a valid one. Takes the path of the value (path), the value (value) and
whether it must be https (httpsOnly), which otherwise also allows http on a loopback host. Rendered for its side effect
only.
*/}}
{{- define "identity-directory.serviceUrlValidate" -}}
{{- if .value }}
{{- if .httpsOnly }}
{{- if not (regexMatch (include "identity-directory.httpsServiceUrlPattern" .) .value) }}
{{- fail (printf "%s must be an absolute https URL with no user information, query or fragment." .path) }}
{{- end }}
{{- else if not (regexMatch (include "identity-directory.serviceUrlPattern" .) .value) }}
{{- fail (printf "%s must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment." .path) }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Refuses a remote key id that does not name a key version the provider can sign with. Takes the path of the value, the
provider, the key id and an example of the key's name (keyName) and Google Cloud KMS key ring (keyRing). Rendered for its
side effect only.
*/}}
{{- define "identity-directory.remoteKeyIdValidate" -}}
{{- if eq .provider "aws-kms" }}
{{- if not (regexMatch (include "identity-directory.awsKmsArnPattern" .) .keyId) }}
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
The database environment entries of a container: the parts of a Postgres operator's Secret, each from its own key, or
the finished connection string. The scope holds the settings of one connection, and its empty keys fall back to the
defaults, which are the values of the runtime role's database. The entries are named for the connection string
(connectionStringName, openiddb when empty) and the configuration section that holds the parts (sectionName,
OpenIdDatabase when empty); the directory reads directory and DirectoryDatabase.
*/}}
{{- define "identity-directory.databaseEnv" -}}
{{- $scope := .scope }}
{{- $defaults := .defaults }}
{{- $connectionStringName := .connectionStringName | default "openiddb" }}
{{- $sectionName := .sectionName | default "OpenIdDatabase" }}
{{- if $scope.parts.existingSecret }}
{{- range $part := list (list "Host" "hostKey") (list "Port" "portKey") (list "Name" "databaseKey") (list "Username" "usernameKey") (list "Password" "passwordKey") }}
{{- $key := (index $scope.parts (last $part)) | default (index $defaults.parts (last $part)) }}
{{- if $key }}
- name: {{ $sectionName }}__{{ first $part }}
  valueFrom:
    secretKeyRef:
      name: {{ $scope.parts.existingSecret }}
      key: {{ $key | quote }}
{{- end }}
{{- end }}
{{- else }}
- name: ConnectionStrings__{{ $connectionStringName }}
  valueFrom:
    secretKeyRef:
      name: {{ $scope.existingSecret }}
      key: {{ $scope.connectionStringKey | default $defaults.connectionStringKey | quote }}
{{- end }}
{{- end }}

{{/*
Refuses a database connection that names both of its forms, or none when it is required, a finished connection string
without its key, or a parts Secret whose required key is empty. The message of a missing connection names it by the
connection string's name (connectionStringName, openiddb when empty). Rendered for its side effect only.
*/}}
{{- define "identity-directory.databaseValidate" -}}
{{- $scope := .scope }}
{{- $connectionStringName := .connectionStringName | default "openiddb" }}
{{- if and $scope.existingSecret $scope.parts.existingSecret }}
{{- fail (printf "%s.existingSecret and %s.parts.existingSecret are both set: name the Secret that holds the finished connection string or the Secret that holds its parts, not both." .path .path) }}
{{- end }}
{{- if and .required (not $scope.existingSecret) (not $scope.parts.existingSecret) }}
{{- fail (printf "%s.existingSecret must name the Secret holding the %s connection string, or %s.parts.existingSecret the Secret holding its host, database, user and password." .path $connectionStringName .path) }}
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
The pod security context of every pod the chart runs: the restricted Pod Security Standard as the image's non-root
'app' user, whose group can read the mounted secrets.
*/}}
{{- define "identity-directory.podSecurityContext" -}}
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
{{- define "identity-directory.containerSecurityContext" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop:
    - ALL
{{- end }}

{{/*
The image pull secrets and scheduling constraints of every pod the chart runs, as pod spec fields.
*/}}
{{- define "identity-directory.imagePullSecrets" -}}
{{- with .Values.imagePullSecrets }}
imagePullSecrets:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- end }}

{{- define "identity-directory.scheduling" -}}
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
The process namespace of every pod the chart runs. The pod's containers share one process namespace, so the pod's pause
process is PID 1 and reaps, and the .NET process is not. The .NET runtime's crash path ends a process with SIGABRT, which
the kernel never delivers to a namespace's PID 1 when it has no handler, so a .NET process that is PID 1 hangs after an
unhandled exception or a fail-fast instead of exiting (dotnet/runtime#112580). Each pod runs one container as the
non-root user with every capability dropped, so no other container sees its processes.
*/}}
{{- define "identity-directory.processNamespace" -}}
shareProcessNamespace: true
{{- end }}

{{/*
The external_account credential configuration that Google Application Default Credentials read: it exchanges the
projected service-account token with Google STS, and impersonates a service account when one is named. Empty unless the
component uses Google Cloud KMS through workload identity federation. The token path must match the volume that
kmsCredentials.volumes projects. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-directory.kmsCredentials.googleCloudCredentialConfiguration" -}}
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
{{- define "identity-directory.kmsCredentials.awsEnv" -}}
{{- $awsWebIdentityRoot := "/var/run/secrets/tokens/aws" }}
{{- /*
A pod whose key ring names no AWS KMS key never carries AWS credentials, whatever the web identity values say.
*/}}
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
{{- define "identity-directory.kmsCredentials.azureEnv" -}}
{{- $azureWorkloadIdentityRoot := "/var/run/secrets/tokens/azure" }}
{{- /*
A pod whose key ring names no Azure Key Vault key never carries Azure credentials, whatever the workload identity
values say.
*/}}
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
{{- define "identity-directory.kmsCredentials.googleEnv" -}}
{{- $googleCloudTokenRoot := "/var/run/secrets/tokens/google" }}
{{- if include "identity-directory.kmsCredentials.googleCloudCredentialConfiguration" . }}
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
{{- define "identity-directory.kmsCredentials.volumeMounts" -}}
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
{{- if include "identity-directory.kmsCredentials.googleCloudCredentialConfiguration" . }}
- name: google-cloud-credentials
  mountPath: {{ $googleCloudTokenRoot }}
  readOnly: true
{{- end }}
{{- end }}

{{/*
The volumes of the AWS, Azure and Google workload credentials, each present only when the component uses that service
with an identity. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-directory.kmsCredentials.volumes" -}}
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
{{- if include "identity-directory.kmsCredentials.googleCloudCredentialConfiguration" . }}
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
Refuses workload credentials the directory could not use: an Azure identity with only one of tenantId and clientId, or a
Google workload identity pool provider that is not a resource name. Checks only the services the directory uses.
Rendered for its side effect only. Takes the dict that kmsCredentials.server renders.
*/}}
{{- define "identity-directory.kmsCredentials.validate" -}}
{{- if .azureKeyVault }}
{{- if ne (empty .workloadIdentity.tenantId) (empty .workloadIdentity.clientId) }}
{{- fail "azure.workloadIdentity needs both tenantId and clientId, or neither." }}
{{- end }}
{{- end }}
{{- if and .googleCloudKms .workloadIdentityFederation.provider }}
{{- if not (regexMatch "^projects/[0-9]+/locations/global/workloadIdentityPools/[a-z0-9-]+/providers/[a-z0-9-]+$" .workloadIdentityFederation.provider) }}
{{- fail "google.workloadIdentityFederation.provider must be the resource name of a workload identity pool provider, such as projects/123456789012/locations/global/workloadIdentityPools/ncode-identity/providers/cluster." }}
{{- end }}
{{- end }}
{{- end }}

{{/*
The pod annotation that names where the directory's backups are kept, for the pods that own a database: the directory
and its migration Job. Renders nothing when backup.location is empty.
*/}}
{{- define "identity-directory.backupAnnotations" -}}
{{- with .Values.backup.location }}
ncode.io/backup-location: {{ . | quote }}
{{- end }}
{{- end }}

{{/*
Whether a remote key of the directory's key ring names the key management service, so that the directory's pods carry
that service's workload credentials. Takes a dict with the chart context as root and the provider name as name; renders
"true" or nothing.
*/}}
{{- define "identity-directory.usesKeyManagementService" -}}
{{- range $kid, $key := .root.Values.keys.remoteKeys }}
{{- if eq $key.provider $.name }}true{{ end }}
{{- end }}
{{- end }}

{{/*
The workload credentials of the directory, as YAML that the kmsCredentials helpers take as a dict (after fromYaml): name
is the directory's full name, awsKms, azureKeyVault and googleCloudKms are "true" or empty by whether a remote key names
that service, and webIdentity, workloadIdentity and workloadIdentityFederation are the identity values of aws, azure and
google. Takes the chart context.
*/}}
{{- define "identity-directory.kmsCredentials.server" -}}
name: {{ include "identity-directory.fullname" . | quote }}
awsKms: {{ include "identity-directory.usesKeyManagementService" (dict "root" . "name" "aws-kms") | trim | trunc 4 | quote }}
azureKeyVault: {{ include "identity-directory.usesKeyManagementService" (dict "root" . "name" "azure-key-vault") | trim | trunc 4 | quote }}
googleCloudKms: {{ include "identity-directory.usesKeyManagementService" (dict "root" . "name" "google-cloud-kms") | trim | trunc 4 | quote }}
webIdentity:
  {{- toYaml .Values.aws.webIdentity | nindent 2 }}
workloadIdentity:
  {{- toYaml .Values.azure.workloadIdentity | nindent 2 }}
workloadIdentityFederation:
  {{- toYaml .Values.google.workloadIdentityFederation | nindent 2 }}
{{- end }}

{{/*
The OpenTelemetry environment of every container the chart runs: the collector's endpoint when one is set.
*/}}
{{- define "identity-directory.telemetryEnv" -}}
{{- with .Values.telemetry.otlpEndpoint }}
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: {{ . | quote }}
{{- end }}
{{- end }}

{{/*
The volume mount of the key ring's PEM key at /etc/ncode-identity/key-ring, rendered when keys.pemSecret is set.
*/}}
{{- define "identity-directory.keyRingVolumeMounts" -}}
{{- if .Values.keys.pemSecret }}
- name: key-ring
  mountPath: /etc/ncode-identity/key-ring
  readOnly: true
{{- end }}
{{- end }}

{{/*
The volume of the key ring's PEM key.
*/}}
{{- define "identity-directory.keyRingVolumes" -}}
{{- if .Values.keys.pemSecret }}
- name: key-ring
  secret:
    secretName: {{ .Values.keys.pemSecret }}
    items:
      - key: {{ printf "%s.pem" .Values.keyRing }}
        path: {{ printf "%s.pem" .Values.keyRing }}
    defaultMode: 0440
{{- end }}
{{- end }}

{{/*
The environment of the directory's Deployment container, which holds the runtime role's connection: the finished
connection string (ConnectionStrings:directory) or the parts of a Postgres operator's Secret (DirectoryDatabase). The
migration Job sets its own environment and never receives that connection.
*/}}
{{- define "identity-directory.env" -}}
{{- $controlPlane := .Values.controlPlane }}
{{- $credentials := include "identity-directory.kmsCredentials.server" . | fromYaml }}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.environment | quote }}
- name: AllowedHosts
  value: {{ join ";" .Values.allowedHosts | quote }}
- name: DirectoryServer__Issuer
  value: {{ .Values.issuer | quote }}
- name: DirectoryServer__KeyRing
  value: {{ .Values.keyRing | quote }}
- name: DirectoryServer__TokenLifetime
  value: {{ .Values.tokenLifetime | quote }}
- name: DirectoryServer__ControlPlane__Cell
  value: {{ $controlPlane.cell | quote }}
- name: DirectoryServer__ControlPlane__Region
  value: {{ $controlPlane.region | quote }}
- name: DirectoryServer__ControlPlane__Kind
  value: {{ $controlPlane.kind | quote }}
- name: DirectoryServer__ControlPlane__TenantId
  value: {{ $controlPlane.tenantId | quote }}
- name: DirectoryServer__ControlPlane__Issuer
  value: {{ $controlPlane.issuer | quote }}
- name: DirectoryServer__ControlPlane__ClientJwks
  value: {{ $controlPlane.clientJwks | quote }}
{{- if .Values.keys.pemSecret }}
- name: {{ printf "KeyRings__%s__PemFiles__0" .Values.keyRing }}
  value: {{ printf "/etc/ncode-identity/key-ring/%s.pem" .Values.keyRing | quote }}
{{- end }}
{{- range $kid, $key := .Values.keys.remoteKeys }}
{{- $prefix := printf "KeyRings__%s__RemoteKeys__%s" $.Values.keyRing $kid }}
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
{{ include "identity-directory.backendTls.env" . | trim }}
{{ include "identity-directory.databaseEnv" (dict "scope" .Values.database "defaults" .Values.database "connectionStringName" "directory" "sectionName" "DirectoryDatabase") | trim }}
{{ include "identity-directory.kmsCredentials.awsEnv" $credentials | trim }}
{{ include "identity-directory.kmsCredentials.azureEnv" $credentials | trim }}
{{ include "identity-directory.kmsCredentials.googleEnv" $credentials | trim }}
{{ include "identity-directory.telemetryEnv" . | trim }}
{{- with .Values.extraEnv }}
{{- toYaml . | nindent 0 }}
{{- end }}
{{- end }}

{{/*
Refuses a directory the host would refuse, before anything is applied. Rendered for its side effect only.
*/}}
{{- define "identity-directory.validate" -}}
{{- if has "*" .Values.allowedHosts }}
{{- fail "allowedHosts must not contain '*': it accepts every Host header." }}
{{- end }}
{{- if contains "*" (first .Values.allowedHosts) }}
{{- fail "allowedHosts must start with a concrete host that the probes can send: the first host is a wildcard." }}
{{- end }}
{{- include "identity-directory.serviceUrlValidate" (dict "path" "issuer" "value" .Values.issuer "httpsOnly" false) }}
{{- include "identity-directory.serviceUrlValidate" (dict "path" "controlPlane.issuer" "value" .Values.controlPlane.issuer "httpsOnly" false) }}
{{- $clientJwks := .Values.controlPlane.clientJwks | fromJson }}
{{- if not (hasKey $clientJwks "keys") }}
{{- fail "controlPlane.clientJwks must be the JSON Web Key Set, as JSON text, of the control-plane cell's client: an object with a keys array." }}
{{- end }}
{{- if not (regexMatch "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$" .Values.controlPlane.tenantId) }}
{{- fail "controlPlane.tenantId must be 1 to 63 lowercase letters, digits or hyphens that start and end with a letter or digit." }}
{{- end }}
{{- /* tokenLifetime is a .NET TimeSpan [d.]hh:mm[:ss[.f]]; the server accepts 00:01:00 to 01:00:00. */}}
{{- $lifetime := .Values.tokenLifetime }}
{{- $days := 0 }}
{{- if regexMatch "^[0-9]+[.]" $lifetime }}
{{- $days = atoi (regexFind "^[0-9]+" $lifetime) }}
{{- $lifetime = regexReplaceAll "^[0-9]+[.]" $lifetime "" }}
{{- end }}
{{- $fraction := regexFind "[.][0-9]+$" $lifetime }}
{{- $lifetime = regexReplaceAll "[.][0-9]+$" $lifetime "" }}
{{- $fields := splitList ":" $lifetime }}
{{- $lifetimeSeconds := add (mul $days 86400) (mul (atoi (index $fields 0)) 3600) (mul (atoi (index $fields 1)) 60) }}
{{- if gt (len $fields) 2 }}
{{- $lifetimeSeconds = add $lifetimeSeconds (atoi (index $fields 2)) }}
{{- end }}
{{- if or (lt $lifetimeSeconds 60) (gt $lifetimeSeconds 3600) (and (eq $lifetimeSeconds 3600) (regexMatch "[1-9]" $fraction)) }}
{{- fail (printf "tokenLifetime must be between 00:01:00 and 01:00:00: %s is outside the range the directory accepts." .Values.tokenLifetime) }}
{{- end }}
{{- $keys := .Values.keys }}
{{- if and (not $keys.pemSecret) (not $keys.remoteKeys) }}
{{- fail (printf "keys must name pemSecret or list a remote key: the directory's key ring %s needs at least one key." .Values.keyRing) }}
{{- end }}
{{- $keyIds := list }}
{{- if $keys.pemSecret }}
{{- $keyIds = append $keyIds (lower .Values.keyRing) }}
{{- end }}
{{- range $kid, $key := $keys.remoteKeys }}
{{- $keyIds = append $keyIds (lower $kid) }}
{{- include "identity-directory.remoteKeyIdValidate" (dict "path" (printf "keys.remoteKeys.%s.keyId" $kid) "provider" $key.provider "keyId" $key.keyId "example" (dict "keyName" "directory-signing" "keyRing" "ncode-identity-directory")) }}
{{- end }}
{{- if ne (len $keyIds) (len (uniq $keyIds)) }}
{{- fail (printf "keys holds two keys with the same id: each remote key's id differs from every other key's in the ring, compared without case, and from %s when pemSecret is set." .Values.keyRing) }}
{{- end }}
{{- include "identity-directory.kmsCredentials.validate" (include "identity-directory.kmsCredentials.server" . | fromYaml) }}
{{- include "identity-directory.databaseValidate" (dict "path" "database" "scope" .Values.database "defaults" .Values.database "required" true "connectionStringName" "directory") }}
{{- if not .Values.database.runtimeRole }}
{{- fail "database.runtimeRole must name the Postgres role the directory connects as: the migration Job grants it access to the tables." }}
{{- end }}
{{- if not (regexMatch "^[a-z_][a-z0-9_]*$" .Values.database.runtimeRole) }}
{{- fail "database.runtimeRole must be a lower-case Postgres role name of letters, digits and underscores that does not start with a digit." }}
{{- end }}
{{- include "identity-directory.databaseValidate" (dict "path" "migrations.database" "scope" .Values.migrations.database "defaults" .Values.database "required" true "connectionStringName" "directory") }}
{{- include "identity-directory.backendTls.validate" . }}
{{- end }}
