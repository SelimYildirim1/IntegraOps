#!/usr/bin/env bash
# Live backup: database + Data Protection keys, verified, copied off-site, then recorded for monitoring.
# Schedule with deploy/systemd/integraops-backup.timer (daily). Safe to run while the system serves traffic.
source "$(dirname "$0")/common.sh"

lock="$STATE_DIR/backup.lock"
mkdir -p "$STATE_DIR/backups" "$STATE_DIR/metrics"
mkdir "$lock" 2>/dev/null || { log "Başka bir yedekleme sürüyor ($lock)."; exit 1; }
trap 'rmdir "$lock"' EXIT

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
dir="$STATE_DIR/backups/$stamp"
mkdir -p "$dir"
log "Yedek başlıyor: $dir"
live_compose exec -T postgres pg_dump -U integraops -d integraops -Fc > "$dir/integraops.dump"
live_compose exec -T api tar -C /var/lib/integraops/keys -czf - . > "$dir/protection-keys.tar.gz"
test -s "$dir/integraops.dump" && test -s "$dir/protection-keys.tar.gz"

# Verify the archive is readable before trusting it.
live_compose exec -T postgres pg_restore --list < "$dir/integraops.dump" > /dev/null
tar -tzf "$dir/protection-keys.tar.gz" | grep -q '\.xml$' || { log "Anahtar arşivinde anahtar dosyası yok."; exit 1; }

image_tag="$(env_get IMAGE_TAG)"
python3 - "$dir" "$image_tag" "$(schema_version)" <<'PY'
import hashlib, json, sys, datetime, pathlib
d = pathlib.Path(sys.argv[1])
files = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(d.iterdir()) if p.is_file()}
json.dump({"created_at": datetime.datetime.now(datetime.timezone.utc).isoformat(), "image_tag": sys.argv[2],
           "schema_version": int(sys.argv[3] or 0), "sha256": files}, open(d / "manifest.json", "w"), indent=1)
PY

remote="$(env_get BACKUP_REMOTE)"
if [ -n "$remote" ]; then
  [ -f "$STATE_DIR/rclone/rclone.conf" ] || { log "rclone ayarı yok: $STATE_DIR/rclone/rclone.conf"; exit 1; }
  rclone() { rclone_run -v "$STATE_DIR/backups:/data:ro" -- "$@"; }
  rclone copy "/data/$stamp" "$remote/$stamp"
  # Compare checksums on the remote side; a copy that cannot be read back is not a backup.
  rclone check "/data/$stamp" "$remote/$stamp" --one-way
  keep_remote="$(env_get BACKUP_REMOTE_KEEP_DAYS)"
  rclone delete "$remote" --min-age "${keep_remote:-90}d" && rclone rmdirs "$remote" --leave-root || log "Uzak eski yedekler temizlenemedi; yeni yedek güvende."
  log "Uzak kopya doğrulandı: $remote/$stamp"
elif [ "$(env_get DEPLOYMENT_ENVIRONMENT)" = "production" ]; then
  log "BACKUP_REMOTE boş. Canlı ortamda sunucu dışı kopya zorunlu; yedek başarılı sayılmadı."
  exit 1
else
  log "Uyarı: BACKUP_REMOTE boş; yalnızca yerel kopya var (canlı ortam dışı)."
fi

keep="$(env_get BACKUP_KEEP_DAYS)"
find "$STATE_DIR/backups" -mindepth 1 -maxdepth 1 -type d -name '20*Z' -mtime +"${keep:-14}" -exec rm -rf {} +
bash "$ROOT/scripts/write_backup_metric.sh" "$STATE_DIR/metrics" "$dir"
log "Yedek tamamlandı: $stamp"
