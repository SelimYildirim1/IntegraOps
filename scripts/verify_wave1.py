"""Dalga 1 checks: Slack and Teams channels with runbook-bearing messages, step instructions and source links on
flows, transactions and alarms, sector templates, sample-data generation and removal, and the public OpenAPI document.
Isolated test project. Requires the local stack with the webhook sample (docker compose up).
"""
import getpass
import os
import time
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:8]
    project = require(request(base, "/api/v1/me/projects", {"name": "Dalga 1 " + suffix}, session))
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

    # --- OpenAPI document (public) ---
    with urllib.request.urlopen(base + "/openapi.yaml", timeout=10) as response:
        spec = response.read().decode()
        check("OpenAPI belgesi YAML olarak sunuluyor", response.status == 200 and "yaml" in response.headers.get("Content-Type", ""))
    check("OpenAPI belgesi olay ve şablon uçlarını içeriyor", "/api/v1/events:" in spec and "/api/v1/flows/from-template:" in spec and "slack" in spec)

    # --- Channels: slack and teams ---
    check("Slack kanalı yalnızca hooks.slack.com ya da izin listesini kabul eder",
        api("/channels", {"name": "Slack yanlış", "kind": "slack", "target": "https://evil.example.com/hook"})[0] == 400)
    check("Teams kanalı rastgele adresi reddeder",
        api("/channels", {"name": "Teams yanlış", "kind": "teams", "target": "https://hooks.slack.com/services/x"})[0] == 400)
    check("Bilinmeyen kanal türü reddedilir", api("/channels", {"name": "x", "kind": "sms", "target": "+90"})[0] == 400)
    # Local stand-ins: the compose stack allows http://webhook:8080 as a private origin.
    require(api("/channels", {"name": "Slack ops", "kind": "slack", "target": "http://webhook:8080/slack"}))
    require(api("/channels", {"name": "Teams ops", "kind": "teams", "target": "http://webhook:8080/teams"}))
    channels = require(api("/channels"))
    check("Slack ve Teams kanalları listede, hedef yalnızca sunucu adıyla gösterilir",
        {c["kind"] for c in channels} == {"slack", "teams"} and all(c["target"] == "http://webhook:8080" for c in channels))

    # --- Templates ---
    catalog = require(api("/flows/catalog"))
    check("Şablon kataloğu 6 sektör şablonu veriyor", len(catalog) == 6 and {t["sector"] for t in catalog} >= {"E-ticaret ve pazar yeri", "Lojistik", "Sigorta"})
    ecommerce = next(t for t in catalog if t["code"] == "ecommerce-order")
    check("Şablon adımları talimat taşıyor", all(s["instructions"] for s in ecommerce["steps"]) and ecommerce["stageCount"] == 6 and "export" in ecommerce["groups"])
    check("Bilinmeyen şablon 404", api("/flows/from-template", {"template": "yok"})[0] == 404)
    created = require(api("/flows/from-template", {"template": "ecommerce-order"}))
    check("Şablondan akış oluşturuldu (1. sürüm yayınlandı)", created["code"] == "ecommerce-order" and created["steps"] == 8)
    check("Aynı kodla ikinci şablon 409", api("/flows/from-template", {"template": "ecommerce-order"})[0] == 409)
    second = require(api("/flows/from-template", {"template": "logistics-delivery", "code": "kargo-" + suffix, "name": "Kargo " + suffix}))
    check("Şablon farklı kod ve adla kopyalanır", second["code"] == "kargo-" + suffix)
    overview = require(api("/flows/ecommerce-order"))
    invoice = next(s for s in overview["steps"] if s["code"] == "invoice-created")
    check("Akış görünümü talimat, aşama ve grup sürelerini veriyor", "e-Fatura" in invoice["instructions"] and invoice["stage"] == 2 and invoice["overrides"] == {"vip": 3600})
    check("Örnek veri sayısı sıfırla başlar", overview["demoCount"] == 0)

    # --- Runbook and link on a draft; link validation ---
    require(api("/flows/ecommerce-order/versions", {}))
    steps = [dict(s, link="") for s in overview["steps"]]
    steps[4]["link"] = "https://erp.example.com/orders/{transactionId}?step={step}"
    steps[4]["instructions"] = "Fatura kuyruğuna bakın; vergi numarasını kontrol edin."
    bad = [dict(s) for s in steps]; bad[4]["link"] = "erp.example.com/{transactionId}"
    check("Bağlantı şablonu tam adres ister", api("/flows/ecommerce-order/versions/2", {"steps": bad}, method="PUT")[0] == 400)
    bad[4]["link"] = "https://erp.example.com/{order}"
    check("Bağlantı şablonu bilinmeyen yer tutucuyu reddeder", api("/flows/ecommerce-order/versions/2", {"steps": bad}, method="PUT")[0] == 400)
    bad[4]["link"] = ""; bad[4]["instructions"] = "x" * 2001
    check("Talimat 2000 karakterle sınırlı", api("/flows/ecommerce-order/versions/2", {"steps": bad}, method="PUT")[0] == 400)
    require(api("/flows/ecommerce-order/versions/2", {"steps": steps, "note": "bağlantı eklendi"}, method="PUT"))
    diff = require(api("/flows/ecommerce-order/versions/1/diff/2"))
    check("Sürüm farkı talimat ve bağlantı değişikliğini listeler",
        any(c["kind"] == "link" and c["code"] == "invoice-created" for c in diff["changes"]) and any(c["kind"] == "instructions" for c in diff["changes"]))
    require(api("/flows/ecommerce-order/versions/2/publish", {}))

    # --- Failed event → alarm with runbook → slack/teams deliveries ---
    key = require(api("/keys", {"name": "Dalga 1 anahtar", "scope": "ingest"}))
    ingest = {"X-API-Key": key["key"]}
    tx = "W1-" + suffix
    now = datetime.now(timezone.utc)
    def event(step, status="completed", minutes_ago=0, error=None):
        return {"eventId": str(uuid.uuid4()), "flow": "ecommerce-order", "transactionId": tx, "step": step, "status": status,
            "occurredAt": (now - timedelta(minutes=minutes_ago)).isoformat(), "error": error}
    require(api("/events", event("order-created", minutes_ago=10), ingest), (202,))
    require(api("/events", event("payment-completed", minutes_ago=9), ingest), (202,))
    require(api("/events", event("stock-reserved", minutes_ago=8), ingest), (202,))
    require(api("/events", event("invoice-created", "failed", 1, "e-Fatura reddedildi: alıcı vergi numarası geçersiz"), ingest), (202,))
    detail = lambda: require(api("/flows/ecommerce-order/transactions/" + tx))
    wait("İşlem detayı adım bağlantısını işlem numarasıyla dolduruyor",
        lambda: next((s for s in detail()["steps"] if s["code"] == "invoice-created"), {}).get("link") == f"https://erp.example.com/orders/{tx}?step=invoice-created")
    check("İşlem detayı adım talimatını taşıyor", "vergi numarasını" in next(s for s in detail()["steps"] if s["code"] == "invoice-created")["instructions"])
    wait("Hata alarmı açıldı", lambda: any(a["business_id"] == tx and a["kind"] == "failed" for a in require(api("/alarms?flow=ecommerce-order&state=open"))["items"]))
    alarm_id = next(a["id"] for a in require(api("/alarms?flow=ecommerce-order&state=open"))["items"] if a["business_id"] == tx)
    alarm = require(api("/alarms/" + alarm_id))["alarm"]
    check("Alarm detayı adım adı, talimat ve kaynak bağlantısını veriyor",
        alarm["step_name"] == "Fatura kesildi" and "vergi" in alarm["instructions"] and alarm["link"].endswith(f"/orders/{tx}?step=invoice-created"))
    wait("Slack ve Teams gönderimleri tamamlandı",
        lambda: (d := require(api("/alarms/" + alarm_id))["deliveries"]) and len(d) == 2 and all(x["state"] == "sent" for x in d))
    chat = require(request("http://127.0.0.1:8082", "/chat"))
    delivery_ids = {d["id"] for d in require(api("/alarms/" + alarm_id))["deliveries"]}
    mine = [m for m in chat if m["id"].split(":", 1)[1] in delivery_ids]
    slack = next(m["body"] for m in mine if m["kind"] == "slack")
    teams = next(m["body"] for m in mine if m["kind"] == "teams")
    slack_text = str(slack)
    check("Slack iletisi Block Kit biçiminde ve talimatı taşıyor",
        slack["blocks"][0]["type"] == "header" and "vergi numarasını" in slack_text and tx in slack["text"])
    buttons = [e["url"] for b in slack["blocks"] if b["type"] == "actions" for e in b["elements"]]
    check("Slack iletisinde panel ve kaynak sistem düğmeleri var",
        any(u.endswith("/alarms/" + alarm_id) for u in buttons) and any(u.endswith(f"/orders/{tx}?step=invoice-created") for u in buttons))
    card = teams["attachments"][0]["content"]
    check("Teams iletisi Adaptive Card ve hata metnini taşıyor",
        card["type"] == "AdaptiveCard" and "vergi numarası geçersiz" in str(card["body"]) and any(a["title"] == "Kaynak sistemde aç" for a in card["actions"]))
    require(api("/events", event("invoice-created"), ingest), (202,))
    wait("Düzelen adım alarmı kapattı; kapanış iletileri de gitti",
        lambda: require(api("/alarms/" + alarm_id))["alarm"]["resolved_at"] is not None and len(require(api("/alarms/" + alarm_id))["deliveries"]) == 4)

    # --- Sample data ---
    check("Örnek veri sınırları doğrulanır", api("/flows/kargo-" + suffix + "/demo", {"count": 500})[0] == 400 and api("/flows/kargo-" + suffix + "/demo", {"days": 0})[0] == 400)
    check("Örnek veri bilinmeyen akışta 404", api("/flows/yok-" + suffix + "/demo", {"count": 5})[0] == 404)
    demo = require(api("/flows/kargo-" + suffix + "/demo", {"count": 40, "days": 7}))
    check("40 örnek işlem üretildi", demo["transactions"] == 40 and demo["events"] >= 40)
    check("Akış görünümü örnek işlem sayısını gösterir", require(api("/flows/kargo-" + suffix))["demoCount"] == 40)
    summary = lambda: require(api("/transactions/summary?flow=kargo-" + suffix))
    wait("Örnek işlemler işlendi ve gerçek veri olarak sayıldı", lambda: summary()["total"] == 40 and summary()["pending"] == 0, timeout=120)
    s = summary()
    check("Örnek veri karışık durumlar içeriyor", s["completed"] > 0 and (s["inProgress"] + s["failed"] + s["cancelled"]) > 0)
    listing = require(api("/transactions?flow=kargo-" + suffix + "&pageSize=50"))
    check("Örnek işlemler DEMO- ile başlar ve test verisi sayılmaz", all(t["transactionId"].startswith("DEMO-") and not t["isTest"] for t in listing["items"]))
    week_ago = (now - timedelta(days=8)).strftime("%Y-%m-%d")
    spread = require(api(f"/transactions?flow=kargo-{suffix}&from={week_ago}&to={(now - timedelta(days=1)).strftime('%Y-%m-%d')}&pageSize=50"))
    check("Oluşturma zamanı döneme yayılmış (önceki günlerde işlem var)", spread["totalCount"] > 0)
    wait("Örnek veri süre aşımı ya da hata alarmı üretti", lambda: require(api("/alarms?flow=kargo-" + suffix + "&state=all"))["total"] > 0, timeout=60)
    report = require(api("/reports/overview?flow=kargo-" + suffix + "&from=" + (now - timedelta(days=8)).strftime("%Y-%m-%d")))
    check("Raporlar örnek veriyle doluyor", report["totals"]["transactions"] == 40 and report["compliance"]["measured"] > 0)
    removed = require(api("/flows/kargo-" + suffix + "/demo", method="DELETE"))
    check("Örnek veri tek çağrıyla silindi", removed["transactions"] == 40 and summary()["total"] == 0
        and require(api("/alarms?flow=kargo-" + suffix + "&state=all"))["total"] == 0)
    check("Gerçek işlem silinmedi", detail()["status"] in ("inProgress", "completed"))

    # --- Roles ---
    viewer_password = uuid.uuid4().hex + "Aa1"
    require(api("/members", {"email": f"viewer-{suffix}@local.test", "name": "İzleyici", "role": "viewer", "password": viewer_password}))
    viewer = {"Authorization": "Bearer " + require(request(base, "/api/v1/auth/login", {"email": f"viewer-{suffix}@local.test", "password": viewer_password}))["token"], "X-Project-Id": project["id"]}
    check("İzleyici kataloğu görür ama şablondan akış oluşturamaz ve örnek veri üretemez",
        api("/flows/catalog", auth=viewer)[0] == 200 and api("/flows/from-template", {"template": "insurance-claim"}, viewer)[0] == 403
        and api("/flows/ecommerce-order/demo", {"count": 1}, viewer)[0] == 403)
    check("Gönderim anahtarı şablon ya da örnek veri ucunu kullanamaz", api("/flows/from-template", {"template": "insurance-claim"}, ingest)[0] == 403)

    audit = require(api("/operations"))["audit"]
    check("Denetim kaydı şablon, örnek veri ve kanal işlemlerini tutar",
        {a["action"] for a in audit} >= {"flow.template", "demo.generate", "demo.delete", "channel.create"})
    print(f"\nTamamlandı: {passed} kontrol geçti.")


if __name__ == "__main__":
    main()
