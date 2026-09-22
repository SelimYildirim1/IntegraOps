"""V1.2 checks against the local stack: flow drafts and versions, effective time, version pinning of
in-progress transactions, version diff, business calendars, group allowances and the breach report.
Creates an isolated test project.
"""
import getpass
import os
import time
import urllib.parse
import uuid
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:10]
    project = require(request(base, "/api/v1/me/projects", {"name": "V1.2 kontrol " + suffix}, session))
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
    require(api("/flows", {"code": "ver-check", "name": "Sürüm kontrolü", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
        {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 3600}]}))
    key = require(api("/keys", {"name": "Sürüm gönderim", "scope": "ingest"}))
    ingest = {"X-API-Key": key["key"]}
    now = datetime.now(timezone.utc)

    def send(tx, step, at=None, group=None, flow="ver-check"):
        return api("/events", {"eventId": str(uuid.uuid4()), "flow": flow, "transactionId": tx, "step": step, "status": "completed",
            "occurredAt": (at or datetime.now(timezone.utc)).isoformat(), "group": group}, ingest)

    def detail(tx):
        return require(api("/flows/ver-check/transactions/" + tx))

    overview = require(api("/flows/ver-check"))
    check("Yeni akış 1. sürüm olarak yayınlandı", overview["version"] == 1 and overview["versions"][0]["state"] == "published" and overview["versions"][0]["effective"])
    old_tx = f"OLD-{suffix}"
    require(send(old_tx, "start"), (202,))
    wait("1. sürümde işlem başladı", lambda: detail(old_tx)["flowVersion"] == 1)

    # --- calendar: Mon-Fri 09:00-18:00 Istanbul, one holiday tomorrow if tomorrow is a weekday ---
    ist = ZoneInfo("Europe/Istanbul")
    cal = require(api("/calendars", {"name": "Mesai " + suffix, "timeZone": "Europe/Istanbul", "workDays": [1, 2, 3, 4, 5],
        "startTime": "09:00", "endTime": "18:00", "holidays": [{"day": "2026-10-29", "name": "Cumhuriyet Bayramı"}]}))
    check("Takvim oluşturuldu", "id" in cal)
    check("Geçersiz saat dilimi reddedilir", api("/calendars", {"name": "x", "timeZone": "Mars/Olympus", "workDays": [1], "startTime": "09:00", "endTime": "18:00"})[0] == 400)
    calendars = require(api("/calendars"))
    check("Takvim listesi tatili içeriyor", any(c["id"] == cal["id"] and c["holidays"][0]["day"] == "2026-10-29" for c in calendars))

    # --- draft: new step, longer allowance, vip override, calendar ---
    draft = require(api("/flows/ver-check/versions", {}))
    check("Taslak 2. sürüm olarak açıldı", draft["version"] == 2)
    check("İkinci taslak açılamaz", api("/flows/ver-check/versions", {})[0] == 409)
    steps = [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
        {"code": "review", "name": "İnceleme", "maxDelaySeconds": 7200, "overrides": {"vip": 60}},
        {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 5400}]
    check("Yayınlanmış sürüm düzenlenemez", api("/flows/ver-check/versions/1", {"steps": steps}, method="PUT")[0] == 409)
    check("Geçersiz grup kodu reddedilir", api("/flows/ver-check/versions/2", {"steps": steps[:2] + [{"code": "finish", "name": "B", "maxDelaySeconds": 10, "overrides": {"VIP!": 5}}]}, method="PUT")[0] == 400)
    require(api("/flows/ver-check/versions/2", {"name": "Sürüm kontrolü v2", "note": "İnceleme adımı eklendi", "calendarId": cal["id"], "steps": steps}, method="PUT"))
    v2 = require(api("/flows/ver-check/versions/2"))
    check("Taslak kaydedildi: adımlar, takvim, not", v2["state"] == "draft" and len(v2["steps"]) == 3 and v2["calendarId"] == cal["id"] and v2["steps"][1]["overrides"] == {"vip": 60})
    check("Taslak etkin değil; yeni işlem 1. sürümde", require(api("/flows/ver-check"))["version"] == 1)
    diff = require(api("/flows/ver-check/versions/1/diff/2"))
    kinds = {(c["kind"], c["code"]) for c in diff["changes"]}
    check("Sürüm farkı eklenen adımı, süre ve takvim değişikliğini gösteriyor",
        ("added", "review") in kinds and ("allowance", "finish") in kinds and ("calendar", "-") in kinds)
    published = require(api("/flows/ver-check/versions/2/publish", {}))
    check("2. sürüm hemen yayınlandı", published["version"] == 2)
    overview = require(api("/flows/ver-check"))
    check("Etkin sürüm 2, 1. sürüm eski", overview["version"] == 2 and not overview["hasDraft"] and [v["effective"] for v in overview["versions"]] == [True, False])
    check("Akış listesi etkin sürümün adım sayısını verir", next(f for f in require(api("/flows")) if f["code"] == "ver-check")["stepCount"] == 3)

    # --- pinning ---
    check("Devam eden işlem 1. sürümde kaldı", detail(old_tx)["flowVersion"] == 1 and len(detail(old_tx)["steps"]) == 2)
    check("Eski sürüme yeni sürümün adımı gönderilemez", send(old_tx, "review")[0] == 400)
    require(send(old_tx, "finish"), (202,))
    wait("Eski işlem kendi sürümüyle tamamlandı", lambda: detail(old_tx)["status"] == "completed")
    new_tx = f"NEW-{suffix}"
    require(send(new_tx, "start", now - timedelta(minutes=1)), (202,))
    wait("Yeni işlem 2. sürüme bağlandı ve işlendi", lambda: (x := detail(new_tx))["flowVersion"] == 2 and x["calendar"] is not None
        and x["processedEventCount"] == 1 and x["delay"]["step"] == "review")
    d = detail(new_tx)
    check("Yeni işlem 3 adım ve inceleme adımını bekliyor", len(d["steps"]) == 3 and d["delay"]["step"] == "review")

    # --- calendar deadline: the same rule computed independently in Python ---
    def add_working(start, seconds):
        local = start.astimezone(ist); remaining = seconds; day = local.date()
        for _ in range(400):
            if day.weekday() < 5 and day.isoformat() != "2026-10-29":
                opening = datetime.combine(day, datetime.min.time().replace(hour=9), tzinfo=ist)
                closing = datetime.combine(day, datetime.min.time().replace(hour=18), tzinfo=ist)
                cursor = max(local, opening)
                if cursor < closing:
                    available = (closing - cursor).total_seconds()
                    if remaining <= available:
                        return cursor + timedelta(seconds=remaining)
                    remaining -= available; local = closing
            day += timedelta(days=1)
        raise RuntimeError("takvim")
    started = datetime.fromisoformat(d["delay"]["startedAt"])
    deadline = datetime.fromisoformat(d["delay"]["deadlineAt"])
    expected = add_working(started, 7200)
    check("Takvime göre son zaman çalışma saatleriyle hesaplandı", abs((deadline - expected).total_seconds()) < 2)
    check("Takvimli son zaman düz toplamdan farklı ya da mesai içinde eşit",
        expected == started + timedelta(hours=2) or deadline != started + timedelta(hours=2))
    # --- group override ---
    vip_tx = f"VIP-{suffix}"
    require(send(vip_tx, "start", now - timedelta(minutes=5), group="vip"), (202,))
    wait("VIP işlemin grubu sabitlendi", lambda: detail(vip_tx)["group"] == "vip")
    vd = detail(vip_tx)
    check("Grup süresi adımda görünüyor (60 sn)", next(s for s in vd["steps"] if s["code"] == "review")["maxDelaySeconds"] == 60)
    require(send(vip_tx, "review", group="standard"), (202,))
    wait("Sonraki bildirim grubu değiştirmez", lambda: detail(vip_tx)["group"] == "vip" and detail(vip_tx)["processedEventCount"] == 2)

    # --- effective in the future ---
    require(api("/flows/ver-check/versions", {}))
    require(api("/flows/ver-check/versions/3", {"steps": steps[:1] + [{"code": "finish", "name": "Bitiş", "maxDelaySeconds": 60}], "calendarId": None}, method="PUT"))
    future = (datetime.now(timezone.utc) + timedelta(hours=2)).isoformat()
    pub3 = require(api("/flows/ver-check/versions/3/publish", {"effectiveFrom": future}))
    check("3. sürüm ileri tarihli yayınlandı", pub3["version"] == 3 and datetime.fromisoformat(pub3["effectiveFrom"]) > datetime.now(timezone.utc) + timedelta(hours=1))
    check("İleri tarihli sürüm henüz etkin değil", require(api("/flows/ver-check"))["version"] == 2)
    later_tx = f"LATER-{suffix}"
    require(send(later_tx, "start"), (202,))
    wait("Yeni işlem hâlâ 2. sürümde", lambda: detail(later_tx)["flowVersion"] == 2)
    check("Kullanılan takvim silinemez", api("/calendars/" + cal["id"], method="DELETE")[0] == 409)

    # --- breach report: complete a transaction slowly on version 1 rules (7/24) ---
    slow_flow = "slow-" + suffix[:6]
    require(api("/flows", {"code": slow_flow, "name": "Yavaş akış", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None}, {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 600}]}))
    slow_tx = f"SLOW-{suffix}"
    require(send(slow_tx, "start", now - timedelta(hours=2), flow=slow_flow), (202,))
    require(send(slow_tx, "finish", now - timedelta(minutes=1), flow=slow_flow), (202,))
    fast_tx = f"FAST-{suffix}"
    require(send(fast_tx, "start", now - timedelta(minutes=3), flow=slow_flow), (202,))
    require(send(fast_tx, "finish", now - timedelta(minutes=2), flow=slow_flow), (202,))
    wait("İhlal raporu yavaş işlemi listeledi", lambda: any(r["business_id"] == slow_tx for r in require(api(f"/reports/breaches?flow={slow_flow}"))["items"]))
    report = require(api(f"/reports/breaches?flow={slow_flow}"))
    row = next(r for r in report["items"] if r["business_id"] == slow_tx)
    check("İhlal satırı geçen ve tanınan süreyi veriyor", row["elapsed_seconds"] >= 7000 and row["allowance_seconds"] == 600)
    check("Hızlı işlem ihlal sayılmadı", not any(r["business_id"] == fast_tx for r in report["items"]))
    step = next(s for s in report["byStep"] if s["step_code"] == "finish")
    check("Adım özeti 2 ölçüm, 1 ihlal", step["total"] == 2 and step["breached"] == 1)
    check("Geçersiz tarih filtresi reddedilir", api("/reports/breaches?from=x")[0] == 400)

    require(api("/keys/" + key["id"], method="DELETE"))
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} V1.2 kontrolü geçti. Panelde '{project['name']}' projesini seçerek sonuçları inceleyin.")


if __name__ == "__main__":
    main()
