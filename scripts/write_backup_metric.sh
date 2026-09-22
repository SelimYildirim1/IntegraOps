#!/usr/bin/env bash
# Records a successful backup for Prometheus (node-exporter textfile collector).
# Usage: write_backup_metric.sh METRICS_DIR BACKUP_DIR
set -euo pipefail
metrics_dir="${1:?metrics dir}"; backup_dir="${2:?backup dir}"
mkdir -p "$metrics_dir"
size=$(du -sk "$backup_dir" | awk '{print $1 * 1024}')
tmp="$metrics_dir/.integraops_backup.prom.$$"
cat > "$tmp" <<METRICS
# HELP integraops_backup_last_success_timestamp_seconds Unix time of the last verified backup.
# TYPE integraops_backup_last_success_timestamp_seconds gauge
integraops_backup_last_success_timestamp_seconds $(date -u +%s)
# HELP integraops_backup_last_size_bytes Size of the last verified backup.
# TYPE integraops_backup_last_size_bytes gauge
integraops_backup_last_size_bytes $size
METRICS
chmod 644 "$tmp"
# Atomic rename so node-exporter never reads a half-written file.
mv "$tmp" "$metrics_dir/integraops_backup.prom"
