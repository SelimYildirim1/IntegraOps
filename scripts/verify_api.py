"""Exercise the real API + PostgreSQL + RabbitMQ + worker. Python standard library only."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
import argparse
import json
import os
import time
import uuid

from prepare_v1 import read_env
from verification_client import VerificationClient, VerificationError

ROOT = Path(__file__).resolve().parents[1]


def local_values():
    return read_env(ROOT / ".env")


def run_checks(client):
    def request(method, path, body=None, api_key=None, expected=200):
        return client.request(method, path, body, project=api_key, expected=expected)

    def provision(label):
        return client.provision("API doğrulama " + label, "Demo " + label)

    a, b = provision("A"), provision("B")
    steps = ["order-created", "payment-completed", "erp-completed", "invoice-created", "cargo-created"]
    names = ["Sipariş", "Ödeme", "ERP", "Fatura", "Kargo"]
    delays = [None, 300, 300, 600, 900]
    flow = {"code": "order-flow", "name": "Sipariş akışı", "steps": [
        {"code": code, "name": name, "maxDelaySeconds": delay}
        for code, name, delay in zip(steps, names, delays)
    ]}
    for project in [a, b]:
        request("POST", "/api/v1/flows", flow, api_key=project, expected=201)

    timeline_path = "/api/v1/flows/order-flow/transactions/ORDER-10092"
    request("GET", timeline_path, expected=401)
    start = datetime.now(timezone.utc) - timedelta(minutes=10)

    def event(step, minute, status="completed"):
        return {"eventId": str(uuid.uuid4()), "flow": "order-flow", "transactionId": "ORDER-10092",
                "step": step, "status": status,
                "occurredAt": (start + timedelta(minutes=minute)).isoformat()}

    first = event(steps[0], 0)
    receipt = request("POST", "/api/v1/events", first, api_key=a, expected=202)
    duplicate = request("POST", "/api/v1/events", first, api_key=a)
    assert duplicate["duplicate"] is True
    assert duplicate["transactionRecordId"] == receipt["transactionRecordId"]
    request("POST", "/api/v1/events", {**first, "status": "failed"}, api_key=a, expected=409)
    request("GET", timeline_path, api_key=b, expected=404)

    # Concurrent and reverse-order deliveries must merge into the same transaction.
    remaining = [event(step, i) for i, step in enumerate(steps[1:], 1)]
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(lambda item: request("POST", "/api/v1/events", item,
                            api_key=a, expected=202), reversed(remaining)))
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

    result = wait_processed(a, 5)
    assert result["acceptedEventCount"] == 5
    assert result["status"] == "completed"
    assert all(step["observation"] == "completed" for step in result["steps"])

    # An older failure arriving late must not regress a later success.
    request("POST", "/api/v1/events", event("erp-completed", 1, "failed"), api_key=a, expected=202)
    assert wait_processed(a, 6)["status"] == "completed"

    # Event and business IDs can be identical across independent projects.
    second_tenant = request("POST", "/api/v1/events", first, api_key=b, expected=202)
    assert second_tenant["transactionRecordId"] != receipt["transactionRecordId"]
    assert wait_processed(b, 1)["status"] == "inProgress"

    request("POST", "/api/v1/events", {**event("unknown-step", 1)}, api_key=a, expected=400)
    request("POST", "/api/v1/events", {**event(steps[0], 0), "occurredAt": "2026-09-07T10:00:00"},
            api_key=a, expected=400)

    if a.bootstrap_credentials is not None:
        # Preserve first-install setup compatibility. Session runs never overwrite this file.
        private_dir = ROOT / ".local"
        private_dir.mkdir(exist_ok=True)
        credentials_path = private_dir / "demo-project.json"
        credentials_path.write_text(json.dumps(a.bootstrap_credentials, indent=2), encoding="utf-8")
        credentials_path.chmod(0o600)
        print("İlk kurulum için demo erişimi yerel kurulum dosyasına kaydedildi.")
    print("Başarılı: kalıcı olay işleme, tekrar gönderim, içerik çakışması, eşzamanlılık, sıra dışı teslim ve proje ayrımı.")
    print(json.dumps(result, ensure_ascii=False, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session", action="store_true", help="Kurulu V1 hesabıyla giriş yapıp ayrı test projeleri oluştur")
    args = parser.parse_args()
    values = local_values()
    base = os.environ.get("INTEGRAOPS_API_URL", f"http://127.0.0.1:{values.get('API_PORT', '8080')}")
    try:
        with VerificationClient(base, values, args.session) as client:
            run_checks(client)
    except (VerificationError, OSError) as error:
        raise SystemExit(str(error)) from None


if __name__ == "__main__":
    main()
