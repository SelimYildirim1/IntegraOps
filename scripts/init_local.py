"""Create local development secrets without overwriting existing configuration."""
from pathlib import Path
import secrets

root = Path(__file__).resolve().parents[1]
target = root / ".env"
if target.exists():
    raise SystemExit(".env zaten var; mevcut ayarlar korundu.")
values = {
    "POSTGRES_PASSWORD": secrets.token_hex(24),
    "RABBITMQ_PASSWORD": secrets.token_hex(24),
    "BOOTSTRAP_KEY": secrets.token_hex(32),
    "POSTGRES_PORT": "54329",
    "RABBITMQ_PORT": "56729",
    "RABBITMQ_MANAGEMENT_PORT": "15679",
    "API_PORT": "8080",
}
with target.open("x", encoding="utf-8") as output:
    output.write("\n".join(f"{key}={value}" for key, value in values.items()) + "\n")
target.chmod(0o600)
print("Yerel ayarlar hazır. Sonraki komut: docker compose up --build -d")
