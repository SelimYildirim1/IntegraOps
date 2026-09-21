#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."
backup_dir="backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
docker compose exec -T postgres pg_dump -U integraops -d integraops -Fc > "$backup_dir/integraops.dump"
docker compose exec -T api tar -C /var/lib/integraops/keys -czf - . > "$backup_dir/protection-keys.tar.gz"
if docker compose --profile demo ps --status running --services | grep -qx source-postgres; then
  docker compose exec -T source-postgres pg_dump -U source -d source -Fc > "$backup_dir/source.dump"
fi
test -s "$backup_dir/integraops.dump"
test -s "$backup_dir/protection-keys.tar.gz"
echo "Yedek hazır: $backup_dir"
