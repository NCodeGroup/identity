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
