#!/usr/bin/env bash
# Deploy one release: scripts/live/deploy.sh v1.2.0
# Takes a pre-deploy backup, pulls the tag's images, migrates, restarts and waits for the new version.
source "$(dirname "$0")/common.sh"
tag="${1:?Kullanım: scripts/live/deploy.sh SÜRÜM_ETİKETİ}"
[[ "$tag" =~ ^[A-Za-z0-9._-]{1,64}$ ]] || { echo "Geçersiz etiket: $tag" >&2; exit 1; }
previous="$(env_get IMAGE_TAG)"
history="$STATE_DIR/releases.log"
mkdir -p "$STATE_DIR/metrics"

python3 "$ROOT/scripts/live/render_alertmanager.py" "$LIVE_ENV" > /dev/null

log "Yayın: ${previous:-ilk kurulum} -> $tag"
IMAGE_TAG="$tag" pull_images api worker panel migrate

before=0
if [ -n "$previous" ] && live_compose ps --status running --services | grep -qx postgres; then
  before="$(schema_version)"
  log "Yayın öncesi yedek alınıyor."
  bash "$ROOT/scripts/live/backup.sh"
fi

env_set IMAGE_TAG "$tag"
live_compose up -d --remove-orphans || log "Servisler başlatılırken hata oluştu; hazır olma kontrolü sonucu belirleyecek."
if ! wait_ready "$tag"; then
  log "YAYIN BAŞARISIZ: $tag hazır olmadı. Önceki sürüme dönmek için: scripts/live/rollback.sh"
  printf '%s\tfailed\t%s\t%s\tschema=%s\n' "$(date -u +%FT%TZ)" "$tag" "${previous:-}" "$before" >> "$history"
  exit 1
fi
after="$(schema_version)"
printf '%s\tdeployed\t%s\t%s\tschema=%s->%s\n' "$(date -u +%FT%TZ)" "$tag" "${previous:-}" "$before" "$after" >> "$history"
log "Yayın tamamlandı: $tag (şema $before -> $after)"
