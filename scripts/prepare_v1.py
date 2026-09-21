"""Append missing local settings; never replace existing credentials."""
import os
import secrets
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def read_env(path):
    values = {}
    if path.exists():
        for line in path.read_text().splitlines():
            if line.strip() and not line.lstrip().startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip('"').strip("'")
    return values

def update_env(path, changes):
    text = path.read_text() if path.exists() else ""
    lines = text.splitlines()
    for key, value in changes.items():
        replacement = f"{key}={value}"
        matches = [i for i, line in enumerate(lines) if line.startswith(key + "=")]
        if matches:
            lines[matches[0]] = replacement
        else:
            lines.append(replacement)
    temporary = path.with_name(path.name + ".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        stream.write("\n".join(lines) + "\n")
    os.replace(temporary, path)
    os.chmod(path, 0o600)

def main():
    path = ROOT / ".env"
    current = read_env(path)
    if not all(current.get(k) for k in ("POSTGRES_PASSWORD", "RABBITMQ_PASSWORD", "BOOTSTRAP_KEY")):
        raise SystemExit("Mevcut .env bulunamadı veya eksik. İlk kurulumdaki scripts/init_local.py dosyasını kullanın.")
    defaults = {"LOCAL_WEBHOOK_SECRET": secrets.token_hex(32), "SOURCE_POSTGRES_PASSWORD": secrets.token_hex(24)}
    update_env(path, {k: v for k, v in defaults.items() if not current.get(k)})
    panel = ROOT / "frontend" / ".env.local"
    if panel.parent.exists():
        config = read_env(panel)
        update_env(panel, {k: v for k, v in {"INTEGRAOPS_API_URL": "http://127.0.0.1:" + current.get("API_PORT", "8080"),
            "PANEL_COOKIE_SECURE": "false"}.items() if k not in config})
    print("V1 yerel ayarları hazır; mevcut anahtarlar korundu.")

if __name__ == "__main__":
    main()
