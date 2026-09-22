"""Local, real-service delay demo. Creates its own demo project; uses Python stdlib."""
from datetime import datetime, timezone
import argparse
import json
import os
import time
import uuid

from verify_api import local_values
from verification_client import VerificationClient, VerificationError


def run_checks(client):
    def request(method, path, body=None, key=None, expected=200):
        return client.request(method, path, body, project=key, expected=expected)

    key = client.provision("Gecikme doğrulama", "Gecikme modülü denemesi")
    request("POST", "/api/v1/flows", {
        "code": "delay-demo", "name": "ERP → Onay → Fatura → Kargo",
        "steps": [
            {"code": "erp-completed", "name": "ERP", "maxDelaySeconds": None},
            {"code": "approval-completed", "name": "Onay", "maxDelaySeconds": 30},
            {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 60},
            {"code": "cargo-created", "name": "Kargo", "maxDelaySeconds": 120}
        ]
    }, key=key, expected=201)
    path = "/api/v1/flows/delay-demo/transactions/DELAY-DEMO-1"

    def submit(step, status="completed"):
        body = {"eventId": str(uuid.uuid4()), "flow": "delay-demo", "transactionId": "DELAY-DEMO-1",
            "step": step, "status": status, "occurredAt": datetime.now(timezone.utc).isoformat()}
        if status == "failed":
            body["error"] = "Demo: dış sistem fatura oluşturamadı."
        request("POST", "/api/v1/events", body, key=key, expected=202)
        return body

    def wait_for(state, step, count, timeout=45):
        until = time.monotonic() + timeout
        while True:
            result = request("GET", path, key=key)
            delay = result.get("delay") or {}
            if (result["processedEventCount"] == count and delay.get("state") == state
                    and delay.get("step") == step):
                return result
            if time.monotonic() >= until:
                raise RuntimeError(f"Beklenen: {state}/{step}; gelen: {delay.get('state')}/{delay.get('step')}. "
                    "docker compose logs --tail=50 worker ile kontrol edin.")
            time.sleep(0.5)

    def show(label, result):
        print(label, flush=True)
        print(json.dumps({"status": result["status"], "delay": result["delay"]},
            ensure_ascii=False, indent=2), flush=True)

    print("Ayrı demo projesi açıldı. Süreler sadece bu demo akışına ait: 30 / 60 / 120 saniye.", flush=True)
    first = submit("erp-completed")
    waiting = wait_for("waiting", "approval-completed", 1, timeout=20)
    show("1. ERP tamamlandı; onay bekleniyor.", waiting)
    print("2. Yeni olay göndermeden yaklaşık 30–35 saniyede gecikme taramasını gözleyeceğiz.", flush=True)
    overdue = wait_for("overdue", "approval-completed", 1)
    assert overdue["status"] == "inProgress", "Gecikme, işlem durumunu hataya çevirmemeli."
    show("Süre doldu: onay gecikmiş, işlem hâlâ devam ediyor.", overdue)

    receipt = request("POST", "/api/v1/events", first, key=key)
    assert receipt["duplicate"] is True
    unchanged = wait_for("overdue", "approval-completed", 1)
    assert unchanged["delay"]["deadlineAt"] == waiting["delay"]["deadlineAt"]

    approval = submit("approval-completed")
    invoice = wait_for("waiting", "invoice-created", 2)
    deadline = datetime.fromisoformat(invoice["delay"]["deadlineAt"].replace("Z", "+00:00"))
    anchor = datetime.fromisoformat(approval["occurredAt"])
    assert abs((deadline - anchor).total_seconds() - 60) < 0.001
    show("3. Onay geç geldi. Onay gecikmesi kalktı; fatura için yeni 60 saniye başladı.", invoice)

    submit("invoice-created", "failed")
    failed = wait_for("none", None, 3)
    assert failed["status"] == "failed"
    show("4. Dış sistem açıkça hata bildirdi: failed, gecikme değil.", failed)

    submit("invoice-created")
    show("5. Yeni başarı bildirimi geldi; kargo için 120 saniye başladı.",
        wait_for("waiting", "cargo-created", 4))
    submit("cargo-created")
    completed = wait_for("none", None, 5)
    assert completed["status"] == "completed"
    show("6. Bütün adımlar tamamlandı; bekleyen adım kalmadı.", completed)
    print("Başarılı: periyodik tarama, adım bazlı süre, tekrar gönderim, geç bildirim ve hata ayrımı.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session", action="store_true", help="Kurulu V1 hesabıyla giriş yapıp ayrı test projesi oluştur")
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
