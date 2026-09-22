"""Verify the local monitoring stack (compose.monitoring.yml).

Default checks are read-only against Prometheus, Alertmanager and Grafana, plus one test
project with a few events. With --faults the local RabbitMQ is stopped while the worker keeps
running, which is the "worker looks alive but events do not move" case; the script waits for
the alert and its e-mail, starts RabbitMQ again and waits until the alert resolves.
"""
import argparse
import getpass
import json
import os
import subprocess
import time
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timezone
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

PROMETHEUS = "http://127.0.0.1:9090"
ALERTMANAGER = "http://127.0.0.1:9093"
GRAFANA = "http://127.0.0.1:3001"
MAILPIT = "http://127.0.0.1:8025"

REQUIRED_METRICS = [
    "integraops_outbox_pending", "integraops_outbox_oldest_age_seconds",
    "integraops_events_unprocessed", "integraops_events_oldest_unprocessed_age_seconds",
    "integraops_worker_heartbeat_age_seconds", "integraops_delay_scan_last_success_seconds",
    "integraops_events_accepted_total", "integraops_events_processed_total",
    "integraops_events_processing_duration_seconds_bucket", "integraops_outbox_published_total",
    "integraops_db_connections", "integraops_db_size_bytes", "integraops_db_table_rows",
    "http_server_request_duration_seconds_bucket", "rabbitmq_detailed_queue_messages_ready",
    "node_filesystem_avail_bytes",
]


def get_json(url, headers=None):
    req = urllib.request.Request(url, headers=headers or {})
    with urllib.request.urlopen(req, timeout=10) as response:
        return json.loads(response.read())


def query(expr):
    data = get_json(PROMETHEUS + "/api/v1/query?" + urllib.parse.urlencode({"query": expr}))
    return data["data"]["result"]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--faults", action="store_true", help="Stop local RabbitMQ and verify the stuck-pipeline alert")
    args = parser.parse_args()
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    passed = 0

    def check(name, value):
        nonlocal passed
        if not value:
            raise AssertionError(name)
        passed += 1
        print("GEÇTİ ·", name, flush=True)

    def wait(name, predicate, timeout=120, interval=3):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                if predicate():
                    check(name, True)
                    return
            except OSError:
                pass
            time.sleep(interval)
        raise AssertionError(name + " zaman aşımı")

    check("Prometheus hazır", urllib.request.urlopen(PROMETHEUS + "/-/ready", timeout=5).status == 200)
    check("Alertmanager hazır", urllib.request.urlopen(ALERTMANAGER + "/-/ready", timeout=5).status == 200)
    check("Grafana hazır", get_json(GRAFANA + "/api/health").get("database") == "ok")
    targets = get_json(PROMETHEUS + "/api/v1/targets")["data"]["activeTargets"]
    check("Bütün kazıma hedefleri erişilebilir", targets and all(t["health"] == "up" for t in targets))
    rules = [r for g in get_json(PROMETHEUS + "/api/v1/rules")["data"]["groups"] for r in g["rules"]]
    check("Alarm kuralları hatasız yüklendi", len(rules) >= 15 and all(r["health"] == "ok" for r in rules))

    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:10]
    project = require(request(base, "/api/v1/me/projects", {"name": "İzleme doğrulama " + suffix}, session))
    headers = session | {"X-Project-Id": project["id"]}
    api = lambda path, data=None, auth=None, method=None: request(base, "/api/v1" + path, data, auth or headers, method)
    require(api("/flows", {"code": "monitoring-check", "name": "İzleme kontrolü", "steps": [
        {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
        {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 86400}]}))
    key = require(api("/keys", {"name": "İzleme testi", "scope": "ingest"}))
    ingest = {"X-API-Key": key["key"]}

    def send(tx, step):
        require(api("/events", {"eventId": str(uuid.uuid4()), "flow": "monitoring-check", "transactionId": tx,
            "step": step, "status": "completed", "occurredAt": datetime.now(timezone.utc).isoformat()}, ingest), (202,))

    before = sum(float(r["value"][1]) for r in query('sum(integraops_events_processed_total{outcome="processed"})'))
    for i in range(5):
        send(f"MON-{suffix}-{i}", "start")
        send(f"MON-{suffix}-{i}", "finish")
    # The worker exports every 15 s; allow two export cycles.
    wait("İşlenen olay sayacı arttı", lambda: sum(float(r["value"][1]) for r in
        query('sum(integraops_events_processed_total{outcome="processed"})')) >= before + 10, timeout=90)
    present = {r["metric"]["__name__"] for r in query("{__name__=~\"" + "|".join(REQUIRED_METRICS) + "\"}")}
    missing = sorted(set(REQUIRED_METRICS) - present)
    if missing:
        print("Eksik ölçümler:", ", ".join(missing))
    check("Beklenen ölçümlerin tamamı Prometheus'ta", not missing)
    p95 = query("histogram_quantile(0.95, sum by (le) (rate(integraops_events_processing_duration_seconds_bucket[5m])))")
    check("Kabulden işlemeye süre hesaplanabiliyor", p95 and p95[0]["value"][1] not in ("NaN", "+Inf"))
    print(f"   kabulden işlemeye p95: {float(p95[0]['value'][1]):.3f} sn (bu bilgisayardaki ölçüm)")
    dashboard = get_json(GRAFANA + "/api/dashboards/uid/integraops-health",
        {"Authorization": "Basic " + __import__("base64").b64encode(f"admin:{env['GRAFANA_ADMIN_PASSWORD']}".encode()).decode()})
    check("Grafana panosu yüklendi", len(dashboard["dashboard"]["panels"]) >= 20)

    if args.faults:
        def compose(*parts):
            subprocess.run(["docker", "compose", "-f", "docker-compose.yml", "-f", "compose.monitoring.yml", *parts],
                cwd=ROOT, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        def firing(name):
            return any(a["labels"]["alertname"] == name and a["status"]["state"] == "active"
                for a in get_json(ALERTMANAGER + "/api/v2/alerts"))
        def mail_with(fragment):
            data = get_json(MAILPIT + "/api/v1/search?" + urllib.parse.urlencode({"query": fragment}))
            return data.get("messages_count", 0) > 0 or data.get("total", 0) > 0
        stuck_tx = f"MON-STUCK-{suffix}"
        print("RabbitMQ durduruluyor; worker çalışmaya devam edecek. Alarm yaklaşık 5 dakika sürer.", flush=True)
        try:
            compose("stop", "rabbitmq")
            send(stuck_tx, "start")
            wait("Worker yaşam sinyali güncel kalırken", lambda: all(float(r["value"][1]) < 30
                for r in query("integraops_worker_heartbeat_age_seconds")), timeout=60)
            wait("'Olaylar ilerlemiyor' alarmı Alertmanager'a ulaştı",
                lambda: firing("IntegraOpsEventsNotProgressing"), timeout=480, interval=10)
            check("Worker yaşam sinyali alarmı açılmadı", not firing("IntegraOpsWorkerHeartbeatStale"))
            wait("Alarm e-postası ekibe gönderildi", lambda: mail_with("IntegraOpsEventsNotProgressing"), timeout=120, interval=5)
        finally:
            compose("start", "rabbitmq")
        wait("RabbitMQ dönünce bekleyen olay işlendi", lambda: require(api(
            f"/flows/monitoring-check/transactions/{stuck_tx}"))["processedEventCount"] == 1, timeout=240, interval=5)
        wait("Alarm kendiliğinden çözüldü", lambda: not firing("IntegraOpsEventsNotProgressing"), timeout=420, interval=10)

    require(api("/keys/" + key["id"], method="DELETE"))
    require(api("/me/logout", method="POST"))
    print(f"\n{passed} izleme kontrolü geçti. Grafana: {GRAFANA} · Prometheus: {PROMETHEUS} · Alertmanager: {ALERTMANAGER}")


if __name__ == "__main__":
    main()
