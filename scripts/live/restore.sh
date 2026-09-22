#!/usr/bin/env bash
# Restore a backup onto a NEW, empty live host: scripts/live/restore.sh BACKUP
#   BACKUP = local directory with manifest.json, or "remote:STAMP" to fetch from BACKUP_REMOTE.
# Refuses to touch a database that already has IntegraOps data.
source "$(dirname "$0")/common.sh"
source_arg="${1:?Kullanım: scripts/live/restore.sh YEDEK_DİZİNİ | remote:ZAMAN}"
mkdir -p "$STATE_DIR/backups" "$STATE_DIR/metrics"

if [[ "$source_arg" == remote:* ]]; then
  stamp="${source_arg#remote:}"
  [[ "$stamp" =~ ^20[0-9]{6}T[0-9]{6}Z$ ]] || { echo "Geçersiz yedek adı: $stamp" >&2; exit 1; }
  remote="$(env_get BACKUP_REMOTE)"; [ -n "$remote" ] || { echo "BACKUP_REMOTE gerekli." >&2; exit 1; }
  rclone_run -v "$STATE_DIR/backups:/data" -- copy "$remote/$stamp" "/data/$stamp"
  dir="$STATE_DIR/backups/$stamp"
else
  dir="$(cd "$source_arg" && pwd)"
fi

python3 - "$dir" <<'PY'
import hashlib, json, sys, pathlib
d = pathlib.Path(sys.argv[1]); m = json.load(open(d / "manifest.json"))
for name, digest in m["sha256"].items():
    if hashlib.sha256((d / name).read_bytes()).hexdigest() != digest:
        raise SystemExit(f"Özet uyuşmuyor: {name}")
print("Yedek özetleri doğrulandı.")
PY
tag="${RESTORE_IMAGE_TAG:-$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]+'/manifest.json'))['image_tag'])" "$dir")}"
[ -n "$tag" ] || { echo "Sürüm etiketi bilinmiyor; RESTORE_IMAGE_TAG verin." >&2; exit 1; }
log "Geri yükleme: $dir (sürüm $tag)"
env_set IMAGE_TAG "$tag"
python3 "$ROOT/scripts/live/render_alertmanager.py" "$LIVE_ENV" > /dev/null
pull_images api worker panel migrate

live_compose up -d --wait postgres
existing="$(live_compose exec -T postgres psql -U integraops -d integraops -tAc "SELECT to_regclass('public.schema_migrations') IS NOT NULL")"
if [ "$existing" = "t" ]; then
  echo "Hedef veritabanında IntegraOps verisi var. Geri yükleme yalnızca boş kuruluma yapılır." >&2
  exit 1
fi
live_compose exec -T postgres pg_restore -U integraops -d integraops --exit-on-error --no-owner < "$dir/integraops.dump"

# Keys: same Data Protection key ring, owned by the app user, before API/worker start.
live_compose run --rm --no-deps -T --user 0:0 --entrypoint bash -v "$dir:/backup:ro" api \
  -ec 'tar -xzf /backup/protection-keys.tar.gz -C /var/lib/integraops/keys && chown -R app:app /var/lib/integraops/keys'

# The new RabbitMQ is empty: republish every event that was accepted but not processed.
{ echo "SET integraops.restore_rehearsal = 'yes';"; cat "$ROOT/scripts/restore_requeue.sql"; } \
  | live_compose exec -T postgres psql -U integraops -d integraops -v ON_ERROR_STOP=1 -q
live_compose up -d --remove-orphans
wait_ready "$tag" || { log "Geri yüklenen sürüm hazır olmadı. Günlükler: docker compose -p $PROJECT logs api worker"; exit 1; }
printf '%s\trestored\t%s\t%s\tschema=%s\n' "$(date -u +%FT%TZ)" "$tag" "$(basename "$dir")" "$(schema_version)" >> "$STATE_DIR/releases.log"
log "Geri yükleme tamamlandı. Paneli açıp son işlemleri ve alarm geçmişini kontrol edin."
