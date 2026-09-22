"""V1.1 daily-operation checks against the local stack: search, date filters, saved views,
test-data separation, alarm assignee and notes, e-mail invitation, password reset, retention
and CSV export. Creates an isolated test project; e-mails are read from the local Mailpit.

Retention is checked by back-dating one finished transaction directly in the local database
(docker compose exec), because the API never lets a client change timestamps.
"""
import getpass
import json
import os
import re
import subprocess
import time
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

MAILPIT = "http://127.0.0.1:8025"


def mailpit_search(query):
    with urllib.request.urlopen(MAILPIT + "/api/v1/search?" + urllib.parse.urlencode({"query": query}), timeout=10) as r:
        return json.loads(r.read())["messages"]


def mailpit_text(message_id):
    with urllib.request.urlopen(f"{MAILPIT}/api/v1/message/{message_id}", timeout=10) as r:
        return json.loads(r.read())["Text"]


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:10]
    project = require(request(base, "/api/v1/me/projects", {"name": "V1.1 kontrol " + suffix}, session))
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

    def raw_get(path, auth):
        req = urllib.request.Request(base + "/api/v1" + path, headers=auth)
        try:
            with urllib.request.urlopen(req, timeout=20) as r:
                return r.status, r.read().decode("utf-8-sig"), r.headers.get("Content-Type", "")
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(), ""

    print("Test projesi:", project["name"], project["id"])
    flow = {"code": "ops-check", "name": "Operasyon kontrolü", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
        {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 86400}]}
    require(api("/flows", flow))
    key = require(api("/keys", {"name": "Ops gönderim", "scope": "ingest"}))
    ingest = {"X-API-Key": key["key"]}

    def send(tx, step, status="completed"):
        require(api("/events", {"eventId": str(uuid.uuid4()), "flow": "ops-check", "transactionId": tx, "step": step,
            "status": status, "occurredAt": datetime.now(timezone.utc).isoformat(),
            "error": "Kontrol hatası" if status == "failed" else None}, ingest), (202,))

    real = [f"ORDER-{suffix}-{i}" for i in range(3)]
    for tx in real:
        send(tx, "start"); send(tx, "finish")
    send(f"TEST-{uuid.uuid4()}", "start")
    send(f"INV-{suffix}", "start")  # stays in progress
    wait("Olaylar işlendi", lambda: require(api("/transactions/summary?flow=ops-check&test=include"))["total"] == 5)

    # --- search, dates, test separation ---
    found = require(api("/transactions?" + urllib.parse.urlencode({"search": f"order-{suffix}-1"})))
    check("İşlem numarasıyla arama büyük/küçük harf duyarsız çalışıyor", found["totalCount"] == 1 and found["items"][0]["transactionId"] == real[1])
    check("Arama joker karakterleri düz metin sayar", require(api("/transactions?search=%25"))["totalCount"] == 0)
    today = datetime.now(timezone.utc).date()
    tomorrow = (today + timedelta(days=1)).isoformat()
    check("Tarih aralığı bugünü kapsıyor", require(api(f"/transactions?flow=ops-check&from={today}&to={tomorrow}"))["totalCount"] == 4)
    check("Yarından başlayan aralık boş", require(api(f"/transactions?flow=ops-check&from={tomorrow}"))["totalCount"] == 0)
    check("Geçersiz tarih reddedilir", api("/transactions?from=2026-13-40")[0] == 400)
    summary = require(api("/transactions/summary?flow=ops-check"))
    check("Özet test verisini varsayılan olarak dışlıyor", summary["total"] == 4)
    check("test=only yalnızca TEST- kayıtlarını verir", require(api("/transactions?flow=ops-check&test=only"))["totalCount"] == 1
        and require(api("/transactions?flow=ops-check&test=only"))["items"][0]["isTest"] is True)
    check("test=include hepsini verir", require(api("/transactions?flow=ops-check&test=include"))["totalCount"] == 5)

    # --- saved views ---
    view = require(api("/views", {"kind": "transactions", "name": "Bugünkü siparişler", "query": f"?flow=ops-check&from={today}"}))
    check("Görünüm kaydedildi ve soru işareti temizlendi", view["query"].startswith("flow="))
    views = require(api("/views?kind=transactions"))
    check("Görünüm listede", any(v["id"] == view["id"] for v in views))
    check("Görünüm türü doğrulanır", api("/views", {"kind": "x", "name": "a", "query": ""})[0] == 400)
    require(api("/views/" + view["id"], method="DELETE"))
    check("Görünüm silindi", not any(v["id"] == view["id"] for v in require(api("/views?kind=transactions"))))

    # --- alarm assignee and notes ---
    failing = f"ORDER-{suffix}-fail"
    send(failing, "start"); send(failing, "finish", "failed")
    wait("Hata alarmı açıldı", lambda: require(api("/alarms?flow=ops-check"))["total"] == 1)
    alarm = require(api("/alarms?flow=ops-check"))["items"][0]
    me = require(api("/me/user"))
    require(api(f"/alarms/{alarm['id']}/assign", {"userId": "me"}))
    detail = require(api("/alarms/" + alarm["id"]))
    check("Alarm sorumlusu atandı ve geçmişe yazıldı", detail["alarm"]["assigned_to"] == me["id"] and any(e["kind"] == "assigned" for e in detail["events"]))
    check("Sorumlu filtresi 'bendekiler' çalışıyor", require(api("/alarms?flow=ops-check&assigned=me"))["total"] == 1)
    check("Sorumlusu olmayan filtresi boş", require(api("/alarms?flow=ops-check&assigned=none"))["total"] == 0)
    viewer_pass = "viewer-" + uuid.uuid4().hex
    viewer_mail = f"viewer-{suffix}@local.test"
    require(api("/members", {"email": viewer_mail, "name": "İzleyici", "role": "viewer", "password": viewer_pass}))
    viewer_id = next(m["id"] for m in require(api("/members")) if m["email"] == viewer_mail)
    check("İzleyici sorumlu olamaz", api(f"/alarms/{alarm['id']}/assign", {"userId": viewer_id})[0] == 400)
    note = require(api(f"/alarms/{alarm['id']}/notes", {"body": "Kaynak ekibiyle görüşüldü; fatura servisi yeniden başlatıldı."}))
    detail = require(api("/alarms/" + alarm["id"]))
    check("Not eklendi ve yazarıyla listelendi", any(n["id"] == note["id"] and n["author"] == me["name"] for n in detail["notes"]))
    check("Boş not reddedilir", api(f"/alarms/{alarm['id']}/notes", {"body": "   "})[0] == 400)
    viewer_token = require(request(base, "/api/v1/auth/login", {"email": viewer_mail, "password": viewer_pass}))["token"]
    viewer_headers = {"Authorization": "Bearer " + viewer_token, "X-Project-Id": project["id"]}
    check("İzleyici not ekleyemez", api(f"/alarms/{alarm['id']}/notes", {"body": "x"}, viewer_headers)[0] == 403)
    check("Not ve atama bildirim üretmez", all(d["event"] in ("opened", "resolved") for d in detail["deliveries"]))
    require(api(f"/alarms/{alarm['id']}/assign", {"userId": ""}))
    check("Sorumlu kaldırıldı", require(api("/alarms/" + alarm["id"]))["alarm"]["assigned_to"] is None)

    # --- invitation ---
    invitee = f"invite-{suffix}@local.test"
    invite = require(api("/members/invite", {"email": invitee, "name": "Davetli Kullanıcı", "role": "operator"}))
    check("Davet oluşturuldu", invite["invited"] is True)
    check("Davetli hesap etkinleşmeden giriş yapamaz", request(base, "/api/v1/auth/login", {"email": invitee, "password": "anything-at-all-12"})[0] == 401)
    wait("Davet e-postası Mailpit'e ulaştı", lambda: len(mailpit_search(invitee)) > 0, timeout=60)
    text = mailpit_text(mailpit_search(invitee)[0]["ID"])
    invite_token = re.search(r"/invite/([0-9a-f]{64})", text).group(1)
    info = require(request(base, "/api/v1/auth/tokens/" + invite_token))
    check("Davet bağlantısı doğrulandı", info["kind"] == "invite" and info["email"] == invitee)
    check("Kısa parola reddedilir", request(base, "/api/v1/auth/invitations/" + invite_token, {"password": "short"})[0] == 400)
    new_pass = "invited-" + uuid.uuid4().hex[:16]
    require(request(base, "/api/v1/auth/invitations/" + invite_token, {"password": new_pass}))
    check("Davet bağlantısı bir kez kullanılır", request(base, "/api/v1/auth/tokens/" + invite_token)[0] == 404)
    invited_token = require(request(base, "/api/v1/auth/login", {"email": invitee, "password": new_pass}))["token"]
    invited_headers = {"Authorization": "Bearer " + invited_token, "X-Project-Id": project["id"]}
    check("Davetli operatör olarak projeye erişiyor", require(api("/access", auth=invited_headers))["role"] == "operator")
    check("Davetli sorumlu listesinde", any(a["id"] == invite["userId"] for a in require(api("/members/assignable"))))
    check("Mevcut hesabı davet etmek projeye ekler, yeni hesap açmaz",
        require(api("/members/invite", {"email": invitee, "name": "x", "role": "viewer"}))["invited"] is False)

    # --- password reset ---
    require(request(base, "/api/v1/auth/password-reset", {"email": invitee}), (202,))
    check("Bilinmeyen adres için de aynı yanıt", request(base, "/api/v1/auth/password-reset", {"email": f"nobody-{suffix}@local.test"})[0] == 202)
    wait("Sıfırlama e-postası ulaştı", lambda: any("Parola" in m["Subject"] for m in mailpit_search(invitee)), timeout=60)
    reset_mail = next(m for m in mailpit_search(invitee) if "Parola" in m["Subject"])
    reset_token = re.search(r"/reset/([0-9a-f]{64})", mailpit_text(reset_mail["ID"])).group(1)
    check("Sıfırlama bağlantısı davet ucunda geçmez", request(base, "/api/v1/auth/invitations/" + reset_token, {"password": "another-pass-123"})[0] == 404)
    reset_pass = "reset-" + uuid.uuid4().hex[:16]
    require(request(base, "/api/v1/auth/password-reset/" + reset_token, {"password": reset_pass}))
    check("Eski oturum sıfırlamadan sonra geçersiz", api("/access", auth=invited_headers)[0] == 401)
    check("Yeni parolayla giriş", request(base, "/api/v1/auth/login", {"email": invitee, "password": reset_pass})[0] == 200)

    # --- export ---
    status, csv, ctype = raw_get("/transactions/export?flow=ops-check", headers)
    lines = csv.splitlines()
    check("İşlem CSV'si başlık ve 5 gerçek kayıt içeriyor", status == 200 and "text/csv" in ctype and lines[0].startswith("islem,akis") and len(lines) == 6)
    check("CSV test verisini varsayılan olarak dışlıyor", not any("TEST-" in line for line in lines))
    status, csv, _ = raw_get("/alarms/export?flow=ops-check&state=all", headers)
    check("Alarm CSV'si hata alarmını içeriyor", status == 200 and failing in csv and "Kontrol hatası" in csv)
    check("İzleyici dışa aktaramaz", raw_get("/transactions/export", viewer_headers)[0] == 403)
    check("Dışa aktarma denetim kaydına yazılır", any(a["action"] == "export.transactions" for a in require(api("/operations"))["audit"]))

    # --- retention ---
    check("Kısa saklama süresi reddedilir", api("/retention", {"finishedTransactionDays": 3, "notificationDays": None, "auditDays": None}, method="PUT")[0] == 400)
    require(api("/retention", {"finishedTransactionDays": 7, "notificationDays": 7, "auditDays": 30}, method="PUT"))
    policy = require(api("/retention"))["policy"]
    check("Saklama kuralı kaydedildi", policy["finished_transaction_days"] == 7 and policy["audit_days"] == 30)
    old = real[0]
    subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "integraops", "-d", "integraops", "-v", "ON_ERROR_STOP=1", "-q", "-c",
        f"UPDATE transactions SET updated_at = updated_at - interval '10 days' WHERE project_id = '{project['id']}' AND business_id = '{old}'"],
        cwd=ROOT, check=True, stdout=subprocess.DEVNULL)
    subprocess.run(["docker", "compose", "restart", "worker"], cwd=ROOT, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    wait("Süresi dolan tamamlanmış işlem silindi", lambda: api("/flows/ops-check/transactions/" + old)[0] == 404, timeout=120)
    check("Süresi dolmayan işlemler duruyor", api("/flows/ops-check/transactions/" + real[1])[0] == 200)
    check("Açık alarmlı işlem korunuyor", api("/flows/ops-check/transactions/" + failing)[0] == 200)
    wait("Süpürme kaydı listelendi", lambda: any(r["transactions_deleted"] >= 1 for r in require(api("/retention"))["runs"]), timeout=30)

    require(api("/keys/" + key["id"], method="DELETE"))
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} V1.1 kontrolü geçti. Panelde '{project['name']}' projesini seçerek sonuçları inceleyin.")


if __name__ == "__main__":
    main()
