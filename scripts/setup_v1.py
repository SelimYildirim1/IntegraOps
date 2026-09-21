"""Create the first owner, local notification channels and the source integration key."""
import getpass
import json
import os
import time
import urllib.error
import urllib.request
from prepare_v1 import ROOT, read_env, update_env

def request(base, path, data=None, headers=None, method=None):
    payload = None if data is None else json.dumps(data).encode()
    req = urllib.request.Request(base.rstrip("/") + path, data=payload,
        headers={"Content-Type": "application/json", **(headers or {})}, method=method or ("POST" if payload is not None else "GET"))
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        body = error.read()
        return error.code, json.loads(body) if body else None

def require(result, codes=(200, 201, 204)):
    status, body = result
    if status not in codes:
        title = body.get("title", "İstek tamamlanamadı") if isinstance(body, dict) else "İstek tamamlanamadı"
        raise SystemExit(f"HTTP {status}: {title}")
    return body

def main():
    env = read_env(ROOT / ".env")
    panel = read_env(ROOT / "frontend" / ".env.local")
    base = panel.get("INTEGRAOPS_API_URL", "http://127.0.0.1:" + env.get("API_PORT", "8080"))
    for attempt in range(30):
        try:
            if request(base, "/health/ready")[0] == 200:
                break
        except (OSError, urllib.error.URLError):
            pass
        if attempt == 29:
            raise SystemExit("API hazır değil. docker compose logs --tail=50 api migrate ile kontrol edin.")
        time.sleep(1)
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    name = (os.environ.get("V1_OWNER_NAME") or input("Ad soyad: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola (ilk kurulumda en az 12 karakter): ")
    # Try login first so re-running setup never needs the revoked legacy key.
    login_status, login_body = request(base, "/api/v1/auth/login", {"email": email, "password": password})
    if login_status != 200:
        saved = ROOT / ".local" / "demo-project.json"
        saved_key = json.loads(saved.read_text()).get("apiKey") if saved.exists() else None
        key = panel.get("INTEGRAOPS_API_KEY") or saved_key or getpass.getpass("Mevcut proje API anahtarı: ")
        status, body = request(base, "/api/v1/bootstrap/owner", {"email": email, "name": name, "password": password},
            {"X-Bootstrap-Key": env["BOOTSTRAP_KEY"], "X-API-Key": key})
        require((status, body), (200, 409))
        login_body = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))
    headers = {"Authorization": "Bearer " + login_body["token"]}
    projects = require(request(base, "/api/v1/me/projects", headers=headers))
    owners = [p for p in projects if p["role"] == "owner"]
    if not owners:
        raise SystemExit("Bu hesap bir projenin sahibi olmalı.")
    selected = next((p for p in owners if p["id"] == env.get("PILOT_PROJECT_ID")), None)
    if selected is None:
        for index, project in enumerate(owners, 1):
            print(f"{index}. {project['name']} ({project['id']})")
        choice = 1 if len(owners) == 1 else int(input("Kurulum yapılacak proje numarası: "))
        if not 1 <= choice <= len(owners):
            raise SystemExit("Proje numarası geçersiz.")
        selected = owners[choice - 1]
    headers["X-Project-Id"] = selected["id"]
    flow = {"code": "pilot-source", "name": "Kaynak sistem denemesi", "steps": [
        {"code": "erp-created", "name": "ERP kaydı", "maxDelaySeconds": None},
        {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 30}]}
    require(request(base, "/api/v1/flows", flow, headers), (201, 409))
    channels = require(request(base, "/api/v1/channels", headers=headers))
    for channel in [
        {"name": "Yerel e-posta", "kind": "email", "target": "operations@local.test"},
        {"name": "Yerel webhook", "kind": "webhook", "target": "http://webhook:8080/hook", "secret": env["LOCAL_WEBHOOK_SECRET"]}]:
        if not any(c["name"] == channel["name"] for c in channels):
            require(request(base, "/api/v1/channels", channel, headers))
    keys = require(request(base, "/api/v1/keys", headers=headers))
    old_key = env.get("SOURCE_API_KEY", "")
    reusable = env.get("PILOT_PROJECT_ID") == selected["id"] and any(k["scope"] == "ingest" and not k["revoked_at"]
        and k["key_prefix"] == old_key[:16] for k in keys)
    if not reusable:
        old_key = require(request(base, "/api/v1/keys", {"name": "Yerel kaynak sistemi", "scope": "ingest"}, headers))["key"]
    update_env(ROOT / ".env", {"SOURCE_API_KEY": old_key, "PILOT_PROJECT_ID": selected["id"]})
    require(request(base, "/api/v1/me/logout", headers=headers, method="POST"))
    print("Kurulum tamamlandı. Panelden giriş yapabilirsiniz.")
    print("Kaynak örneğini başlatmak için: docker compose --profile demo up --build -d source")
    print("Yerel e-postalar: http://localhost:8025 · Webhook kayıtları: http://localhost:8082/receipts")

if __name__ == "__main__":
    main()
