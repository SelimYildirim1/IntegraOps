"""Reconciliation service checks against the local stack.

Creates a test project and flow, a source table with orders in the local PostgreSQL, sends some of
them to IntegraOps, runs the Python reconciler against the API and verifies that never-reported
transactions and missing steps are found, that recent records are left alone (grace), that a
second run does not duplicate discrepancies, and that a discrepancy resolves once the missing
event arrives. Also checks the one-time e-mail notification in Mailpit.
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

PY = ROOT / "reconciler" / ".venv" / "bin" / "python"


def psql(sql):
    return subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "integraops", "-d", "integraops", "-v", "ON_ERROR_STOP=1", "-tA", "-c", sql],
        cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:8]
    project = require(request(base, "/api/v1/me/projects", {"name": "Mutabakat kontrol " + suffix}, session))
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

    python = PY if PY.exists() else Path("python3")
    print("Test projesi:", project["name"], project["id"])
    require(api("/flows", {"code": "recon-check", "name": "Mutabakat kontrolü", "steps": [
        {"code": "order-created", "name": "Sipariş", "maxDelaySeconds": None}, {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 86400}]}))
    key = require(api("/keys", {"name": "Mutabakat anahtarı", "scope": "ingest"}))["key"]
    require(api("/channels", {"name": "Mutabakat e-postası", "kind": "email", "target": f"recon-{suffix}@local.test"}))
    ingest = {"X-API-Key": key}
    now = datetime.now(timezone.utc)

    def send(tx, step, at):
        require(api("/events", {"eventId": str(uuid.uuid4()), "flow": "recon-check", "transactionId": tx, "step": step, "status": "completed",
            "occurredAt": at.isoformat()}, ingest), (202,))

    # Source table: 3 fully reported orders, 1 with a missing invoice step, 1 never reported, 1 fresh (inside grace).
    table = f"recon_orders_{suffix}"
    psql(f"CREATE TABLE {table} (order_no text PRIMARY KEY, created_at timestamptz NOT NULL, invoiced_at timestamptz)")
    two_h = now - timedelta(hours=2)
    orders = {f"SO-{suffix}-{i}": two_h + timedelta(minutes=i) for i in range(3)}
    for tx, at in orders.items():
        psql(f"INSERT INTO {table} VALUES ('{tx}', '{at.isoformat()}', '{(at + timedelta(minutes=10)).isoformat()}')")
        send(tx, "order-created", at); send(tx, "invoice-created", at + timedelta(minutes=10))
    partial = f"SO-{suffix}-partial"
    psql(f"INSERT INTO {table} VALUES ('{partial}', '{two_h.isoformat()}', '{(two_h + timedelta(minutes=10)).isoformat()}')")
    send(partial, "order-created", two_h)
    lost = f"SO-{suffix}-lost"
    psql(f"INSERT INTO {table} VALUES ('{lost}', '{two_h.isoformat()}', NULL)")
    fresh = f"SO-{suffix}-fresh"
    psql(f"INSERT INTO {table} VALUES ('{fresh}', '{(now - timedelta(minutes=2)).isoformat()}', NULL)")
    # Old order whose invoice was issued moments ago and is not reported yet: inside the grace period, not a discrepancy.
    late_step = f"SO-{suffix}-latestep"
    psql(f"INSERT INTO {table} VALUES ('{late_step}', '{two_h.isoformat()}', '{(now - timedelta(minutes=2)).isoformat()}')")
    send(late_step, "order-created", two_h)
    wait("Kaynak olayları işlendi", lambda: require(api("/transactions/summary?flow=recon-check"))["completed"] == 3
        and require(api("/transactions/summary?flow=recon-check"))["total"] == 5)

    lookup = require(api("/reconciliation/lookup", {"flow": "recon-check", "transactionIds": [partial, lost]}, ingest))
    by_id = {i["transactionId"]: i for i in lookup["items"]}
    check("Toplu sorgu var olan ve olmayan işlemi ayırıyor", by_id[partial]["exists"] and by_id[partial]["completedSteps"] == ["order-created"] and not by_id[lost]["exists"])
    check("Toplu sorgu 500 kimlikle sınırlı", api("/reconciliation/lookup", {"flow": "recon-check", "transactionIds": [str(i) for i in range(501)]}, ingest)[0] == 400)

    work = Path(tempfile.mkdtemp(prefix="integraops-recon-"))
    (work / "orders.sql").write_text(f"""
        SELECT order_no AS transaction_id, created_at AS occurred_at, 'order-created' AS step_code, 'completed' AS step_status, created_at AS step_at
        FROM {table} WHERE created_at >= $1 AND created_at < $2
        UNION ALL
        SELECT order_no, created_at, 'invoice-created', 'completed', invoiced_at FROM {table} WHERE invoiced_at IS NOT NULL AND created_at >= $1 AND created_at < $2
    """)
    dsn = f"postgres://integraops:{env['POSTGRES_PASSWORD']}@127.0.0.1:{env.get('POSTGRES_PORT', '54329')}/integraops?sslmode=disable"
    (work / "reconciler.conf").write_text(f"NAME=verify\nAPI_URL={base}\nAPI_KEY={key}\nFLOW=recon-check\nSOURCE=postgres\nPOSTGRES_DSN={dsn}\n"
                                          f"QUERY_FILE={work / 'orders.sql'}\nWINDOW=24h\nOVERLAP=1h\nGRACE=30m\nINTERVAL=1m\n")
    reconciler = ROOT / "reconciler"

    def run():
        result = subprocess.run([str(python), "-m", "integraops_reconciler", "--config", str(work / "reconciler.conf")], cwd=reconciler, capture_output=True, text=True)
        if result.returncode != 0:
            print(result.stdout, result.stderr)
            raise AssertionError("mutabakat koşusu başarısız")
        return [json.loads(line) for line in result.stdout.splitlines() if line.startswith("{")]

    check("Yapılandırma denetimi geçti", subprocess.run([str(python), "-m", "integraops_reconciler", "--config", str(work / "reconciler.conf"), "--check"], cwd=reconciler, capture_output=True).returncode == 0)
    logs = run()
    summary = next(l for l in logs if l["message"] == "koşu kaydedildi")
    check("Taze kayıt aralık dışında kaldı; 6 eski kayıt karşılaştırıldı", summary["source"] == 6 and summary["skipped_recent"] == 0)
    check("İlk koşu 2 yeni uyuşmazlık buldu, taze adım bekletildi", summary["new"] == 2 and summary["matched"] == 4)
    open_items = require(api("/reconciliation/discrepancies?state=open"))
    kinds = {(i["transaction_id"], i["kind"], i["step_code"]) for i in open_items["items"]}
    check("Hiç bildirilmemiş işlem ve eksik adım listelendi", kinds == {(lost, "missing_transaction", ""), (partial, "missing_step", "invoice-created")})
    check("Eksik adımlı işlem panelden açılabilir, bildirilmemiş işlem bilinmiyor", {i["transaction_id"]: i["known"] for i in open_items["items"]} == {lost: False, partial: True})
    check("Taze kayıt ve taze adım uyuşmazlık sayılmadı", not any(i["transaction_id"] in (fresh, late_step) for i in open_items["items"]))
    with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/search?" + urllib.parse.urlencode({"query": f"recon-{suffix}@local.test"}), timeout=10) as r:
        mails = json.loads(r.read())["messages"]
    deadline = time.monotonic() + 60
    while not mails and time.monotonic() < deadline:
        time.sleep(2)
        with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/search?" + urllib.parse.urlencode({"query": f"recon-{suffix}@local.test"}), timeout=10) as r:
            mails = json.loads(r.read())["messages"]
    check("Yeni uyuşmazlıklar e-posta kanalına bir kez bildirildi", len(mails) == 1 and "2 yeni uyuşmazlık" in mails[0]["Subject"])

    second = next(l for l in run() if l["message"] == "koşu kaydedildi")
    check("İkinci koşu aynı uyuşmazlıkları yeniden açmadı", second["new"] == 0 and second["repeated"] == 2)
    again = require(api("/reconciliation/discrepancies?state=open"))
    check("Görülme sayısı arttı, kayıt çoğalmadı", again["total"] == 2 and all(i["seen_count"] == 2 for i in again["items"]))
    time.sleep(3)
    with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/search?" + urllib.parse.urlencode({"query": f"recon-{suffix}@local.test"}), timeout=10) as r:
        check("Tekrarlanan uyuşmazlık yeni e-posta üretmedi", len(json.loads(r.read())["messages"]) == 1)

    # The missing invoice arrives late; the next run resolves that discrepancy.
    send(partial, "invoice-created", two_h + timedelta(minutes=10))
    wait("Geç gelen adım işlendi", lambda: require(api("/flows/recon-check/transactions/" + partial))["status"] == "completed")
    third = next(l for l in run() if l["message"] == "koşu kaydedildi")
    check("Üçüncü koşu eksik adımı çözülmüş saydı", third["resolved"] == 1 and third["repeated"] == 1)
    resolved = require(api("/reconciliation/discrepancies?state=resolved"))
    check("Çözülen uyuşmazlık kapanış zamanıyla listeleniyor", any(i["transaction_id"] == partial and i["resolved_at"] for i in resolved["items"]))
    runs = require(api("/reconciliation/runs?flow=recon-check"))
    check("Koşu geçmişi ve son koşu kaydı tutuluyor", len(runs["items"]) == 3 and any(r["name"] == "verify" for r in runs["lastRuns"]))
    check("Okuma anahtarı koşu kaydedemez", api("/reconciliation/runs", {"name": "x", "flow": "recon-check"}, {"X-API-Key": require(api("/keys", {"name": "r", "scope": "read"}))["key"]})[0] == 403)

    psql(f"DROP TABLE {table}")
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} mutabakat kontrolü geçti.")


if __name__ == "__main__":
    main()
