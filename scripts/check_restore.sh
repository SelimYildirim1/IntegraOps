#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backup_dir="${1:?Kullanım: bash scripts/check_restore.sh backups/ZAMAN}"
test -s "$backup_dir/integraops.dump"
test -s "$backup_dir/protection-keys.tar.gz"
restore_db="restore_check_$(date -u +%Y%m%d%H%M%S)_$$"
# Only a newly created, uniquely named database is touched; never the working database.
docker compose exec -T postgres createdb -U integraops "$restore_db"
docker compose exec -T postgres pg_restore -U integraops -d "$restore_db" --exit-on-error --no-owner < "$backup_dir/integraops.dump"
docker compose exec -T postgres psql -U integraops -d "$restore_db" -v ON_ERROR_STOP=1 -c \
  "SELECT 'transactions' AS kind,count(*) FROM transactions UNION ALL SELECT 'alarms',count(*) FROM alarms UNION ALL SELECT 'users',count(*) FROM app_users;"
tar -tzf "$backup_dir/protection-keys.tar.gz" > /dev/null
echo "Veritabanı geri yükleme ve anahtar arşivi okuma başarılı. Kontrol veritabanı: $restore_db"
echo "İnceledikten sonra yalnızca bu kontrol veritabanını kaldırabilirsiniz: docker compose exec postgres dropdb -U integraops $restore_db"
