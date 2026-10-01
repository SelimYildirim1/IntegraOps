"""Render deploy/monitoring/alertmanager.live.yml.template into $LIVE_STATE_DIR/alertmanager.yml."""
import json
import os
import sys
from pathlib import Path
from string import Template

ROOT = Path(__file__).resolve().parents[2]


def read_env(path):
    values = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, v = line.split("=", 1)
            values[k.strip()] = v.strip().strip('"').strip("'")
    return values


def main():
    env = read_env(sys.argv[1] if len(sys.argv) > 1 else ROOT / ".env.live")
    for key in ("OPS_ALERT_EMAIL", "SMTP_FROM", "SMTP_HOST", "LIVE_STATE_DIR"):
        if not env.get(key):
            raise SystemExit(f"{key} gerekli.")
    # YAML strings are JSON-compatible; json.dumps quotes and escapes every user value safely.
    q = lambda k, d="": json.dumps(env.get(k) or d)
    webhook = env.get("OPS_ALERT_WEBHOOK_URL", "")
    if webhook and not webhook.startswith("https://"):
        raise SystemExit("OPS_ALERT_WEBHOOK_URL https:// ile başlamalı.")
    block = f"    webhook_configs:\n      - url: {json.dumps(webhook)}\n        send_resolved: true\n" if webhook else ""
    text = Template((ROOT / "deploy/monitoring/alertmanager.live.yml.template").read_text(encoding="utf-8")).substitute(
        OPS_ALERT_EMAIL=q("OPS_ALERT_EMAIL"), SMTP_FROM=q("SMTP_FROM"),
        SMTP_HOST=env["SMTP_HOST"], SMTP_PORT=env.get("SMTP_PORT") or "587",
        SMTP_USER=q("SMTP_USER"), SMTP_PASSWORD=q("SMTP_PASSWORD"),
        SMTP_STARTTLS="false" if env.get("SMTP_STARTTLS", "true").lower() == "false" else "true",
        WEBHOOK_BLOCK=block)
    # smarthost is host:port and must be one quoted scalar.
    text = text.replace(f"smarthost: {env['SMTP_HOST']}:{env.get('SMTP_PORT') or '587'}",
                        "smarthost: " + json.dumps(f"{env['SMTP_HOST']}:{env.get('SMTP_PORT') or '587'}"))
    target = Path(env["LIVE_STATE_DIR"]) / "alertmanager.yml"
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp = target.with_suffix(".tmp")
    # Alertmanager runs as "nobody"; the file holds the SMTP password, so keep it out of other users' reach
    # except through the container's read-only mount.
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(text)
    os.chmod(tmp, 0o644)  # the live scripts run with umask 077, which would otherwise strip the read bit
    os.replace(tmp, target)
    print(target)


if __name__ == "__main__":
    main()
