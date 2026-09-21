"""Collective local V1 test; creates an isolated test project. No third-party services are contacted."""
import argparse
import getpass
import json
import os
import secrets
import subprocess
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone, timedelta
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--load", type=int, default=25, help="Number of two-step transactions, 1–500")
    parser.add_argument("--faults", action="store_true", help="Temporarily stop local API and RabbitMQ, then start them again")
    args = parser.parse_args()
    if not 1 <= args.load <= 500:
        raise SystemExit("--load 1–500 arasında olmalı.")
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:10]
    project = require(request(base, "/api/v1/me/projects", {"name": "V1 toplu test " + suffix}, session))
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
    flow = {"code": "v1-check", "name": "Toplu kontrol", "steps": [
        {"code": "erp-created", "name": "ERP", "maxDelaySeconds": None},
        {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 2}]}
    require(api("/flows", flow)); check("Aynı akış yeniden oluşturulamaz", api("/flows", flow)[0] == 409)
    for name, kind, target in [("Yerel e-posta", "email", "test@local.test"), ("Yerel webhook", "webhook", "http://webhook:8080/hook")]:
        require(api("/channels", {"name": name, "kind": kind, "target": target, "secret": env["LOCAL_WEBHOOK_SECRET"] if kind == "webhook" else None}))
    key = require(api("/keys", {"name": "Test gönderimi", "scope": "ingest"}))
    read = require(api("/keys", {"name": "Test okuma", "scope": "read"}))
    ingest = {"X-API-Key": key["key"]}; readonly = {"X-API-Key": read["key"]}
    check("Gönderim anahtarı akışları okuyamaz", api("/flows", auth=ingest)[0] == 403)
    check("Okuma anahtarı akışları okuyabilir", api("/flows", auth=readonly)[0] == 200)
    temporary_password = secrets.token_urlsafe(24)
    roles = {}
    for role in ("viewer", "operator"):
        address = f"{role}-{suffix}@local.test"
        require(api("/members", {"email": address, "name": "Test " + role, "role": role, "password": temporary_password}))
        user_token = require(request(base, "/api/v1/auth/login", {"email": address, "password": temporary_password}))["token"]
        roles[role] = {"Authorization": "Bearer " + user_token, "X-Project-Id": project["id"]}
    check("İzleyici akış oluşturamaz", api("/flows", flow, roles["viewer"])[0] == 403)
    check("Operatör kullanıcı yönetemez", api("/members", auth=roles["operator"])[0] == 403)
    me = require(api("/me/user"))
    check("Son proje sahibi kaldırılamaz", api("/members/" + me["id"], method="DELETE")[0] == 409)
    check("Özel ağdaki izinsiz webhook reddedilir", api("/channels", {"name": "Reddedilecek", "kind": "webhook", "target": "http://169.254.169.254/", "secret": env["LOCAL_WEBHOOK_SECRET"]})[0] == 400)
    require(request("http://127.0.0.1:8082", "/test/fail-next", {"count": 1}))
    def event(tx, step, status="completed", age=0):
        return {"eventId": str(uuid.uuid4()), "flow": "v1-check", "transactionId": tx, "step": step,
            "status": status, "occurredAt": (datetime.now(timezone.utc) - timedelta(seconds=age)).isoformat(),
            "error": "Yerel fatura hatası" if status == "failed" else None}
    first = event("V1-DELAY-" + suffix, "erp-created", age=5)
    check("Okuma anahtarı bildirim gönderemez", api("/events", first, readonly)[0] == 403)
    check("Bildirim kabul edildi", api("/events", first, ingest)[0] == 202)
    check("Aynı eventId ve içerik tekrar kaydedilmez", require(api("/events", first, ingest))["duplicate"] is True)
    check("Aynı eventId farklı içerikle reddedilir", api("/events", first | {"step": "invoice-created"}, ingest)[0] == 409)
    def detail(tx): return require(api("/flows/v1-check/transactions/" + tx))
    wait("Süre aşımı ayrı gecikme durumudur", lambda: detail(first["transactionId"])["delay"]["state"] == "overdue")
    check("Gecikme işlemi failed yapmaz", detail(first["transactionId"])["status"] == "inProgress")
    alarms = require(api("/alarms?flow=v1-check&state=open"))["items"]
    check("Bir açık gecikme alarmı oluştu", len(alarms) == 1)
    aid = alarms[0]["id"]
    require(api(f"/alarms/{aid}/acknowledge", {}, roles["operator"]))
    require(api(f"/alarms/{aid}/acknowledge", {}, roles["operator"]))
    alarm = require(api("/alarms/" + aid))
    check("Gördüm alarmı kapatmaz ve tekrarlanmaz", alarm["alarm"]["resolved_at"] is None and sum(e["kind"] == "acknowledged" for e in alarm["events"]) == 1)
    require(api("/events", event(first["transactionId"], "invoice-created"), ingest), (202,))
    wait("Geç tamamlanma alarmı kapatır", lambda: require(api("/alarms/" + aid))["alarm"]["resolution"] == "step_completed")
    wait("Açılış ve kapanış iki kanala gönderildi", lambda: len((d := require(api("/alarms/" + aid))["deliveries"])) == 4 and all(x["state"] == "sent" for x in d))
    check("Webhook başarısız gönderimden sonra tekrar denendi", any(d["attempts"] >= 2 for d in require(api("/alarms/" + aid))["deliveries"]))
    failure = event("V1-FAIL-" + suffix, "invoice-created", "failed")
    require(api("/events", failure, ingest), (202,))
    wait("Hata bildirimi failed alarmı açtı", lambda: any(a["kind"] == "failed" for a in require(api("/alarms?flow=v1-check&state=open"))["items"]))
    require(api("/events", event(failure["transactionId"], "invoice-created"), ingest), (202,))
    wait("Düzelen adımın hata alarmı kapandı", lambda: not any(a["kind"] == "failed" for a in require(api("/alarms?flow=v1-check&state=open"))["items"]))
    # Invoice arriving before ERP is a valid delivery order.
    reversed_tx = "V1-REVERSE-" + suffix
    require(api("/events", event(reversed_tx, "invoice-created"), ingest), (202,))
    require(api("/events", event(reversed_tx, "erp-created", age=1), ingest), (202,))
    wait("Ters sırada ulaşan bildirimler tamamlandı", lambda: detail(reversed_tx)["status"] == "completed")
    foreign = require(api("/me/projects", {"name": "V1 izolasyon " + suffix}))
    check("İzleyici diğer projeye erişemez", api("/flows", auth=roles["viewer"] | {"X-Project-Id": foreign["id"]})[0] == 403)
    check("Alarm kimliği diğer projede görünmez", api("/alarms/" + aid, auth=session | {"X-Project-Id": foreign["id"]})[0] == 404)
    bulk = flow | {"code": "v1-load", "name": "Yerel yük denemesi", "steps": [flow["steps"][0], flow["steps"][1] | {"maxDelaySeconds": 86400}]}
    require(api("/flows", bulk))
    def send_pair(i):
        for step in ("erp-created", "invoice-created"):
            require(api("/events", event(f"V1-LOAD-{suffix}-{i}", step) | {"flow": "v1-load"}, ingest), (202,))
    started = time.monotonic()
    with ThreadPoolExecutor(max_workers=4) as pool: list(pool.map(send_pair, range(args.load)))
    print(f"{args.load * 2} bildirim kabulü: {time.monotonic() - started:.2f} sn (bu bilgisayardaki ölçüm)")
    wait("Toplu işlemler tamamlandı", lambda: require(api("/transactions/summary?flow=v1-load"))["completed"] == args.load, timeout=180)
    check("Sayfalama sınırı çalışıyor", len(require(api("/transactions?flow=v1-load&page=1&pageSize=10"))["items"]) == min(10, args.load))
    require(api("/keys/" + key["id"], method="DELETE"))
    check("İptal edilen anahtar çalışmıyor", api("/events", first, ingest)[0] == 401)
    source_project = env.get("PILOT_PROJECT_ID")
    if args.faults and not source_project:
        raise SystemExit("Kesinti testi için önce setup_v1.py ve kaynak örneği kurulmalı.")
    if source_project:
        source_headers = session | {"X-Project-Id": source_project}
        tx = "DEMO-" + suffix
        require(request("http://127.0.0.1:8081", "/orders", {"transactionId": tx}))
        require(request("http://127.0.0.1:8081", "/orders/" + tx + "/invoice", {}))
        wait("Kaynak .NET uygulaması uçtan uca tamamlandı", lambda: api(f"/flows/pilot-source/transactions/{tx}", auth=source_headers)[1].get("status") == "completed")
    if args.faults:
        def compose(*parts): subprocess.run(["docker", "compose", *parts], cwd=ROOT, check=True, stdout=subprocess.DEVNULL)
        tx = "DEMO-OFFLINE-" + suffix
        try:
            compose("stop", "api")
            require(request("http://127.0.0.1:8081", "/orders", {"transactionId": tx}))
            check("IntegraOps kapalıyken kaynak sipariş kaydı sürüyor", True)
        finally: compose("start", "api")
        def ready():
            try: return request(base, "/health/ready")[0] == 200
            except OSError: return False
        wait("API yeniden hazır", ready)
        wait("Kaynak kuyruğu bağlantı gelince aktarır", lambda: api(f"/flows/pilot-source/transactions/{tx}", auth=source_headers)[0] == 200, timeout=150)
        try:
            compose("stop", "rabbitmq")
            event1 = event("V1-BROKER-" + suffix, "erp-created") | {"flow": "v1-load"}
            require(api("/events", event1), (202,))
            wait("RabbitMQ kapalıyken API bildirimi veritabanında tutar", lambda: require(api("/operations"))["counts"]["outbox_pending"] > 0)
        finally: compose("start", "rabbitmq")
        wait("RabbitMQ dönüşünde bekleyen bildirim işlenir", lambda: require(api("/flows/v1-load/transactions/" + event1["transactionId"]))["processedEventCount"] == 1, timeout=180)
    require(api("/me/logout", method="POST"))
    check("Çıkış oturumu geçersiz kılar", api("/flows")[0] == 401)
    print(f"\n{passed} kontrol geçti. Panelde '{project['name']}' projesini seçerek sonuçları inceleyin.")

if __name__ == "__main__":
    main()
