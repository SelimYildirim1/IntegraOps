#!/usr/bin/env bash
# Return to the previous release: scripts/live/rollback.sh [ETİKET]
# Image-only rollback is allowed when the database schema did not change since that release.
# When a migration ran, restore the pre-deploy backup instead (docs/canli-ortam.md, "Şema değişen sürümden dönüş").
source "$(dirname "$0")/common.sh"
history="$STATE_DIR/releases.log"
current="$(env_get IMAGE_TAG)"
target="${1:-}"
if [ -z "$target" ]; then
  target="$(awk -F'\t' -v cur="$current" '$2=="deployed" && $3==cur {prev=$4} END {print prev}' "$history" 2>/dev/null)"
fi
[ -n "$target" ] || { echo "Dönülecek sürüm bulunamadı; etiketi açıkça verin." >&2; exit 1; }
[ "$target" != "$current" ] || { echo "Zaten $current çalışıyor." >&2; exit 1; }

# Schema version the target release ran with, recorded at its deployment.
target_schema="$(awk -F'\t' -v t="$target" '$2=="deployed" && $3==t {split($5,a,"->"); s=a[length(a)]} END {print s}' "$history" 2>/dev/null)"
target_schema="${target_schema#schema=}"
current_schema="$(schema_version)"
if [ -n "$target_schema" ] && [ "$current_schema" != "$target_schema" ]; then
  echo "Şema $target_schema -> $current_schema değişmiş. Yalnızca imaj değiştirmek veriyi bozabilir." >&2
  echo "Yayın öncesi yedekten geri yükleyin (docs/canli-ortam.md)." >&2
  exit 1
fi
[ -n "$target_schema" ] || log "Uyarı: $target için şema kaydı yok; şema $current_schema varsayılıyor."

log "Geri dönüş: $current -> $target"
IMAGE_TAG="$target" pull_images api worker panel
env_set IMAGE_TAG "$target"
# migrate is idempotent and has nothing to apply at an equal schema version.
live_compose up -d --remove-orphans
wait_ready "$target" || { log "GERİ DÖNÜŞ BAŞARISIZ: $target hazır olmadı."; exit 1; }
printf '%s\trolledback\t%s\t%s\tschema=%s\n' "$(date -u +%FT%TZ)" "$target" "$current" "$current_schema" >> "$history"
log "Geri dönüş tamamlandı: $target"
