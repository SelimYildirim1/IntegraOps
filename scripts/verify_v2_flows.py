"""V2 complex-flow checks: parallel stages, optional and conditional steps, explicit skip, cancellation,
and their effect on transaction status, delay tracking, alarms and step statistics. Isolated test project.
"""
import getpass
import os
import time
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
    project = require(request(base, "/api/v1/me/projects", {"name": "Karmaşık akış " + suffix}, session))
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
    # Create a linear flow, then publish version 2 with stages/kinds.
    require(api("/flows", {"code": "order", "name": "Sipariş", "steps": [
        {"code": "order", "name": "Sipariş", "maxDelaySeconds": None}, {"code": "shipped", "name": "Kargo", "maxDelaySeconds": 3600}]}))
    require(api("/flows/order/versions", {}))
    steps = [
        {"code": "order", "name": "Sipariş", "maxDelaySeconds": None, "stage": 0},
        {"code": "payment", "name": "Ödeme", "maxDelaySeconds": 5, "stage": 1},
        {"code": "stock", "name": "Stok", "maxDelaySeconds": 60, "stage": 1},
        {"code": "gift-wrap", "name": "Hediye paketi", "maxDelaySeconds": None, "stage": 1, "kind": "optional"},
        {"code": "customs", "name": "Gümrük", "maxDelaySeconds": 60, "stage": 2, "kind": "conditional", "groups": ["export"]},
        {"code": "shipped", "name": "Kargo", "maxDelaySeconds": 3600, "stage": 3}]
    check("Aşama sırası doğrulanır", api("/flows/order/versions/2", {"steps": steps[:1] + [dict(steps[1], stage=3)]}, method="PUT")[0] == 400)
    check("Koşullu adım grup ister", api("/flows/order/versions/2", {"steps": steps[:4] + [dict(steps[4], groups=[])]}, method="PUT")[0] == 400)
    require(api("/flows/order/versions/2", {"steps": steps}, method="PUT"))
    require(api("/flows/order/versions/2/publish", {}))
    overview = require(api("/flows/order"))
    check("Sürüm 2 aşama, tür ve grupları taşıyor", overview["version"] == 2 and [s["stage"] for s in overview["steps"]] == [0, 1, 1, 1, 2, 3]
        and next(s for s in overview["steps"] if s["code"] == "customs")["groups"] == ["export"])
    diff = require(api("/flows/order/versions/1/diff/2"))
    check("Sürüm farkı aşama değişikliğini gösteriyor", any(c["kind"] == "stage" and c["code"] == "shipped" for c in diff["changes"]))
    key = require(api("/keys", {"name": "k", "scope": "ingest"}))["key"]
    ingest = {"X-API-Key": key}

    def send(tx, step, status="completed", at=None, group=None, error=None):
        return api("/events", {"eventId": str(uuid.uuid4()), "flow": "order", "transactionId": tx, "step": step, "status": status,
            "occurredAt": (at or datetime.now(timezone.utc)).isoformat(), "group": group, "error": error}, ingest)

    def detail(tx):
        return require(api("/flows/order/transactions/" + tx))

    def obs(tx, step):
        return next(s for s in detail(tx)["steps"] if s["code"] == step)["observation"]

    # --- parallel branches ---
    par = f"PAR-{suffix}"
    require(send(par, "order", at=datetime.now(timezone.utc) - timedelta(seconds=10)), (202,))
    wait("Paralel aşama: en yakın son zaman ödeme adımına ait", lambda: (d := detail(par))["processedEventCount"] == 1 and d["delay"]["step"] == "payment")
    wait("Ödeme dalı süre aşımı alarmı açtı", lambda: any(a["step_code"] == "payment" and a["kind"] == "overdue" for a in require(api("/alarms?flow=order"))["items"]), timeout=60)
    check("Stok dalı henüz süresi dolmadığı için alarm açmadı", not any(a["step_code"] == "stock" for a in require(api("/alarms?flow=order"))["items"]))
    require(send(par, "payment"), (202,))
    wait("Ödeme gelince alarm kapandı, bekleme stok dalına geçti", lambda: (d := detail(par))["processedEventCount"] == 2 and d["delay"]["step"] == "stock"
        and require(api("/alarms?flow=order&state=open"))["total"] == 0)
    require(send(par, "stock"), (202,))
    wait("İki dal bitince sonraki aşama (kargo) bekleniyor; gümrük bu grup için beklenmiyor",
        lambda: (d := detail(par))["delay"]["step"] == "shipped" and obs(par, "customs") == "not_applicable")
    check("İsteğe bağlı adım bildirim yok olarak duruyor ve beklenmiyor", obs(par, "gift-wrap") == "not_observed" and detail(par)["status"] == "inProgress")
    require(send(par, "shipped"), (202,))
    wait("Bütün beklenen adımlar tamamlanınca işlem tamamlandı (gümrük ve hediye paketi olmadan)", lambda: detail(par)["status"] == "completed")
    stats = require(api("/reports/breaches?flow=order"))["byStep"]
    check("Adım istatistikleri paralel dallar için ayrı ölçüldü", {s["step_code"] for s in stats} >= {"payment", "stock", "shipped"})

    # --- conditional for export group ---
    exp = f"EXP-{suffix}"
    for step in ("order", "payment", "stock", "shipped"):
        require(send(exp, step, group="export"), (202,))
    wait("İhracat grubu için gümrük adımı bekleniyor", lambda: (d := detail(exp))["processedEventCount"] == 4 and d["status"] == "inProgress" and obs(exp, "customs") == "not_observed")
    check("Gümrük bekleniyor ama kargo daha önce bildirildiği için sıra gümrükte", detail(exp)["delay"]["step"] == "customs")
    require(send(exp, "customs", "skipped"), (202,))
    wait("Atlandı bildirimi adımı tamamlandı sayar ve işlem tamamlanır", lambda: detail(exp)["status"] == "completed" and obs(exp, "customs") == "skipped")

    # --- cancellation ---
    can = f"CAN-{suffix}"
    require(send(can, "order", at=datetime.now(timezone.utc) - timedelta(seconds=10)), (202,))
    wait("İptal öncesi süre aşımı alarmı açıldı", lambda: any(a["business_id"] == can for a in require(api("/alarms?flow=order&state=open"))["items"]), timeout=60)
    require(send(can, "payment", "cancelled", error="Müşteri vazgeçti"), (202,))
    wait("İptal bildirimi işlemi iptal etti", lambda: detail(can)["status"] == "cancelled")
    alarm = next(a for a in require(api("/alarms?flow=order&state=all"))["items"] if a["business_id"] == can)
    check("İptal açık alarmı 'cancelled' nedeniyle kapattı", alarm["resolved_at"] is not None and alarm["resolution"] == "cancelled")
    check("İptal edilen işlem süre takibi yapmıyor", detail(can)["delay"]["state"] == "none")
    check("Liste iptal filtresini destekliyor", require(api("/transactions?flow=order&status=cancelled"))["totalCount"] == 1)
    check("Özet iptal sayısını veriyor", require(api("/transactions/summary?flow=order"))["cancelled"] == 1)
    check("Geçersiz durum reddedilir", send(f"X-{suffix}", "order", "paused")[0] == 400)

    # --- optional step reported anyway ---
    opt = f"OPT-{suffix}"
    require(send(opt, "order"), (202,)); require(send(opt, "gift-wrap"), (202,))
    wait("İsteğe bağlı adım bildirilince görünür ama süre takibi ödeme/stok dalında kalır",
        lambda: (d := detail(opt))["processedEventCount"] == 2 and obs(opt, "gift-wrap") == "completed" and d["delay"]["step"] in ("payment", "stock"))

    # --- version 1 transactions keep linear behaviour ---
    old = require(api("/flows/order/versions/1"))
    check("1. sürüm doğrusal aşamalarla korunuyor", [s["stage"] for s in old["steps"]] == [0, 1] and all(s["kind"] == "required" for s in old["steps"]))

    require(api("/keys/" + require(api("/keys"))[0]["id"], method="DELETE"))
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} karmaşık akış kontrolü geçti.")


if __name__ == "__main__":
    main()
