"""V2 reporting checks: overview KPIs, step percentiles, compliance, alarm response times, recurring
problems, groups, data quality, trends, digest settings, on-demand digest e-mail (Mailpit) and the
Python digest command. Creates an isolated test project.
"""
import getpass
import json
import os
import subprocess
import tempfile
import time
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
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
    project = require(request(base, "/api/v1/me/projects", {"name": "Rapor kontrol " + suffix}, session))
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
    require(api("/flows", {"code": "report-check", "name": "Rapor kontrolü", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None}, {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 600}]}))
    key = require(api("/keys", {"name": "Rapor anahtarı", "scope": "ingest"}))["key"]
    require(api("/channels", {"name": "Rapor e-postası", "kind": "email", "target": f"report-{suffix}@local.test"}))
    ingest = {"X-API-Key": key}
    now = datetime.now(timezone.utc)

    def send(tx, step, at, status="completed", group=None):
        require(api("/events", {"eventId": str(uuid.uuid4()), "flow": "report-check", "transactionId": tx, "step": step, "status": status,
            "occurredAt": at.isoformat(), "error": "Fatura servisi zaman aşımı" if status == "failed" else None, "group": group}, ingest), (202,))

    base_t = now - timedelta(hours=3)
    # 4 fast (5 min), 2 slow (30 min, breached), 1 vip fast, 1 failed twice (recurring), 1 out-of-order, 1 late notification
    for i in range(4):
        send(f"R-{suffix}-fast{i}", "start", base_t + timedelta(minutes=i)); send(f"R-{suffix}-fast{i}", "finish", base_t + timedelta(minutes=i + 5))
    for i in range(2):
        send(f"R-{suffix}-slow{i}", "start", base_t); send(f"R-{suffix}-slow{i}", "finish", base_t + timedelta(minutes=30))
    send(f"R-{suffix}-vip", "start", base_t, group="vip"); send(f"R-{suffix}-vip", "finish", base_t + timedelta(minutes=2), group="vip")
    rec = f"R-{suffix}-recurring"
    send(rec, "start", base_t); send(rec, "finish", base_t + timedelta(minutes=1), "failed")
    wait("Hata alarmı açıldı", lambda: require(api("/alarms?flow=report-check"))["total"] >= 1)
    alarm = require(api("/alarms?flow=report-check"))["items"][0]
    require(api(f"/alarms/{alarm['id']}/acknowledge", {}))
    send(rec, "finish", base_t + timedelta(minutes=2))  # resolves
    wait("Hata alarmı kapandı", lambda: require(api("/alarms/" + alarm["id"]))["alarm"]["resolved_at"] is not None)
    send(rec, "finish", base_t + timedelta(minutes=3), "failed")  # second alarm → recurring
    wait("İkinci alarm açıldı", lambda: require(api("/alarms?flow=report-check&state=all"))["total"] == 2)
    send(f"R-{suffix}-ooo", "start", base_t + timedelta(minutes=10)); send(f"R-{suffix}-ooo", "finish", base_t + timedelta(minutes=5))
    send(f"R-{suffix}-late", "start", now - timedelta(minutes=20))  # arrives 20 minutes after it happened
    wait("Olaylar işlendi", lambda: require(api("/transactions/summary?flow=report-check"))["total"] == 10
        and require(api("/transactions/summary?flow=report-check"))["pending"] == 0)

    o = require(api("/reports/overview?flow=report-check"))
    check("Toplamlar: 10 işlem, 8 tamamlanan, 1 hata, 1 devam", (o["totals"]["transactions"], o["totals"]["completed"], o["totals"]["failed"], o["totals"]["in_progress"]) == (10, 8, 1, 1))
    # 9 measured: 4 fast, 2 slow, vip, the recurring one (completed once), the out-of-order one (flagged, not breached).
    check("Süre uyumu: 9 ölçülen adımın 2'si aşım, 1'i tutarsız zaman", o["compliance"]["measured"] == 9 and o["compliance"]["breached"] == 2 and o["compliance"]["out_of_order"] == 1)
    step = next(s for s in o["steps"] if s["step_code"] == "finish")
    check("Adım yüzdelikleri hesaplandı", float(step["p50"]) <= 600 < float(step["p95"]) and step["allowance"] == 600 and float(step["compliance"]) == 77.8)
    # The "late" transaction is 20 minutes into a 10-minute allowance, so one overdue alarm is open as well.
    check("Alarm sayıları ve yanıt süreleri", o["alarms"]["failed"] == 2 and o["alarms"]["overdue"] == 1 and o["alarms"]["still_open"] == 2 and o["alarms"]["acknowledged"] == 1
        and o["alarms"]["mtta_seconds"] is not None and o["alarms"]["mttr_seconds"] is not None)
    check("En sık hata veren adım listelendi", o["topFailed"][0]["step_code"] == "finish" and o["topFailed"][0]["count"] == 2 and "Fatura servisi" in o["topFailed"][0]["error"])
    check("Tekrarlayan sorun listelendi", any(r["business_id"] == rec and r["alarms"] == 2 for r in o["recurring"]))
    check("Grup dağılımı vip grubunu ayırıyor", any(g["group"] == "vip" and g["transactions"] == 1 for g in o["byGroup"]))
    check("Veri kalitesi: geç bildirim ve tutarsız zaman sayıldı", o["quality"]["late_events"] >= 1 and o["quality"]["out_of_order_steps"] == 1)
    check("Geçersiz aralık reddedilir", api("/reports/overview?from=2020-01-01&to=2026-01-01")[0] == 400)
    trend = require(api("/reports/trend?flow=report-check&period=day&buckets=7"))
    check("Günlük eğilim son kovada işlemleri gösteriyor", len(trend["buckets"]) == 7 and trend["buckets"][-1]["transactions"] == 10 and trend["buckets"][-1]["breached"] == 2)
    check("Haftalık eğilim üretiliyor", len(require(api("/reports/trend?flow=report-check&period=week&buckets=4"))["buckets"]) == 4)
    check("Okuma anahtarı raporu görebilir", api("/reports/overview?flow=report-check", auth={"X-API-Key": require(api("/keys", {"name": "r", "scope": "read"}))["key"]})[0] == 200)

    # digest settings + manual send
    check("Geçersiz saat reddedilir", api("/reports/digest", {"period": "daily", "hour": 25, "timeZone": "Europe/Istanbul"}, method="PUT")[0] == 400)
    require(api("/reports/digest", {"period": "weekly", "hour": 8, "timeZone": "Europe/Istanbul"}, method="PUT"))
    settings = require(api("/reports/digest"))["settings"]
    check("Özet ayarları kaydedildi", settings["digest_period"] == "weekly" and settings["digest_hour"] == 8)
    sent = require(api("/reports/digest", {"period": "daily"}))
    check("Özet e-posta kanalına kuyruğa alındı ve önizleme içeriyor", sent["recipients"] == 1 and "Süre uyumu" in sent["preview"] and rec in sent["preview"])

    def mails():
        with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/search?" + urllib.parse.urlencode({"query": f"report-{suffix}@local.test"}), timeout=10) as r:
            return json.loads(r.read())["messages"]
    wait("Özet e-postası Mailpit'e ulaştı", lambda: any("Günlük özet" in m["Subject"] for m in mails()), timeout=60)
    check("Özet geçmişi panel için listeleniyor", any(d["triggered_by"] == "manual" for d in require(api("/reports/digest"))["sent"]))

    # Python digest command through the reconciler CLI
    py = ROOT / "reconciler" / ".venv" / "bin" / "python"
    work = Path(tempfile.mkdtemp(prefix="integraops-digest-"))
    (work / "d.conf").write_text(f"NAME=digest\nAPI_URL={base}\nAPI_KEY={key}\nFLOW=report-check\nSOURCE=digest-only\n")
    result = subprocess.run([str(py if py.exists() else "python3"), "-m", "integraops_reconciler", "--config", str(work / "d.conf"), "--digest", "weekly"], cwd=ROOT / "reconciler", capture_output=True, text=True)
    check("Python özet komutu haftalık özeti gönderdi", result.returncode == 0 and "özet kuyruğa alındı" in result.stdout)
    wait("Haftalık özet e-postası ulaştı", lambda: any("Haftalık özet" in m["Subject"] for m in mails()), timeout=60)
    check("Dışa aktarma denetim kaydı özet gönderimini içeriyor", any(a["action"] == "digest.send" for a in require(api("/operations"))["audit"]))

    require(api("/me/logout", method="POST"))
    print(f"\n{passed} rapor kontrolü geçti.")


if __name__ == "__main__":
    main()
