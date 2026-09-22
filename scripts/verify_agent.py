"""Transfer agent checks against the local stack.

Builds the Go agent, creates an outbox table in the local PostgreSQL, runs the agent against a
test project and verifies: delivery with preserved event ids, duplicate handling, dead-lettering
of rejected bodies, buffering while the API is stopped, rate limiting, the status endpoint,
heartbeats visible through the API, and the directory source. Stops the local API for a short
time during the outage scenario.
"""
import getpass
import json
import os
import shutil
import subprocess
import tempfile
import time
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require


def psql(env, sql):
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
    project = require(request(base, "/api/v1/me/projects", {"name": "Ajan kontrol " + suffix}, session))
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
            try:
                if predicate():
                    check(name, True)
                    return
            except (OSError, KeyError):
                pass
            time.sleep(1)
        raise AssertionError(name + " zaman aşımı")

    def status(port):
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/status", timeout=5) as r:
            return json.loads(r.read())

    print("Test projesi:", project["name"], project["id"])
    require(api("/flows", {"code": "agent-check", "name": "Ajan kontrolü", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None}, {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 86400}]}))
    key = require(api("/keys", {"name": "Ajan anahtarı", "scope": "ingest"}))["key"]

    work = Path(tempfile.mkdtemp(prefix="integraops-agent-"))
    binary = work / "integraops-agent"
    subprocess.run(["go", "build", "-ldflags", "-X main.version=verify", "-o", str(binary), "./cmd/integraops-agent"], cwd=ROOT / "agent", check=True)
    check("Ajan derlendi", binary.exists())
    subprocess.run([str(binary), "-check"], env={**os.environ, "AGENT_API_URL": base, "AGENT_API_KEY": key, "AGENT_POSTGRES_DSN": "postgres://x"}, check=True, capture_output=True)
    bad = subprocess.run([str(binary), "-check"], env={**os.environ, "AGENT_API_URL": "http://api.example.com", "AGENT_API_KEY": key, "AGENT_POSTGRES_DSN": "postgres://x"}, capture_output=True)
    check("Yapılandırma denetimi uzak http adresini reddediyor", bad.returncode == 2)

    table = f"agent_outbox_{suffix}"
    psql(env, f"""CREATE TABLE {table} (id uuid PRIMARY KEY, body text NOT NULL, state varchar(16) NOT NULL DEFAULT 'pending',
        attempts integer NOT NULL DEFAULT 0, available_at timestamptz NOT NULL DEFAULT clock_timestamp(), lease_token uuid, lease_until timestamptz,
        last_error text, created_at timestamptz NOT NULL DEFAULT clock_timestamp())""")

    def event(tx, step):
        return {"eventId": str(uuid.uuid4()), "flow": "agent-check", "transactionId": tx, "step": step, "status": "completed",
            "occurredAt": datetime.now(timezone.utc).isoformat()}

    def enqueue(body, event_id=None):
        event_id = event_id or body["eventId"]
        psql(env, f"INSERT INTO {table}(id, body) VALUES ('{event_id}', '{json.dumps(body).replace(chr(39), chr(39) * 2)}')")
        return event_id

    def states():
        rows = psql(env, f"SELECT state, count(*) FROM {table} GROUP BY state")
        return {line.split("|")[0]: int(line.split("|")[1]) for line in rows.splitlines() if line}

    dsn = f"postgres://integraops:{env['POSTGRES_PASSWORD']}@127.0.0.1:{env.get('POSTGRES_PORT', '54329')}/integraops?sslmode=disable"
    agent_env = {**os.environ, "AGENT_NAME": "verify-postgres", "AGENT_API_URL": base, "AGENT_API_KEY": key, "AGENT_SOURCE": "postgres",
        "AGENT_POSTGRES_DSN": dsn, "AGENT_TABLE": table, "AGENT_RATE_PER_SEC": "20", "AGENT_LISTEN": "127.0.0.1:19464", "AGENT_HEARTBEAT": "5s", "AGENT_POLL": "500ms"}
    log = open(work / "agent.log", "w")
    proc = subprocess.Popen([str(binary)], env=agent_env, stdout=log, stderr=subprocess.STDOUT)
    try:
        wait("Ajan durum ucu açıldı", lambda: status(19464)["name"] == "verify-postgres", timeout=30)
        ids = [enqueue(event(f"AG-{suffix}-{i}", "start")) for i in range(5)]
        wait("5 olay aynı kimliklerle teslim edildi", lambda: states().get("sent") == 5 and all(
            api(f"/flows/agent-check/transactions/AG-{suffix}-{i}")[0] == 200 for i in range(5)))
        detail = require(api(f"/flows/agent-check/transactions/AG-{suffix}-0"))
        wait("Olay kimliği korunarak işlendi", lambda: require(api(f"/flows/agent-check/transactions/AG-{suffix}-0"))["events"][0]["eventId"] == ids[0])
        dup_body = event(f"AG-{suffix}-dup", "start")
        enqueue(dup_body); require(api("/events", dup_body, {"X-API-Key": key}), (202,))
        # Re-queue the same event id a second time: the API answers 200 duplicate, the agent marks it sent.
        psql(env, f"UPDATE {table} SET state='pending', available_at=clock_timestamp() WHERE id='{dup_body['eventId']}'")
        wait("Yinelenen olay 200 ile kabul edilip gönderildi sayıldı", lambda: status(19464)["duplicates"] >= 1)
        enqueue({"eventId": str(uuid.uuid4()), "flow": "agent-check", "transactionId": f"AG-{suffix}-bad", "step": "unknown-step",
            "status": "completed", "occurredAt": datetime.now(timezone.utc).isoformat()})
        wait("Reddedilen olay kalıcı hata olarak durduruldu", lambda: states().get("dead", 0) == 1 and "http_400" in psql(env, f"SELECT last_error FROM {table} WHERE state='dead'"))
        mismatch = str(uuid.uuid4())
        enqueue(event(f"AG-{suffix}-mismatch", "start"), event_id=mismatch)
        wait("Gövde kimliği ile kayıt kimliği uyuşmayan olay gönderilmeden durduruldu", lambda: "body_event_id_mismatch" in psql(env, f"SELECT last_error FROM {table} WHERE id='{mismatch}'"))
        wait("Yaşam sinyali API'de görünüyor", lambda: any(a["name"] == "verify-postgres" and a["version"] == "verify" and a["sent_total"] >= 5 and not a["stale"]
            for a in require(api("/agents"))), timeout=30)
        agent_row = next(a for a in require(api("/agents")) if a["name"] == "verify-postgres")
        check("Yaşam sinyali yapılandırma özetini taşıyor, anahtar içermiyor", agent_row["config"].get("table") == table and "key" not in json.dumps(agent_row["config"]).lower())
        read_key = require(api("/keys", {"name": "Okuma", "scope": "read"}))["key"]
        check("Okuma anahtarı yaşam sinyali gönderemez", api("/agents/heartbeat", {"name": "x", "version": "1"}, {"X-API-Key": read_key})[0] == 403)

        # --- outage ---
        subprocess.run(["docker", "compose", "stop", "api"], cwd=ROOT, check=True, capture_output=True)
        try:
            for i in range(10, 20):
                enqueue(event(f"AG-{suffix}-{i}", "start"))
            wait("API kapalıyken kayıtlar kuyrukta bekliyor ve bağlantı yok bildiriliyor",
                lambda: status(19464)["connected"] is False and status(19464)["pending"] >= 10 and status(19464)["lastError"] == "unreachable", timeout=60)
            attempts = int(psql(env, f"SELECT max(attempts) FROM {table} WHERE state<>'sent' AND state<>'dead'") or 0)
            check("Kesinti sırasında deneme sayacı hızla artmıyor", attempts <= 8)
        finally:
            subprocess.run(["docker", "compose", "start", "api"], cwd=ROOT, check=True, capture_output=True)
        wait("API dönünce bekleyenler aynı kimliklerle teslim edildi", lambda: states().get("sent", 0) == 16 and states().get("pending", 0) == 0
            and all(api(f"/flows/agent-check/transactions/AG-{suffix}-{i}")[0] == 200 for i in range(10, 20)), timeout=180)
        check("Bağlantı yeniden kuruldu bildiriliyor", status(19464)["connected"] is True)

        # --- rate limit: 40 events at 20/s take at least ~1 s beyond the burst ---
        started = time.monotonic()
        for i in range(40):
            enqueue(event(f"AG-{suffix}-rate-{i}", "start"))
        wait("40 olay hız sınırıyla teslim edildi", lambda: states().get("sent", 0) == 56, timeout=60)
        check("Hız sınırı gönderimi yaydı", time.monotonic() - started >= 1.0)
        with urllib.request.urlopen("http://127.0.0.1:19464/metrics", timeout=5) as r:
            metrics = r.read().decode()
        check("Prometheus ölçümleri sunuluyor", "integraops_agent_sent_total 56" in metrics and "integraops_agent_connected 1" in metrics)
    finally:
        proc.terminate()
        try: proc.wait(timeout=10)
        except subprocess.TimeoutExpired: proc.kill()
        log.close()

    # --- directory source ---
    root = work / "events"
    (root / "pending").mkdir(parents=True)
    dir_env = {**os.environ, "AGENT_NAME": "verify-directory", "AGENT_API_URL": base, "AGENT_API_KEY": key, "AGENT_SOURCE": "directory",
        "AGENT_DIRECTORY": str(root), "AGENT_KEEP_SENT": "true", "AGENT_LISTEN": "127.0.0.1:19465", "AGENT_HEARTBEAT": "5s", "AGENT_POLL": "300ms"}
    for i in range(3):
        body = event(f"AGD-{suffix}-{i}", "start")
        tmp = root / "pending" / f".{i}.tmp"; tmp.write_text(json.dumps(body)); tmp.rename(root / "pending" / f"event-{i}.json")
    (root / "pending" / "broken.json").write_text('{"eventId":"not-a-uuid"}')
    log2 = open(work / "agent-dir.log", "w")
    proc = subprocess.Popen([str(binary)], env=dir_env, stdout=log2, stderr=subprocess.STDOUT)
    try:
        wait("Dizin kaynağı dosyaları teslim etti", lambda: len(list((root / "sent").glob("*.json"))) == 3 and all(
            api(f"/flows/agent-check/transactions/AGD-{suffix}-{i}")[0] == 200 for i in range(3)), timeout=60)
        wait("Bozuk dosya dead/ altına hata notuyla taşındı", lambda: (root / "dead" / "broken.json.error").exists())
        wait("Dizin ajanı da yaşam sinyali gönderdi", lambda: any(a["name"] == "verify-directory" and a["source_kind"] == "directory" for a in require(api("/agents"))), timeout=30)
    finally:
        proc.terminate()
        try: proc.wait(timeout=10)
        except subprocess.TimeoutExpired: proc.kill()
        log2.close()

    psql(env, f"DROP TABLE {table}")
    shutil.rmtree(work, ignore_errors=True)
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} ajan kontrolü geçti.")


if __name__ == "__main__":
    main()
