#!/usr/bin/env bash
# Shared helpers for live operations. Source from the other scripts; never run directly.
set -euo pipefail
umask 077
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LIVE_ENV="${LIVE_ENV:-$ROOT/.env.live}"
[ -f "$LIVE_ENV" ] || { echo "Ayar dosyası yok: $LIVE_ENV (.env.live.example dosyasından oluşturun)" >&2; exit 1; }

env_get() { # env_get KEY -> value from LIVE_ENV (last definition wins), without sourcing the file
  python3 - "$LIVE_ENV" "$1" <<'PY'
import sys
value = ""
for line in open(sys.argv[1], encoding="utf-8"):
    line = line.strip()
    if line and not line.startswith("#") and "=" in line:
        k, v = line.split("=", 1)
        if k.strip() == sys.argv[2]:
            value = v.strip().strip('"').strip("'")
print(value)
PY
}

env_set() { # env_set KEY VALUE -> replace or append in LIVE_ENV atomically
  python3 - "$LIVE_ENV" "$1" "$2" <<'PY'
import os, sys
path, key, value = sys.argv[1:4]
lines = open(path, encoding="utf-8").read().splitlines()
hits = [i for i, l in enumerate(lines) if l.startswith(key + "=")]
if hits: lines[hits[0]] = f"{key}={value}"
else: lines.append(f"{key}={value}")
tmp = path + ".tmp"
fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
with os.fdopen(fd, "w") as f: f.write("\n".join(lines) + "\n")
os.replace(tmp, path)
PY
}

STATE_DIR="$(env_get LIVE_STATE_DIR)"
[ -n "$STATE_DIR" ] || { echo "LIVE_STATE_DIR gerekli." >&2; exit 1; }
PROJECT="$(env_get COMPOSE_PROJECT_NAME)"; PROJECT="${PROJECT:-integraops-live}"

EXTRA_COMPOSE="$(env_get LIVE_COMPOSE_EXTRA)" # optional override next to compose.live.yml, e.g. compose.shared-host.yml

live_compose() { # IMAGE_TAG may be overridden through the environment for a single call
  local files=(-f "$ROOT/compose.live.yml")
  [ -z "$EXTRA_COMPOSE" ] || files+=(-f "$ROOT/$EXTRA_COMPOSE")
  docker compose --project-directory "$ROOT" "${files[@]}" --env-file "$LIVE_ENV" -p "$PROJECT" "$@"
}

pull_images() { # pull_images SERVICE... ; LIVE_SKIP_PULL=1 only for rehearsals with locally built images
  [ "${LIVE_SKIP_PULL:-0}" = "1" ] && return 0
  live_compose pull --quiet "$@"
}

RCLONE_IMAGE=rclone/rclone:1.71.0
rclone_run() { # rclone_run DOCKER_VOLUME_ARGS... -- RCLONE_ARGS...
  local docker_args=()
  while [ "$1" != "--" ]; do docker_args+=("$1"); shift; done; shift
  # shellcheck disable=SC2086
  docker run --rm --user "$(id -u):$(id -g)" ${RCLONE_DOCKER_ARGS:-} -v "$STATE_DIR/rclone:/config/rclone" "${docker_args[@]}" "$RCLONE_IMAGE" "$@"
}

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

api_get() { # api_get PATH -> raw HTTP response from inside the api container (no curl in the image)
  live_compose exec -T api bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET %s HTTP/1.0\r\nHost: localhost\r\n\r\n" "$1" >&3 && cat <&3' _ "$1" 2>/dev/null
}

wait_ready() { # wait_ready EXPECTED_VERSION
  local expected="$1" deadline=$((SECONDS + 180))
  while [ $SECONDS -lt $deadline ]; do
    if api_get /health/version | grep -q "\"version\":\"$expected\"" && api_get /health/ready | head -1 | grep -q " 200 "; then
      return 0
    fi
    sleep 3
  done
  return 1
}

schema_version() {
  live_compose exec -T postgres psql -U integraops -d integraops -tAc "SELECT COALESCE(max(version),0) FROM schema_migrations" 2>/dev/null || echo 0
}
