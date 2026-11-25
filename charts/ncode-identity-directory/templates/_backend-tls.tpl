{{/*
The name of the Secret that holds the directory pods' serving certificate: backendTls.existingSecret, or else the
Secret the chart's Certificate writes.
*/}}
{{- define "identity-directory.backendTls.secretName" -}}
{{- .Values.backendTls.existingSecret | default (printf "%s-tls" (include "identity-directory.fullname" .)) }}
{{- end }}

{{/*
The name of the Secret that the chart's ingress Certificate writes: the ingress controller's own client certificate and
the CA that signed the pods' certificates.
*/}}
{{- define "identity-directory.backendTls.ingressSecretName" -}}
{{- printf "%s-ingress" (include "identity-directory.fullname" .) }}
{{- end }}

{{/*
The name of the ingress controller's Secret: its ca.crt verifies the pods' certificate and its tls.crt and tls.key are
the controller's client certificate. It is backendTls.ingressSecret, else the chart's ingress Secret. The pods' serving
Secret is never a fallback, so the controller never reads the pods' private key.
*/}}
{{- define "identity-directory.backendTls.verificationSecretName" -}}
{{- .Values.backendTls.ingressSecret | default (include "identity-directory.backendTls.ingressSecretName" .) }}
{{- end }}

{{/*
Prints true when the directory's route uses the ingress controller's Secret, and nothing otherwise: a Traefik Ingress
verifies the pods against it, and any route presents its client certificate when backendTls.clientCertificate.required
is true.
*/}}
{{- define "identity-directory.backendTls.controllerSecretUsed" -}}
{{- $route := or .Values.ingress.enabled .Values.httpRoute.enabled }}
{{- if or (and .Values.ingress.enabled (eq .Values.ingress.controller "traefik")) (and $route .Values.backendTls.clientCertificate.required) }}
{{- print "true" }}
{{- end }}
{{- end }}

{{/*
Prints true when the chart issues the ingress Secret: the route uses the controller's Secret and no ingressSecret of the
operator's names it. Prints nothing otherwise.
*/}}
{{- define "identity-directory.backendTls.ingressCertificateNeeded" -}}
{{- if and (include "identity-directory.backendTls.controllerSecretUsed" .) (not .Values.backendTls.ingressSecret) }}
{{- print "true" }}
{{- end }}
{{- end }}

{{/*
The DNS name an ingress controller verifies the pods' certificate against.
*/}}
{{- define "identity-directory.backendTls.serverName" -}}
{{- printf "%s.%s.svc" (include "identity-directory.fullname" .) .Release.Namespace }}
{{- end }}

{{/*
The DNS names of the directory's certificate, as a YAML list.
*/}}
{{- define "identity-directory.backendTls.dnsNames" -}}
{{- $service := include "identity-directory.fullname" . }}
- {{ $service }}
- {{ printf "%s.%s" $service .Release.Namespace }}
- {{ include "identity-directory.backendTls.serverName" . }}
- {{ printf "%s.%s.svc.cluster.local" $service .Release.Namespace }}
{{- end }}

{{/*
The issuer of the pods' certificates: backendTls.certManager.issuerRef, or else the release's private CA Issuer.
*/}}
{{- define "identity-directory.backendTls.issuerRef" -}}
{{- $issuerRef := .Values.backendTls.certManager.issuerRef }}
{{- if $issuerRef.name }}
name: {{ $issuerRef.name }}
kind: {{ $issuerRef.kind }}
group: {{ $issuerRef.group }}
{{- else }}
name: {{ printf "%s-ca" (include "identity-directory.fullname" .) }}
kind: Issuer
group: cert-manager.io
{{- end }}
{{- end }}

{{/*
The Certificate that issues the directory's serving certificate.
*/}}
{{- define "identity-directory.backendTls.certificate" -}}
{{- $certManager := .Values.backendTls.certManager }}
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: {{ include "identity-directory.backendTls.secretName" . }}
  labels:
    {{- include "identity-directory.labels" . | nindent 4 }}
spec:
  secretName: {{ include "identity-directory.backendTls.secretName" . }}
  duration: {{ $certManager.duration }}
  renewBefore: {{ $certManager.renewBefore }}
  privateKey:
    algorithm: {{ $certManager.privateKey.algorithm }}
    size: {{ $certManager.privateKey.size }}
    encoding: PKCS8
    rotationPolicy: Always
  usages:
    - digital signature
    - server auth
  dnsNames:
    {{- include "identity-directory.backendTls.dnsNames" . | nindent 4 }}
  issuerRef:
    {{- include "identity-directory.backendTls.issuerRef" . | trim | nindent 4 }}
{{- end }}

{{/*
The Certificate that issues the ingress controller's client certificate.
*/}}
{{- define "identity-directory.backendTls.ingressCertificate" -}}
{{- $certManager := .Values.backendTls.certManager }}
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: {{ include "identity-directory.backendTls.ingressSecretName" . }}
  labels:
    {{- include "identity-directory.labels" . | nindent 4 }}
spec:
  secretName: {{ include "identity-directory.backendTls.ingressSecretName" . }}
  commonName: {{ .Values.backendTls.clientCertificate.commonName }}
  duration: {{ $certManager.duration }}
  renewBefore: {{ $certManager.renewBefore }}
  privateKey:
    algorithm: {{ $certManager.privateKey.algorithm }}
    size: {{ $certManager.privateKey.size }}
    encoding: PKCS8
    rotationPolicy: Always
  usages:
    - digital signature
    - client auth
  issuerRef:
    {{- include "identity-directory.backendTls.issuerRef" . | trim | nindent 4 }}
{{- end }}

{{/*
The environment of the directory container: the one HTTPS endpoint and the certificate files the mounted Secret holds.
When backendTls.clientCertificate.required is true the pod also requires the ingress controller's client certificate:
one that chains to the mounted ca.crt and whose subject is CN=<backendTls.clientCertificate.commonName>.
*/}}
{{- define "identity-directory.backendTls.env" -}}
- name: ASPNETCORE_URLS
  value: "https://+:8443"
- name: Kestrel__Certificates__Default__Path
  value: /etc/ncode-identity/tls/tls.crt
- name: Kestrel__Certificates__Default__KeyPath
  value: /etc/ncode-identity/tls/tls.key
{{- if .Values.backendTls.clientCertificate.required }}
- name: ProxyClientCertificate__Required
  value: "true"
- name: ProxyClientCertificate__CertificateAuthorityPath
  value: /etc/ncode-identity/tls/ca.crt
- name: ProxyClientCertificate__AllowedSubjects__0
  value: {{ printf "CN=%s" .Values.backendTls.clientCertificate.commonName | quote }}
{{- end }}
{{- end }}

{{/*
The Service annotations that make the Traefik controller speak HTTPS to the Service through the release's
ServersTransport, which verifies the pods' certificate and, when backendTls.clientCertificate.required is true,
presents the controller's client certificate.
*/}}
{{- define "identity-directory.backendTls.traefikServiceAnnotations" -}}
traefik.ingress.kubernetes.io/service.serversscheme: https
traefik.ingress.kubernetes.io/service.serverstransport: {{ printf "%s-%s@kubernetescrd" .Release.Namespace (include "identity-directory.fullname" .) | quote }}
{{- end }}

{{/*
The name of the ConfigMap that holds the CA a Gateway API implementation verifies the pods against:
backendTls.caConfigMap, or <release>-ca when backendTls.caPublisher.enabled publishes it. Empty when none is set.
*/}}
{{- define "identity-directory.backendTls.caConfigMap" -}}
{{- $name := .Values.backendTls.caConfigMap }}
{{- if and (not $name) .Values.backendTls.caPublisher.enabled }}
{{- $name = printf "%s-ca" (include "identity-directory.fullname" .) }}
{{- end }}
{{- $name }}
{{- end }}

{{/*
The BackendTLSPolicy that makes a Gateway API implementation speak HTTPS to the Service and verify its pods' certificate
against the CA ConfigMap.
*/}}
{{- define "identity-directory.backendTls.policy" -}}
apiVersion: gateway.networking.k8s.io/v1
kind: BackendTLSPolicy
metadata:
  name: {{ include "identity-directory.fullname" . }}
  labels:
    {{- include "identity-directory.labels" . | nindent 4 }}
spec:
  targetRefs:
    - group: ""
      kind: Service
      name: {{ include "identity-directory.fullname" . }}
      sectionName: https
  validation:
    caCertificateRefs:
      - group: ""
        kind: ConfigMap
        name: {{ include "identity-directory.backendTls.caConfigMap" . }}
    hostname: {{ include "identity-directory.backendTls.serverName" . | quote }}
{{- end }}

{{/*
The HTTPRoute that sends every path of each allowed host to the Service's HTTPS port.
*/}}
{{- define "identity-directory.httpRoute" -}}
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: {{ include "identity-directory.fullname" . }}
  labels:
    {{- include "identity-directory.labels" . | nindent 4 }}
  {{- with .Values.httpRoute.annotations }}
  annotations:
    {{- toYaml . | nindent 4 }}
  {{- end }}
spec:
  parentRefs:
    {{- toYaml .Values.httpRoute.parentRefs | nindent 4 }}
  hostnames:
    {{- toYaml .Values.allowedHosts | nindent 4 }}
  rules:
    - matches:
        - path:
            type: PathPrefix
            value: /
      backendRefs:
        - name: {{ include "identity-directory.fullname" . }}
          port: {{ .Values.service.port }}
{{- end }}

{{/*
Refuses a certificate the chart's Certificate could not describe to cert-manager, and a route the chart could not wire.
Rendered for its side effect only.
*/}}
{{- define "identity-directory.backendTls.validate" -}}
{{- $certManager := .Values.backendTls.certManager }}
{{- if ge (int (trimSuffix "h" $certManager.renewBefore)) (int (trimSuffix "h" $certManager.duration)) }}
{{- fail "backendTls.certManager.renewBefore must be shorter than backendTls.certManager.duration." }}
{{- end }}
{{- if and (eq $certManager.privateKey.algorithm "ECDSA") (gt (int $certManager.privateKey.size) 521) }}
{{- fail "backendTls.certManager.privateKey.size must be 256, 384 or 521 for an ECDSA key." }}
{{- end }}
{{- if and (eq $certManager.privateKey.algorithm "RSA") (lt (int $certManager.privateKey.size) 2048) }}
{{- fail "backendTls.certManager.privateKey.size must be at least 2048 for an RSA key." }}
{{- end }}
{{- if and .Values.ingress.enabled (not .Values.ingress.controller) }}
{{- fail "ingress.controller must be traefik or other when an Ingress is enabled: the pods serve HTTPS only, so the controller must speak HTTPS to the Service and verify the pods' certificate." }}
{{- end }}
{{- if .Values.httpRoute.enabled }}
{{- if not .Values.httpRoute.parentRefs }}
{{- fail "httpRoute.parentRefs must name a Gateway listener when httpRoute.enabled is true: an HTTPRoute without a parent attaches to nothing." }}
{{- end }}
{{- if not (include "identity-directory.backendTls.caConfigMap" .) }}
{{- fail "backendTls.caConfigMap must be set, or backendTls.caPublisher.enabled with the chart's private CA, when httpRoute.enabled is true: the BackendTLSPolicy verifies the pods' certificate against the CA in that ConfigMap." }}
{{- end }}
{{- end }}
{{- if and .Values.backendTls.caPublisher.enabled (or $certManager.issuerRef.name .Values.backendTls.existingSecret) }}
{{- fail "backendTls.caPublisher.enabled publishes the chart's private CA to the CA ConfigMap, so the directory must take its certificate from that CA: backendTls.certManager.issuerRef.name and backendTls.existingSecret must be empty." }}
{{- end }}
{{- if and (not $certManager.issuerRef.name) (include "identity-directory.backendTls.controllerSecretUsed" .) .Values.backendTls.existingSecret (not .Values.backendTls.ingressSecret) }}
{{- fail "backendTls.ingressSecret is required when backendTls.existingSecret is set without backendTls.certManager.issuerRef and the route uses the ingress controller's Secret: the chart does not know the CA that signed your certificate." }}
{{- end }}
{{- end }}
