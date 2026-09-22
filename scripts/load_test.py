"""Capacity test: submits a burst of events through the public API with an ingest key, measures acceptance
throughput and latency, then waits for the worker to process everything and reports end-to-end lag,
queue depth and table growth. Use it to find the bottleneck before scaling anything (docs/olcek.md).

    python3 scripts/load_test.py --transactions 500 --concurrency 16
    python3 scripts/load_test.py --transactions 2000 --concurrency 32 --json result.json

Each transaction sends two events (order-created, invoice-created), so the event count is twice the
transaction count. Runs in an isolated test project; transaction ids carry the TEST- prefix so they count as test data.
"""
import argparse
import getpass
import json
import os
import statistics
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require


def psql(sql):
    out = subprocess.run(["docker", "exec", "-i", "integraops-postgres-1", "psql", "-U", "integraops", "-d", "integraops", "-Atc", sql],
                         capture_output=True, text=True)
    return out.stdout.strip() if out.returncode == 0 else None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--transactions", type=int, default=500)
    parser.add_argument("--concurrency", type=int, default=16)
    parser.add_argument("--json", help="write the measurements to this file")
    args = parser.parse_args()
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:8]
    project = require(request(base, "/api/v1/me/projects", {"name": "Kapasite " + suffix}, session))
    headers = session | {"X-Project-Id": project["id"]}
    require(request(base, "/api/v1/flows", {"code": "erp-order-invoice", "name": "ERP", "steps": [
        {"code": "order-created", "name": "Sipariş", "maxDelaySeconds": None}, {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 3600}]}, headers))
    key = require(request(base, "/api/v1/keys", {"name": "kapasite", "scope": "ingest"}, headers))["key"]
    print(f"Test projesi: {project['name']} · {args.transactions} işlem, {args.transactions * 2} olay, eşzamanlılık {args.concurrency}")

    sizes_before = table_sizes()
    latencies, statuses, lock = [], {}, threading.Lock()
    now = datetime.now(timezone.utc).isoformat()

    def send(i, step):
        body = json.dumps({"eventId": str(uuid.uuid4()), "flow": "erp-order-invoice", "transactionId": f"TEST-LOAD-{suffix}-{i}", "step": step,
                           "status": "completed", "occurredAt": now}).encode()
        req = urllib.request.Request(base + "/api/v1/events", data=body, headers={"Content-Type": "application/json", "X-API-Key": key}, method="POST")
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                status = r.status
        except urllib.error.HTTPError as e:
            status = e.code
        except Exception:
            status = 0
        elapsed = time.perf_counter() - started
        with lock:
            latencies.append(elapsed); statuses[status] = statuses.get(status, 0) + 1

    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        list(pool.map(lambda i: send(i, "order-created"), range(args.transactions)))
        list(pool.map(lambda i: send(i, "invoice-created"), range(args.transactions)))
    accept_seconds = time.perf_counter() - started
    accepted = statuses.get(202, 0) + statuses.get(200, 0)
    sorted_latency = sorted(latencies)
    pct = lambda p: sorted_latency[min(len(sorted_latency) - 1, int(len(sorted_latency) * p))]

    # End-to-end: wait until every transaction reached Completed (consumer + projector done).
    deadline = time.monotonic() + 600; completed = 0; processed_at = None
    while time.monotonic() < deadline:
        summary = require(request(base, "/api/v1/transactions/summary?flow=erp-order-invoice&test=include", None, headers))
        completed = summary.get("completed", 0) if isinstance(summary, dict) else 0
        if completed >= args.transactions:
            processed_at = time.perf_counter(); break
        time.sleep(1)
    e2e_seconds = None if processed_at is None else processed_at - started
    pending_outbox = psql("SELECT count(*) FROM outbox_messages WHERE published_at IS NULL")
    unprocessed = psql("SELECT count(*) FROM transaction_events WHERE processed_at IS NULL")
    sizes_after = table_sizes()

    result = {
        "transactions": args.transactions, "events": args.transactions * 2, "concurrency": args.concurrency,
        "accept_seconds": round(accept_seconds, 2), "accepted": accepted, "statuses": statuses,
        "accept_events_per_second": round(len(latencies) / accept_seconds, 1),
        "latency_ms": {"p50": round(pct(0.5) * 1000, 1), "p95": round(pct(0.95) * 1000, 1), "p99": round(pct(0.99) * 1000, 1), "max": round(sorted_latency[-1] * 1000, 1)},
        "completed_transactions": completed, "end_to_end_seconds": None if e2e_seconds is None else round(e2e_seconds, 1),
        "end_to_end_events_per_second": None if e2e_seconds is None else round(args.transactions * 2 / e2e_seconds, 1),
        "pending_outbox": pending_outbox, "unprocessed_events": unprocessed,
        "table_growth_bytes": {t: sizes_after.get(t, 0) - sizes_before.get(t, 0) for t in sizes_after} if sizes_before and sizes_after else None,
        "project_id": project["id"], "measured_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    print()
    print(f"Kabul: {len(latencies)} istek {accept_seconds:.1f} sn içinde · {result['accept_events_per_second']} olay/sn · durumlar {statuses}")
    print(f"Gecikme (ms): p50 {result['latency_ms']['p50']} · p95 {result['latency_ms']['p95']} · p99 {result['latency_ms']['p99']} · en yüksek {result['latency_ms']['max']}")
    if e2e_seconds is None:
        print(f"UYARI: {completed}/{args.transactions} işlem 10 dakikada tamamlanmadı; bekleyen outbox {pending_outbox}, işlenmemiş olay {unprocessed}")
    else:
        print(f"Uçtan uca: {args.transactions} işlem {e2e_seconds:.1f} sn içinde tamamlandı · {result['end_to_end_events_per_second']} olay/sn · bekleyen outbox {pending_outbox}")
    if result["table_growth_bytes"]:
        growth = ", ".join(f"{t} +{b // 1024} KB" for t, b in result["table_growth_bytes"].items() if b > 0)
        print("Tablo büyümesi:", growth or "ölçülemedi")
    if args.json:
        with open(args.json, "w") as f:
            json.dump(result, f, indent=2, ensure_ascii=False)
        print("Sonuç yazıldı:", args.json)
    if accepted < len(latencies) or e2e_seconds is None:
        sys.exit(1)


def table_sizes():
    raw = psql("SELECT relname||'='||pg_total_relation_size(relid) FROM pg_stat_user_tables WHERE relname IN ('transactions','transaction_events','outbox_messages','transaction_delays','transaction_step_stats')")
    if raw is None:
        return {}
    return {line.split("=")[0]: int(line.split("=")[1]) for line in raw.splitlines() if "=" in line}


if __name__ == "__main__":
    main()
