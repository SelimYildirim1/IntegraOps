"""V2–V3 reusable-integration checks: connections, catalog templates, mapping dry run, mapped ingest,
usage and client-version tracking, ping, key rotation with grace, SDK deliveries (Python and Node)
and deterministic event ids shared by both SDKs and the server mapper. Isolated test project.
"""
import getpass
import json
import os
import subprocess
import sys
import time
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

sys.path.insert(0, str(ROOT / "sdk" / "python"))
from integraops_sender import Event, IntegraOpsSender, PermanentRejection  # noqa: E402


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:8]
    project = require(request(base, "/api/v1/me/projects", {"name": "Bağlantı kontrol " + suffix}, session))
    headers = session | {"X-Project-Id": project["id"]}
    passed = 0

    def check(name, value):
        nonlocal passed
        if not value:
            raise AssertionError(name)
        passed += 1
        print("GEÇTİ ·", name, flush=True)

    def api(path, data=None, auth=None, method=None):
        return request(base, "/api/v1" + path, data, headers if auth is None else auth, method)

    def wait(name, predicate, timeout=90):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                check(name, True)
                return
            time.sleep(1)
        raise AssertionError(name + " zaman aşımı")

    print("Test projesi:", project["name"], project["id"])
    require(api("/flows", {"code": "erp-order-invoice", "name": "ERP", "steps": [
        {"code": "order-created", "name": "Sipariş", "maxDelaySeconds": None}, {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 14400}]}))

    # --- connections and catalog ---
    catalog = require(api("/connections/catalog"))
    check("Hazır şablon kataloğu 3 şablon veriyor", len(catalog) == 3 and {c["code"] for c in catalog} == {"generic-order", "erp-document", "webhook-batch"})
    check("Eşlemeli bağlantı şablon ister", api("/connections", {"name": "x", "kind": "mapped"})[0] == 400)
    check("Bilinmeyen şablon alanı reddedilir", api("/connections", {"name": "x", "kind": "mapped", "template": {"flow": "a", "transactionId": "$.a", "step": "s", "occurredAt": "$.t", "renk": "x"}})[0] == 400)
    direct = require(api("/connections", {"name": "ERP direct " + suffix, "kind": "sdk", "description": "Python SDK"}))
    generic = next(c for c in catalog if c["code"] == "generic-order")
    mapped = require(api("/connections", {"name": "ERP mapped " + suffix, "kind": "mapped", "template": generic["template"], "templateCode": "generic-order"}))
    check("İki bağlantı oluşturuldu", len(require(api("/connections"))) == 2)
    check("Aynı ad reddedilir", api("/connections", {"name": "ERP direct " + suffix, "kind": "direct"})[0] == 409)

    # --- dry run ---
    dry = require(api("/connections/test", {"template": generic["template"], "sample": generic["sample"]}))
    check("Şablon denemesi geçerli olay üretti", len(dry["events"]) == 1 and dry["events"][0]["problems"] == [] and dry["events"][0]["event"]["transactionId"] == "SO-1001")
    bad = require(api("/connections/test", {"template": generic["template"], "sample": {"order_no": "SO-9", "step": "Invoice Created", "occurred_at": "dün"}}))
    check("Şablon denemesi sorunları kayıt başına raporluyor", any("step" in p for p in bad["events"][0]["problems"]) and any("occurredAt" in p for p in bad["events"][0]["problems"]))

    # --- keys bound to connections ---
    sdk_key = require(api("/keys", {"name": "SDK", "scope": "ingest", "connectionId": direct["id"]}))
    mapped_key = require(api("/keys", {"name": "Mapped", "scope": "ingest", "connectionId": mapped["id"]}))
    check("Anahtar bağlantıya bağlandı", any(k["id"] == sdk_key["id"] and k["connection"] == "ERP direct " + suffix for k in require(api("/keys"))))
    check("Bilinmeyen bağlantıya anahtar verilmez", api("/keys", {"name": "x", "scope": "ingest", "connectionId": str(uuid.uuid4())})[0] == 400)

    # --- mapped ingest ---
    payload = [{"event_id": str(uuid.uuid4()), "order_no": f"SO-{suffix}-1", "step": "order-created", "occurred_at": datetime.now(timezone.utc).isoformat(), "customer_class": "vip"},
               {"order_no": f"SO-{suffix}-1", "step": "invoice-created", "occurred_at": datetime.now(timezone.utc).isoformat()},
               {"order_no": f"SO-{suffix}-2", "step": "unknown-step", "occurred_at": datetime.now(timezone.utc).isoformat()}]
    status, result = api(f"/connections/{mapped['id']}/ingest", payload, {"X-API-Key": mapped_key["key"]})
    check("Eşlemeli alım: 2 kabul, 1 red", status == 202 and result["accepted"] == 2 and result["rejected"] == 1 and "step" in result["results"][2]["problem"])
    check("Kimliksiz kayıt için üretilen olay kimliği deterministik", result["results"][1]["eventId"] == api(f"/connections/{mapped['id']}/ingest", [payload[1]], {"X-API-Key": mapped_key["key"]})[1]["results"][0]["eventId"])
    check("Yeniden gönderim yinelenen sayıldı", api(f"/connections/{mapped['id']}/ingest", [payload[1]], {"X-API-Key": mapped_key["key"]})[1]["duplicates"] == 1)
    check("Başka bağlantının anahtarı eşlemeli alım yapamaz", api(f"/connections/{mapped['id']}/ingest", payload[:1], {"X-API-Key": sdk_key["key"]})[0] == 403)
    check("Okuma anahtarı eşlemeli alım yapamaz", api(f"/connections/{mapped['id']}/ingest", payload[:1], {"X-API-Key": require(api("/keys", {"name": "r", "scope": "read"}))["key"]})[0] == 403)
    wait("Eşlenen olaylar işlendi ve grup sabitlendi", lambda: (d := api(f"/flows/erp-order-invoice/transactions/SO-{suffix}-1")[1]).get("status") == "completed" and d.get("group") == "vip")
    conn = next(c for c in require(api("/connections")) if c["id"] == mapped["id"])
    check("Bağlantı kullanım sayılarını ve son hatayı tutuyor", conn["events_total"] >= 4 and conn["rejected_total"] == 1 and "step" in (conn["last_error"] or "") and conn["last_event_at"])

    # --- SDK (Python) ---
    sender = IntegraOpsSender(base, sdk_key["key"])
    receipt = sender.send(Event("erp-order-invoice", f"SDK-{suffix}", "order-created", datetime.now(timezone.utc)))
    check("Python SDK olayı teslim etti", receipt.duplicate is False)
    again = sender.send(Event("erp-order-invoice", f"SDK-{suffix}", "order-created", receipt_at := datetime.now(timezone.utc)))
    check("Python SDK deterministik kimlikle tekrar gönderimde yinelenen almıyor (farklı zaman → farklı olay)", again.duplicate is False)
    same = Event("erp-order-invoice", f"SDK-{suffix}", "invoice-created", receipt_at)
    sender.send(same)
    check("Aynı olay ikinci kez gönderilince yinelenen", sender.send(same).duplicate is True)
    try:
        sender.send(Event("erp-order-invoice", f"SDK-{suffix}", "nope", datetime.now(timezone.utc)))
        check("Bilinmeyen adım kalıcı red", False)
    except PermanentRejection as e:
        check("Bilinmeyen adım kalıcı red", e.status == 400)
    ping = sender.ping()
    check("Ping bağlantıyı doğruluyor", ping["ok"] is True)
    conn = next(c for c in require(api("/connections")) if c["id"] == direct["id"])
    check("Bağlantı istemci adını ve sürümünü kaydetti", conn["client_name"] == "integraops-sdk-python" and conn["client_version"] and conn["last_ping_at"] and conn["rejected_total"] == 1)
    check("Anahtar son kullanım zamanı yazıldı", next(k for k in require(api("/keys")) if k["id"] == sdk_key["id"])["last_used_at"])

    # --- SDK (Node) ---
    node = subprocess.run(["node", "--input-type=module", "-e", f"""
      import {{ IntegraOpsSender, deriveEventId }} from "{ROOT / 'sdk' / 'typescript' / 'dist' / 'index.js'}";
      const s = new IntegraOpsSender({{ baseUrl: "{base}", apiKey: "{sdk_key['key']}" }});
      const r = await s.send({{ flow: "erp-order-invoice", transactionId: "NODE-{suffix}", step: "order-created", occurredAt: new Date() }});
      console.log(JSON.stringify({{ duplicate: r.duplicate, derived: deriveEventId("x") }}));
    """], capture_output=True, text=True, cwd=ROOT)
    out = json.loads(node.stdout.strip().splitlines()[-1]) if node.returncode == 0 else None
    check("Node SDK olayı teslim etti", out is not None and out["duplicate"] is False)
    check("Python, Node ve sunucu aynı kimliği türetiyor", out["derived"] == str(uuid.uuid5(uuid.UUID("6f4a3c2e-8b71-4d5e-9a0f-2c1d3e4f5a6b"), "x")) == "144d2318-4031-5d31-a0e0-712528b987b7")

    # --- rotation ---
    rotated = require(api(f"/keys/{sdk_key['id']}/rotate", {"graceHours": 1}))
    check("Yenileme yeni anahtar üretti ve bağlantıyı korudu", rotated["scope"] == "ingest" and next(k for k in require(api("/keys")) if k["id"] == rotated["id"])["connection"] == "ERP direct " + suffix)
    check("Eski anahtar geçiş süresince çalışıyor", api("/events", Event("erp-order-invoice", f"ROT-{suffix}", "order-created", datetime.now(timezone.utc)).payload(), {"X-API-Key": sdk_key["key"]})[0] == 202)
    check("Yeni anahtar çalışıyor", api("/events", Event("erp-order-invoice", f"ROT-{suffix}", "invoice-created", datetime.now(timezone.utc)).payload(), {"X-API-Key": rotated["key"]})[0] == 202)
    check("Aynı anahtar ikinci kez yenilenemez", api(f"/keys/{sdk_key['id']}/rotate", {})[0] == 409)
    subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "integraops", "-d", "integraops", "-q", "-c",
        f"UPDATE api_keys SET expires_at = clock_timestamp() - interval '1 second' WHERE id = '{sdk_key['id']}'"], cwd=ROOT, check=True, capture_output=True)
    check("Geçiş süresi dolan eski anahtar reddediliyor", api("/events", Event("erp-order-invoice", f"ROT-{suffix}", "order-created", datetime.now(timezone.utc)).payload(), {"X-API-Key": sdk_key["key"]})[0] == 401)
    check("Etkin anahtarı olan bağlantı silinemez", api(f"/connections/{direct['id']}", method="DELETE")[0] == 409)
    check("Yönetim geçmişi yenilemeyi kaydetti", any(a["action"] == "key.rotate" for a in require(api("/operations"))["audit"]))

    require(api("/me/logout", method="POST"))
    print(f"\n{passed} bağlantı kontrolü geçti.")


if __name__ == "__main__":
    main()
