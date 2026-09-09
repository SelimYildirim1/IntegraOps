"""Exercise the real API + PostgreSQL + RabbitMQ + worker. Python standard library only."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
import json
import os
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]


def local_values():
    values = {}
    path = ROOT / ".env"
    if path.exists():
        for line in path.read_text().splitlines():
            if line.strip() and not line.lstrip().startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    return values


def main():
    values = local_values()
    base = os.environ.get("INTEGRAOPS_API_URL", f"http://127.0.0.1:{values.get('API_PORT', '8080')}")
    bootstrap_key = os.environ.get("BOOTSTRAP_KEY", values.get("BOOTSTRAP_KEY"))
    if not bootstrap_key:
        raise SystemExit("Önce python3 scripts/init_local.py çalıştırın veya BOOTSTRAP_KEY tanımlayın.")

    def request(method, path, body=None, api_key=None, bootstrap=False, expected=200):
        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["X-API-Key"] = api_key
        if bootstrap:
            headers["X-Bootstrap-Key"] = bootstrap_key
        payload = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(base + path, data=payload, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=20) as response:
                code, data = response.status, response.read()
        except urllib.error.HTTPError as error:
            code, data = error.code, error.read()
        if code != expected:
            raise RuntimeError(f"{method} {path}: beklenen HTTP {expected}, gelen HTTP {code}")
        return json.loads(data) if data else None

    ready_until = time.monotonic() + 90
    while True:
        try:
            request("GET", "/health/ready")
            break
        except (OSError, RuntimeError):
            if time.monotonic() >= ready_until:
                raise SystemExit("API hazır olmadı. docker compose logs api migrate komutuyla kontrol edin.")
            time.sleep(1)

    def provision(label):
        return request("POST", "/api/v1/bootstrap/projects", {
            "organizationName": f"Demo {label}", "projectName": "Commerce"
        }, bootstrap=True, expected=201)

    a, b = provision("A"), provision("B")
    steps = ["order-created", "payment-completed", "erp-completed", "invoice-created", "cargo-created"]
    names = ["Sipariş", "Ödeme", "ERP", "Fatura", "Kargo"]
    delays = [None, 300, 300, 600, 900]
    flow = {"code": "order-flow", "name": "Sipariş akışı", "steps": [
        {"code": code, "name": name, "maxDelaySeconds": delay}
        for code, name, delay in zip(steps, names, delays)
    ]}
    for project in [a, b]:
        request("POST", "/api/v1/flows", flow, api_key=project["apiKey"], expected=201)

    timeline_path = "/api/v1/flows/order-flow/transactions/ORDER-10092"
    request("GET", timeline_path, expected=401)
    start = datetime.now(timezone.utc) - timedelta(minutes=10)

    def event(step, minute, status="completed"):
        return {"eventId": str(uuid.uuid4()), "flow": "order-flow", "transactionId": "ORDER-10092",
                "step": step, "status": status,
                "occurredAt": (start + timedelta(minutes=minute)).isoformat()}

    first = event(steps[0], 0)
    receipt = request("POST", "/api/v1/events", first, api_key=a["apiKey"], expected=202)
    duplicate = request("POST", "/api/v1/events", first, api_key=a["apiKey"])
    assert duplicate["duplicate"] is True
    assert duplicate["transactionRecordId"] == receipt["transactionRecordId"]
    request("POST", "/api/v1/events", {**first, "status": "failed"}, api_key=a["apiKey"], expected=409)
    request("GET", timeline_path, api_key=b["apiKey"], expected=404)

    # Concurrent and reverse-order deliveries must merge into the same transaction.
    remaining = [event(step, i) for i, step in enumerate(steps[1:], 1)]
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(lambda item: request("POST", "/api/v1/events", item,
                            api_key=a["apiKey"], expected=202), reversed(remaining)))
    assert all(item["transactionRecordId"] == receipt["transactionRecordId"] for item in results)

    def wait_processed(key, count):
        deadline = time.monotonic() + 45
        while True:
            result = request("GET", timeline_path, api_key=key)
            if result["processedEventCount"] == count:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError("Kayıt kabul edildi fakat işleyici süresinde tamamlamadı. Worker/RabbitMQ loglarını kontrol edin.")
            time.sleep(0.3)

    result = wait_processed(a["apiKey"], 5)
    assert result["acceptedEventCount"] == 5
    assert result["status"] == "completed"
    assert all(step["observation"] == "completed" for step in result["steps"])

    # An older failure arriving late must not regress a later success.
    request("POST", "/api/v1/events", event("erp-completed", 1, "failed"), api_key=a["apiKey"], expected=202)
    assert wait_processed(a["apiKey"], 6)["status"] == "completed"

    # Event and business IDs can be identical across independent projects.
    second_tenant = request("POST", "/api/v1/events", first, api_key=b["apiKey"], expected=202)
    assert second_tenant["transactionRecordId"] != receipt["transactionRecordId"]
    assert wait_processed(b["apiKey"], 1)["status"] == "inProgress"

    request("POST", "/api/v1/events", {**event("unknown-step", 1)}, api_key=a["apiKey"], expected=400)
    request("POST", "/api/v1/events", {**event(steps[0], 0), "occurredAt": "2026-09-07T10:00:00"},
            api_key=a["apiKey"], expected=400)

    private_dir = ROOT / ".local"
    private_dir.mkdir(exist_ok=True)
    credentials_path = private_dir / "demo-project.json"
    credentials_path.write_text(json.dumps(a, indent=2), encoding="utf-8")
    credentials_path.chmod(0o600)
    print("Başarılı: kalıcı olay işleme, tekrar gönderim, içerik çakışması, eşzamanlılık, sıra dışı teslim ve proje ayrımı.")
    print("Demo erişimi .local/demo-project.json dosyasında; bu dosya paylaşılmamalı.")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
