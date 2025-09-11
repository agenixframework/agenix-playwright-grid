{{- define "pg.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "pg.fullname" -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "pg.labels" -}}
app.kubernetes.io/name: {{ include "pg.name" . }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{- define "pg.selectorLabels" -}}
app.kubernetes.io/name: {{ include "pg.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "pg.hub.name" -}}
{{ printf "%s-hub" (include "pg.fullname" .) }}
{{- end -}}

{{- define "pg.worker.name" -}}
{{ printf "%s-worker" (include "pg.fullname" .) }}
{{- end -}}

{{- define "pg.dashboard.name" -}}
{{ printf "%s-dashboard" (include "pg.fullname" .) }}
{{- end -}}

{{- define "pg.redis.name" -}}
{{ printf "%s-redis" (include "pg.fullname" .) }}
{{- end -}}
