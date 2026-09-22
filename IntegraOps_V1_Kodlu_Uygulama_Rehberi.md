# IntegraOps V1 — Kodlarla uygulama rehberi

Bu belge, daha önce hazırlanan V1 Paket 01 değişikliklerinin tek dosyalık sürümüdür. ZIP veya ayrı patch dosyası gerekmez. Aşağıda projeye uygulanacak 10 dosyanın **tam içerikleri** bulunur; kodlarda kısaltma yoktur. Paketi zaten uyguladıysan aynı değişiklikleri tekrar yapmana gerek yok.

## Neler ekleniyor?

- İşlem detayında negatif olay zamanı farkı için tutarsızlık açıklaması.
- Sistem ekranında kuyruk ve işlenmemiş olay sayaçlarının doğru açıklaması.
- Mevcut V1 hesabıyla API ve gecikme testleri için `--session` desteği.
- Testlere özel erişim anahtarlarının iptali ve test oturumunun kapatılması.
- 9 yardımcı istemci testi, CI adımları ve güncel proje dokümantasyonu.

Veritabanı migration'ı veya yeni paket kurulumu gerektiren bir bağımlılık eklenmiyor. Bu, V1 kapanışının ilk düzeltme paketidir; Go/Python servisleri, gerçek ERP bağlantısı ve canlı pilot bu değişikliklerin kapsamında değildir.

## Nasıl uygulayacaksın?

1. Mevcut projende değişecek dosyaları yedekle veya mevcut çalışmanın Git kaydını al.
2. Aşağıdaki yolları **proje köküne göre** aç. “Yeni dosya” denilenleri, gerekirse üst klasörlerini oluşturarak ekle.
3. Her dosyanın altındaki kod bloğunun yalnızca içeriğini kopyala. Kod çitlerini ve dosya başlıklarını kaynak dosyaya ekleme. “Değiştir” denilen dosyalarda blok, dosyanın tamamıdır; eski içeriğin sonuna ekleme.
4. Paylaştığın proje sürümünden sonra bir dosyayı değiştirdiysen o dosyada kendi değişikliklerini koruyarak birleştir. Bu belge, paylaşılan sürüme göre hazırlanmıştır.
5. Tüm dosyaları tamamladıktan sonra belgenin sonundaki kontrolleri çalıştır.

`[flow]` ve `[transactionId]` gerçek klasör adlarıdır; bunları sipariş veya akış adıyla değiştirme. `verification_client.py`, `verify_api.py` ve `verify_delays.py` birlikte uygulanmalıdır.

## Dosya listesi

| Sıra | Yol | İşlem |
|---|---|---|
| 1 | `scripts/verification_client.py` | Yeni dosya |
| 2 | `scripts/verify_api.py` | Değiştir |
| 3 | `scripts/verify_delays.py` | Değiştir |
| 4 | `scripts/tests/test_verification_client.py` | Yeni dosya |
| 5 | `frontend/src/lib/time.ts` | Değiştir |
| 6 | `frontend/src/app/transactions/[flow]/[transactionId]/page.tsx` | Değiştir |
| 7 | `frontend/src/app/operations/page.tsx` | Değiştir |
| 8 | `.github/workflows/ci.yml` | Değiştir |
| 9 | `README.md` | Değiştir |
| 10 | `docs/v1-paket-01.md` | Yeni dosya |

## 1. `scripts/verification_client.py`

**İşlem:** Yeni dosya oluştur.

```python
"""Local verification access: legacy bootstrap or an isolated session-owned project.

Session-mode keys exist only in memory and are revoked on exit. Test records remain
available in the panel. Only this run's keys and session are cleaned up.
"""
from dataclasses import dataclass, field
import getpass
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid


class VerificationError(RuntimeError):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


@dataclass
class TestProject:
    project_id: str
    read_key: str = field(repr=False)
    ingest_key: str = field(repr=False)
    session_headers: dict = field(default_factory=dict, repr=False)
    bootstrap_credentials: dict | None = field(default=None, repr=False)

    def headers(self, method, path):
        if method == "POST" and path == "/api/v1/flows" and self.session_headers:
            return dict(self.session_headers)
        key = self.read_key if method == "GET" else self.ingest_key
        return {"X-API-Key": key}


class VerificationClient:
    def __init__(self, base, values, session_mode=False):
        self.base = base.rstrip("/")
        self.bootstrap_key = os.environ.get("BOOTSTRAP_KEY", values.get("BOOTSTRAP_KEY"))
        self.session_mode = session_mode
        self.session_headers = {}
        self.created_keys = []

    def request(self, method, path, body=None, project=None, expected=200, headers=None):
        actual_headers = {"Content-Type": "application/json"}
        if project is not None:
            actual_headers.update(project.headers(method, path))
        if headers:
            actual_headers.update(headers)
        payload = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=payload, headers=actual_headers, method=method)
        # A proxy redirect must never forward test credentials to another endpoint.
        opener = urllib.request.build_opener(NoRedirect())
        try:
            with opener.open(req, timeout=20) as response:
                code, data = response.status, response.read()
        except urllib.error.HTTPError as error:
            code, data = error.code, error.read()
        if code != expected:
            if path == "/api/v1/bootstrap/projects" and code == 409:
                raise VerificationError("V1 kullanıcı kurulumu tamamlanmış. Bu betiği --session ile çalıştırın.")
            # Do not expose response bodies, credentials or submitted passwords.
            raise VerificationError(f"{method} {path}: beklenen HTTP {expected}, gelen HTTP {code}")
        if not data:
            return None
        try:
            return json.loads(data)
        except (ValueError, UnicodeDecodeError) as error:
            raise VerificationError(f"{method} {path}: JSON yanıtı okunamadı.") from error

    def __enter__(self):
        until = time.monotonic() + 90
        while True:
            try:
                self.request("GET", "/health/ready")
                break
            except (OSError, VerificationError):
                if time.monotonic() >= until:
                    raise VerificationError("API hazır olmadı. docker compose logs --tail=50 api migrate ile kontrol edin.")
                time.sleep(1)
        if self.session_mode:
            email = (os.environ.get("V1_OWNER_EMAIL") or input("Kullanıcı e-posta: ")).strip()
            password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
            result = self.request("POST", "/api/v1/auth/login", {"email": email, "password": password})
            token = result.get("token") if isinstance(result, dict) else None
            if not isinstance(token, str) or len(token) != 64:
                raise VerificationError("Giriş yanıtında geçerli oturum bulunamadı.")
            self.session_headers = {"Authorization": "Bearer " + token}
        elif not self.bootstrap_key:
            raise VerificationError("İlk kurulum anahtarı bulunamadı. V1 kuruluysa --session kullanın.")
        return self

    def provision(self, name, organization="Yerel doğrulama"):
        if not self.session_mode:
            data = self.request("POST", "/api/v1/bootstrap/projects",
                {"organizationName": organization, "projectName": name}, expected=201,
                headers={"X-Bootstrap-Key": self.bootstrap_key})
            return TestProject(data["projectId"], data["apiKey"], data["apiKey"], bootstrap_credentials=data)
        name = name + " " + uuid.uuid4().hex[:8]
        data = self.request("POST", "/api/v1/me/projects", {"name": name}, expected=201,
            headers=self.session_headers)
        headers = self.session_headers | {"X-Project-Id": data["id"]}
        keys = {}
        for scope in ("read", "ingest"):
            key = self.request("POST", "/api/v1/keys", {"name": "Geçici doğrulama " + scope, "scope": scope},
                expected=201, headers=headers)
            self.created_keys.append((dict(headers), key["id"]))
            keys[scope] = key["key"]
        print("Test projesi:", name, data["id"], flush=True)
        return TestProject(data["id"], keys["read"], keys["ingest"], headers)

    def __exit__(self, exc_type, exc_value, traceback):
        failures = 0
        for headers, key_id in reversed(self.created_keys):
            try:
                self.request("DELETE", "/api/v1/keys/" + key_id, expected=204, headers=headers)
            except (OSError, VerificationError):
                failures += 1
                print("Geçici test anahtarı kapatılamadı; test projesinin Ayarlar ekranından iptal edin:",
                    headers["X-Project-Id"], key_id, file=sys.stderr)
        self.created_keys.clear()
        if self.session_headers:
            try:
                self.request("POST", "/api/v1/me/logout", expected=204, headers=self.session_headers)
            except (OSError, VerificationError):
                failures += 1
                print("Test oturumu kapatılamadı; oturum en geç 8 saatte sona erer.", file=sys.stderr)
            self.session_headers.clear()
        if failures and exc_type is None:
            raise VerificationError("Kontroller tamamlandı ancak test erişimlerinin temizliği eksik kaldı.")
        return False
```

## 2. `scripts/verify_api.py`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```python
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
```

## 3. `scripts/verify_delays.py`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```python
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
```

## 4. `scripts/tests/test_verification_client.py`

**İşlem:** Yeni dosya oluştur.

```python
"""Checks the verification client's HTTP/auth contract with a local fake API.

These tests do not replace PostgreSQL/RabbitMQ integration tests.
"""
from contextlib import redirect_stdout, redirect_stderr
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Thread
from unittest.mock import patch
import io
import json
import os
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from verification_client import VerificationClient, VerificationError


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def handle_request(self):
        api = self.server
        data = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        body = json.loads(data) if data else None
        api.calls.append((self.command, self.path, {k.lower(): v for k, v in self.headers.items()}, body))
        code, result = 200, {}
        if self.path == "/health/ready":
            result = {"status": "ready"}
        elif self.path == "/api/v1/auth/login":
            code = api.login_status
            result = {"token": "t" * 64} if code == 200 else {"title": "private-response"}
        elif self.path == "/api/v1/bootstrap/projects":
            code = api.bootstrap_status
            result = {"projectId": "bootstrap-project", "organizationId": "org", "apiKey": "legacy-test-key"}
        elif self.path == "/api/v1/me/projects":
            api.projects += 1
            code, result = 201, {"id": f"project-{api.projects}"}
        elif self.path == "/api/v1/keys" and self.command == "POST":
            scope = body["scope"]
            code = 503 if scope == api.fail_scope else 201
            result = {"id": f"key-{api.projects}-{scope}", "key": f"value-{api.projects}-{scope}"}
        elif self.path.startswith("/api/v1/keys/"):
            code = 503 if api.fail_cleanup else 204
        elif self.path == "/api/v1/me/logout":
            code = 204
        elif self.path == "/api/v1/flows" and self.command == "POST":
            code = 201
        elif self.path == "/api/v1/events":
            code = 202
        elif self.path == "/private":
            code, result = 401, {"password": "never-display-me"}
        elif self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", "/redirect-target")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        encoded = b"" if code == 204 else json.dumps(result).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    do_GET = do_POST = do_DELETE = handle_request


class VerificationClientTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = Thread(target=cls.server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True)
        cls.thread.start()
        cls.base = "http://127.0.0.1:" + str(cls.server.server_port)

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def setUp(self):
        self.server.calls = []
        self.server.projects = 0
        self.server.login_status = 200
        self.server.bootstrap_status = 201
        self.server.fail_scope = None
        self.server.fail_cleanup = False
        env = patch.dict(os.environ, {"V1_OWNER_EMAIL": "test@local.test", "V1_OWNER_PASSWORD": "test-password"}, clear=True)
        env.start()
        self.addCleanup(env.stop)
        output = redirect_stdout(io.StringIO())
        output.__enter__()
        self.addCleanup(output.__exit__, None, None, None)

    def client(self, session=True):
        return VerificationClient(self.base, {"BOOTSTRAP_KEY": "bootstrap-test-key"}, session)

    def calls(self, method=None, path=None):
        return [x for x in self.server.calls if (method is None or x[0] == method) and (path is None or x[1] == path)]

    def test_bootstrap_keeps_initial_setup_credentials_and_does_not_login(self):
        with self.client(False) as client:
            project = client.provision("First setup")
            self.assertEqual(project.bootstrap_credentials["apiKey"], "legacy-test-key")
            self.assertEqual(project.headers("POST", "/api/v1/flows"), {"X-API-Key": "legacy-test-key"})
        self.assertFalse(self.calls(path="/api/v1/auth/login"))
        self.assertFalse(self.calls("DELETE"))

    def test_session_projects_use_scoped_keys_and_cleanup_only_their_keys(self):
        with self.client() as client:
            a, b = client.provision("A"), client.provision("B")
            self.assertNotEqual(a.project_id, b.project_id)
            self.assertIsNone(a.bootstrap_credentials)
            client.request("POST", "/api/v1/flows", {}, a, expected=201)
            client.request("POST", "/api/v1/events", {}, a, expected=202)
            client.request("GET", "/api/v1/flows", project=b)
            client.request("GET", "/private", expected=401)
        flow = self.calls("POST", "/api/v1/flows")[0][2]
        self.assertEqual(flow["x-project-id"], "project-1")
        self.assertIn("authorization", flow)
        event = self.calls("POST", "/api/v1/events")[0][2]
        self.assertEqual(event["x-api-key"], "value-1-ingest")
        self.assertNotIn("authorization", event)
        read = self.calls("GET", "/api/v1/flows")[0][2]
        self.assertEqual(read["x-api-key"], "value-2-read")
        self.assertNotIn("authorization", self.calls(path="/private")[0][2])
        self.assertEqual({c[1] for c in self.calls("DELETE")}, {
            "/api/v1/keys/key-1-read", "/api/v1/keys/key-1-ingest",
            "/api/v1/keys/key-2-read", "/api/v1/keys/key-2-ingest"})
        self.assertEqual(len(self.calls(path="/api/v1/me/logout")), 1)

    def test_failure_inside_checks_still_cleans_up(self):
        with self.assertRaisesRegex(AssertionError, "business-check"):
            with self.client() as client:
                client.provision("Failure")
                raise AssertionError("business-check")
        self.assertEqual(len(self.calls("DELETE")), 2)
        self.assertEqual(len(self.calls(path="/api/v1/me/logout")), 1)

    def test_partial_key_creation_cleans_up_the_first_key(self):
        self.server.fail_scope = "ingest"
        with self.assertRaises(VerificationError):
            with self.client() as client:
                client.provision("Partial")
        self.assertEqual([x[1] for x in self.calls("DELETE")], ["/api/v1/keys/key-1-read"])
        self.assertEqual(len(self.calls(path="/api/v1/me/logout")), 1)

    def test_bad_login_never_falls_back_to_bootstrap(self):
        self.server.login_status = 401
        with self.assertRaises(VerificationError):
            with self.client():
                self.fail("Login must fail")
        self.assertFalse(self.calls(path="/api/v1/bootstrap/projects"))
        self.assertFalse(self.calls(path="/api/v1/me/projects"))

    def test_closed_bootstrap_explains_session_mode(self):
        self.server.bootstrap_status = 409
        with self.assertRaisesRegex(VerificationError, "--session"):
            with self.client(False) as client:
                client.provision("Closed")

    def test_error_does_not_include_response_secrets(self):
        with self.assertRaises(VerificationError) as caught:
            self.client().request("GET", "/private")
        self.assertNotIn("never-display-me", str(caught.exception))

    def test_redirect_is_not_followed(self):
        with self.assertRaises(VerificationError):
            self.client().request("GET", "/redirect", headers={"X-API-Key": "test-only-secret"})
        self.assertFalse(self.calls(path="/redirect-target"))

    def test_cleanup_failure_is_reported_and_logout_still_attempted(self):
        self.server.fail_cleanup = True
        with redirect_stderr(io.StringIO()), self.assertRaisesRegex(VerificationError, "temizliği"):
            with self.client() as client:
                client.provision("Cleanup")
        self.assertEqual(len(self.calls("DELETE")), 2)
        self.assertEqual(len(self.calls(path="/api/v1/me/logout")), 1)


if __name__ == "__main__":
    unittest.main()
```

## 5. `frontend/src/lib/time.ts`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```typescript
// Süre metinleri ve son zaman ölçeri için yardımcılar. Sunucu saatine göre hesaplanır.

export function spanText(seconds: number) {
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s} sn`;
  const m = Math.floor(s / 60);
  if (m < 60) return s % 60 && m < 10 ? `${m} dk ${s % 60} sn` : `${m} dk`;
  const h = Math.floor(m / 60);
  if (h < 24) return m % 60 ? `${h} sa ${m % 60} dk` : `${h} sa`;
  const d = Math.floor(h / 24);
  return h % 24 ? `${d} gün ${h % 24} sa` : `${d} gün`;
}

export type Deadline = {
  ratio: number;       // 0–1 arasında dolan pay; geçmişse 1
  late: boolean;       // son zaman geçti mi
  text: string;        // "12 dk kaldı" ya da "3 dk geçti"
  allowance: string;   // tanınan toplam süre
};

export function deadline(startedAt: string | null, deadlineAt: string | null, now = Date.now()): Deadline | null {
  if (!startedAt || !deadlineAt) return null;
  const start = Date.parse(startedAt), end = Date.parse(deadlineAt);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return null;
  const total = (end - start) / 1000;
  const remaining = (end - now) / 1000;
  const late = remaining < 0;
  return {
    ratio: late ? 1 : Math.min(1, Math.max(0, (now - start) / (end - start))),
    late,
    text: late ? `${spanText(-remaining)} geçti` : `${spanText(remaining)} kaldı`,
    allowance: spanText(total),
  };
}

export function elapsedBetween(from: string | null, to: string | null) {
  if (!from || !to) return null;
  const a = Date.parse(from), b = Date.parse(to);
  if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
  return (b - a) / 1000;
}

// İş olaylarının zaman farkını gösterir; ağdaki teslim sırasını ölçmez.
export function stepTiming(seconds: number, allowance: number): { tone: string; text: string } {
  if (seconds < 0) {
    const difference = -seconds < 1 ? "1 saniyeden az" : spanText(-seconds);
    return {
      tone: "late",
      text: `Zaman sırası tutarsız: bu adımın olay zamanı önceki adımdan ${difference} önce. Kaynak olay zamanlarını kontrol edin.`,
    };
  }
  return {
    tone: seconds > allowance ? "late" : "ok",
    text: `${spanText(seconds)} sonra gerçekleşti, ${spanText(allowance)} tanınmıştı`,
  };
}
```

## 6. `frontend/src/app/transactions/[flow]/[transactionId]/page.tsx`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```tsx
import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getTransaction } from "@/lib/api";
import type { DelayInfo, TransactionDetail } from "@/lib/types";
import { Badge, DeadlineMeter, Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import { elapsedBetween, spanText, stepTiming } from "@/lib/time";

export const dynamic = "force-dynamic";

export default async function Detail({ params }: {
  params: Promise<{ flow: string; transactionId: string }>;
}) {
  const { flow, transactionId } = await params;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow) || !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(transactionId)) notFound();
  let item: TransactionDetail;
  try { item = await getTransaction(flow, transactionId); }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="İşlem yüklenemedi" description={apiMessage(error)} />;
  }
  const delay: DelayInfo = item.delay ?? {
    state: "pending_evaluation", step: null, startedAt: null, deadlineAt: null, checkedAt: null,
  };
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  const expected = steps.find(step => step.code === delay.step);
  const waiting = delay.state === "waiting" || delay.state === "overdue";
  const unprocessed = item.acceptedEventCount - item.processedEventCount;

  return <>
    <PageHead crumb={{ href: `/?${new URLSearchParams({ flow })}`, label: "İşlem listesi" }}
      title={<span className="mono">{item.transactionId}</span>}
      lede={<>{item.flow} akışı. {number(item.acceptedEventCount)} bildirim kabul edildi, {number(item.processedEventCount)} işlendi.</>}>
      <Badge value={item.status} /><RefreshControls />
    </PageHead>

    {unprocessed > 0 && <p className="callout" style={{ marginBottom: 20 }}>
      <strong>{number(unprocessed)} bildirim işlenmeyi bekliyor.</strong> Son durum bu görünüme henüz yansımamış olabilir; birkaç saniye sonra yenileyin.
    </p>}
    {delay.state === "overdue" && <p className="callout warn" style={{ marginBottom: 20 }}>
      <strong>{expected?.name ?? delay.step} adımı tanınan sürede bildirilmedi.</strong> Bu bir gecikmedir; kaynak sistem hata bildirmediği sürece işlem başarısız sayılmaz.
    </p>}

    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Adımlar</h2><p>Her adımın süresi bir önceki adımın gerçekleşme anından başlar.</p></div><span className="count">{steps.length} adım</span></div>
        <div className="surface-body">
          <ol className="rail">{steps.map((step, index) => {
            const previous = index > 0 ? steps[index - 1] : null;
            const isExpected = step.code === delay.step && waiting;
            const dot = step.observation === "completed" ? "completed" : step.observation === "failed" ? "failed" : isExpected ? `expected ${delay.state}` : "";
            const line = step.observation === "completed" ? "done" : isExpected ? "dashed" : "";
            const took = previous && step.occurredAt ? elapsedBetween(previous.occurredAt, step.occurredAt) : null;
            const timing = took !== null && step.maxDelaySeconds !== null ? stepTiming(took, step.maxDelaySeconds) : null;
            return <li key={step.code}>
              <div className="mark"><span className={`dot ${dot}`}>{step.observation === "completed" ? "✓" : step.observation === "failed" ? "!" : index + 1}</span><span className={`line ${line}`} /></div>
              <div className="step">
                <div className="step-head"><h3>{step.name}</h3><Badge value={step.observation} /></div>
                <div className="step-meta"><span className="mono">{step.code}</span>
                  {step.occurredAt && <Time value={step.occurredAt} />}</div>
                {step.maxDelaySeconds === null
                  ? <span className="gap">Başlangıç adımı</span>
                  : timing
                    ? <span className={`gap ${timing.tone}`}>{timing.text}</span>
                    : isExpected
                      ? <DeadlineMeter startedAt={delay.startedAt} deadlineAt={delay.deadlineAt} state={delay.state} />
                      : <span className="gap">{spanText(step.maxDelaySeconds)} tanınan süre</span>}
                {step.error && <pre className="error-content" style={{ marginTop: 10 }}>{step.error}</pre>}
              </div>
            </li>;
          })}</ol>
        </div>
      </section>

      <section className="surface">
        <div className="surface-head"><h2>Süre takibi</h2><Badge value={delay.state} delay /></div>
        <div className="surface-body">
          <dl className="facts">
            <div><dt>Beklenen adım</dt><dd>{expected?.name || delay.step || "—"}</dd></div>
            <div><dt>Bekleme başlangıcı</dt><dd className="time">{delay.startedAt ? <Time value={delay.startedAt} /> : "—"}</dd></div>
            <div><dt>Beklenen son zaman</dt><dd className="time">{delay.deadlineAt ? <Time value={delay.deadlineAt} /> : "—"}</dd></div>
            <div><dt>Son kontrol</dt><dd className="time">{delay.checkedAt ? <Time value={delay.checkedAt} /> : "—"}</dd></div>
          </dl>
          {waiting && <DeadlineMeter startedAt={delay.startedAt} deadlineAt={delay.deadlineAt} state={delay.state} />}
          <p className="help">Süre kontrolü, worker tarafından düzenli aralıklarla yapılır. Süre dolduğunda alarm açılır; eksik adım bildirilince alarm kendiliğinden kapanır.</p>
        </div>
      </section>
    </div>

    <section className="surface">
      <div className="surface-head"><div><h2>Gelen bildirimler</h2><p>En yeni bildirim en üstte.</p></div>
        <span className="count">{number(item.events.length)} bildirim</span></div>
      <div className="table-wrap"><table><thead><tr><th>Adım</th><th>Bildirim kimliği</th><th>Sonuç</th><th>Gerçekleşme</th><th>Ulaşma</th><th>Hata</th></tr></thead>
        <tbody>{[...item.events].reverse().map(event => <tr key={event.eventId}>
          <td>{steps.find(s => s.code === event.step)?.name ?? event.step}<small className="mono">{event.step}</small></td>
          <td className="mono" style={{ fontSize: 12 }}>{event.eventId}</td>
          <td><Badge value={event.status} /></td>
          <td className="nowrap"><Time value={event.occurredAt} /></td><td className="nowrap"><Time value={event.receivedAt} /></td>
          <td>{event.error ? <span className="error-content" style={{ display: "inline-block", padding: "4px 8px" }}>{event.error}</span> : <span className="muted">—</span>}</td>
        </tr>)}</tbody></table></div>
      {item.events.length === 0 && <div className="empty"><h3>Henüz işlenmiş bildirim yok</h3><p>İlk bildirim işlendiğinde burada listelenir.</p></div>}
    </section>
    <p className="help"><Link href={`/flows/view/${encodeURIComponent(item.flow)}`} prefetch={false}>Bu akışın tanımını görüntüle</Link></p>
  </>;
}
```

## 7. `frontend/src/app/operations/page.tsx`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```tsx
import { api, apiMessage } from "@/lib/api";
import type { Operations } from "@/lib/pilot-types";
import { Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";

export const dynamic = "force-dynamic";

const actionNames: Record<string, string> = {
  "member.owner": "Kullanıcı proje sahibi yapıldı", "member.operator": "Kullanıcı operatör yapıldı",
  "member.viewer": "Kullanıcı izleyici yapıldı", "member.remove": "Kullanıcı projeden çıkarıldı",
  "key.create": "Anahtar oluşturuldu", "key.revoke": "Anahtar iptal edildi",
  "channel.create": "Kanal oluşturuldu", "channel.enable": "Kanal etkinleştirildi", "channel.disable": "Kanal kapatıldı",
  "alarm.acknowledge": "Alarm görüldü olarak işaretlendi", "delivery.retry": "Gönderim yeniden denendi",
  "project.create": "Proje oluşturuldu", "owner.bootstrap": "İlk proje sahibi kuruldu",
};

export default async function OperationsPage() {
  let result: Operations;
  try { result = await api<Operations>("/operations"); }
  catch (e) { return <Message title="Sistem görünümü açılamadı" description={apiMessage(e)} />; }
  const c = result.counts;
  const recent = Boolean(c.worker_seen_at) && Date.parse(c.checked_at) - Date.parse(c.worker_seen_at as string) < 20000;
  const stats: [string, number, boolean][] = [
    ["Kuyruğa aktarılacak olay", c.outbox_pending, c.outbox_pending > 0 && Boolean(c.oldest_outbox_at) && Date.parse(c.checked_at) - Date.parse(c.oldest_outbox_at as string) > 60000],
    ["İşlenmeyi bekleyen bildirim", c.events_pending, false],
    ["Gönderim bekleyen alarm bildirimi", c.notifications_pending, false],
    ["Denemesi duran bildirim", c.notifications_dead, c.notifications_dead > 0],
  ];
  return <>
    <PageHead title="Sistem" lede="Seçili projenin bekleyen işleri, worker yaşam sinyali ve yönetim işlemlerinin geçmişi.">
      <RefreshControls />
    </PageHead>
    <div className="stat-grid">
      {stats.map(([name, value, attention]) => <div className={`stat ${attention ? "attention" : ""}`} key={name}><p>{name}</p><strong>{number(value)}</strong></div>)}
    </div>
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Worker</h2></div>
        <div className="surface-body">
          <div className="heartbeat"><span className={`lamp ${c.worker_seen_at ? (recent ? "on" : "off") : ""}`} />
            <strong>{recent ? "Yaşam sinyali güncel" : c.worker_seen_at ? "Yaşam sinyali gecikmiş" : "Henüz yaşam sinyali yok"}</strong></div>
          <dl className="facts">
            <div><dt>Son sinyal</dt><dd className="time"><Time value={c.worker_seen_at} /></dd></div>
            <div><dt>En eski bekleyen aktarım</dt><dd className="time"><Time value={c.oldest_outbox_at} /></dd></div>
            <div><dt>Kontrol zamanı</dt><dd className="time"><Time value={c.checked_at} /></dd></div>
          </dl>
          <p className="help">Yaşam sinyali worker sürecinin çalıştığını gösterir; kuyruk bağlantısının sağlıklı olduğunu tek başına kanıtlamaz. Bekleyen sayılar artıyorsa worker ve RabbitMQ günlüklerine bakın.</p>
        </div>
      </section>
      <section className="surface">
        <div className="surface-head"><h2>Sayılar ne anlama geliyor</h2></div>
        <div className="surface-body">
          <p className="help"><strong>Kuyruğa aktarılacak olay:</strong> API'nin kabul edip henüz kuyruğa yazmadığı bildirimler. Bir dakikadan uzun süre bekleyen varsa dikkat gerekir.</p>
          <p className="help"><strong>İşlenmeyi bekleyen bildirim:</strong> API'nin kabul ettiği ve worker'ın henüz işlemediği bütün olaylar. Henüz kuyruğa aktarılmamış olaylar da bu sayıya dahildir; iki sayıyı toplamayın.</p>
          <p className="help"><strong>Gönderim bekleyen alarm bildirimi:</strong> E-posta ya da webhook için sırada bekleyen alarm iletileri.</p>
          <p className="help"><strong>Denemesi duran bildirim:</strong> Deneme sınırına ulaşmış veya kalıcı bir ret nedeniyle durmuş iletiler. Hata nedenini giderdikten sonra alarm detayından yeniden deneme başlatılabilir.</p>
        </div>
      </section>
    </div>
    <section className="surface">
      <div className="surface-head"><div><h2>Yönetim geçmişi</h2><p>Son 100 yönetim işlemi.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Kullanıcı</th><th>İşlem</th><th>Kayıt</th></tr></thead><tbody>
        {result.audit.map((a, i) => <tr key={i}><td className="nowrap"><Time value={a.created_at} /></td><td>{a.actor ?? "Sistem"}</td>
          <td>{actionNames[a.action] ?? a.action}<small className="mono">{a.action}</small></td><td><code>{a.target}</code></td></tr>)}
      </tbody></table></div>
      {result.audit.length === 0 && <div className="empty"><h3>Henüz yönetim işlemi yok</h3><p>Kullanıcı, anahtar ve kanal değişiklikleri burada listelenir.</p></div>}
    </section>
  </>;
}
```

## 8. `.github/workflows/ci.yml`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

```yaml
name: IntegraOps checks

on:
  push:
  pull_request:

jobs:
  backend:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    env:
      V1_OWNER_EMAIL: ci-owner@local.test
      V1_OWNER_NAME: CI Owner
      # A disposable account in this job's isolated local database, never a deployment credential.
      V1_OWNER_PASSWORD: ci-only-test-password-123456
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Build
        run: dotnet build IntegraOps.slnx -c Release
      - name: Unit tests
        run: dotnet test IntegraOps.slnx -c Release --no-build
      - name: Verification client contract tests
        run: python3 -m unittest discover -s scripts/tests -v
      - name: Local configuration
        run: |
          python3 scripts/init_local.py
          python3 scripts/prepare_v1.py
      - name: Start actual services
        run: docker compose up --build -d
      - name: Verify the event pipeline
        run: python3 scripts/verify_api.py
      - name: Configure V1 and start the source sample
        run: |
          python3 scripts/setup_v1.py
          docker compose --profile demo up --build -d source
      - name: Verify V1 modules
        run: python3 scripts/verify_v1.py --load 50
      - name: Verify pipeline and delays after owner setup
        run: |
          python3 scripts/verify_api.py --session
          python3 scripts/verify_delays.py --session
      - name: Service logs on failure
        if: failure()
        run: docker compose logs --no-color --tail=150 api worker migrate
      - name: Stop test services
        if: always()
        run: docker compose --profile demo down

  panel:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: npm
          cache-dependency-path: frontend/package-lock.json
      - run: npm ci
        working-directory: frontend
      - run: npm run build
        working-directory: frontend
```

## 9. `README.md`

**İşlem:** Dosyanın tamamını aşağıdaki içerikle değiştir.

````markdown
# IntegraOps — V1

IntegraOps, farklı sistemlerden gelen iş olaylarını aynı işlem altında birleştirir; beklenen adımları, gecikmeleri ve kaynak sistemin bildirdiği hataları izler. Kullanıcılar panelden işlem ve alarm geçmişini inceler. IntegraOps çalışma adıdır.

**Mevcut aşama:** V1'in olay takibi, panel, kullanıcı/proje yetkileri, alarm ve bildirim modülleri uygulanmıştır. 17 Eylül 2026 yerel kabul kaydı vardır. Gerçek müşteri entegrasyonu, kesinti ve tam kurtarma provası ile canlı ortam kabulü ayrıca tamamlanmalıdır.

## Mevcut modüller

- Güvenilir olay kabulü: PostgreSQL transaction + outbox, RabbitMQ ve worker.
- Olay kimliğiyle tekilleştirme, ters teslim sırasını olay zamanına göre değerlendirme.
- İşlem listesi/detayı, durum özeti, akış/durum/gecikme filtresi ve sayfalama.
- Adım bazlı süre, periyodik gecikme kontrolü, geç tamamlanma.
- Akış oluşturma, detay, kopyalama ve panelden gerçek test bildirimi.
- Giriş/çıkış/parola değiştirme, proje seçimi, sahip/operatör/izleyici rolleri.
- Gönderim ve okuma API anahtarları; iptal ve eski anahtarla uyumluluk.
- Gecikme/hata alarmları, görüldü kaydı, otomatik kapanış ve geçmiş.
- Kalıcı e-posta/webhook teslimatı, imza, yeniden deneme ve kanal yönetimi.
- Sistem görünümü, bekleyen işler, worker yaşam sinyali ve yönetim geçmişi.
- Kaynakta outbox kullanan .NET örneği ve yerel işletim betikleri.

## Gerekenler

- Docker ve Docker Compose v2.
- Python 3.10 veya üzeri.
- Panel için Node.js 24 ve npm.
- Backend'i yerel derlemek/test etmek için .NET 10 SDK. Docker derlemesi SDK'yı imaj içinde kullanır.

Paket sürümleri proje dosyalarında sabittir. Veritabanı erişiminde Npgsql ve açık SQL kullanılır.

## V1 zaten kuruluysa

Mevcut ortam ayarlarını ve veri birimlerini koruyun. Bu adımlar ilk kullanıcıyı yeniden oluşturmaz:

```bash
python3 scripts/prepare_v1.py
docker compose up --build -d
docker compose --profile demo up --build -d source
```

Panel için ayrı terminalde:

```bash
cd frontend
npm ci
npm run typecheck
npm run build
npm run start
```

Panel önceden 3000 portunda çalışıyorsa yeni sürümü başlatmadan önce eski panel sürecini durdurun. `prepare_v1.py` mevcut ayarları koruyarak eksik V1 yerel ayarlarını tamamlar. Kaynak örneği henüz yapılandırılmadıysa proje kökünde `python3 scripts/setup_v1.py` çalıştırın ve ardından kaynak servisini yeniden başlatın.

## İlk kez, boş ortamda kurulum

Aşağıdaki sıra ilk sahibi bulunmayan yeni veritabanı içindir:

```bash
python3 scripts/init_local.py
python3 scripts/prepare_v1.py
docker compose up --build -d
python3 scripts/verify_api.py
python3 scripts/setup_v1.py
docker compose --profile demo up --build -d source
```

`verify_api.py`, iki başlangıç test projesi açar ve ilk sahip kurulumunun kullanacağı demo proje erişimini yerel olarak saklar. `setup_v1.py` ilk sahibin bilgilerini ister; `pilot-source` akışını, yerel bildirim kanallarını ve kaynak uygulamasının gönderim anahtarını hazırlar. Parolalar gizli terminal girdisiyle alınır.

Paneli yukarıdaki panel komutlarıyla açın. İlk sahip kurulduğunda bootstrap ile yeni proje açma yolu kapanır. Sonraki projeler panelden oluşturulur.

## Yerel adresler

Varsayılan ayarlarla:

| Adres | Görev |
|---|---|
| http://localhost:3000 | Panel |
| http://127.0.0.1:8080/health/live | API süreci |
| http://127.0.0.1:8080/health/ready | Veritabanı ve migration 003 hazırlığı |
| http://localhost:8025 | Mailpit yerel e-posta kutusu |
| http://localhost:8082/receipts | Doğrulanmış yerel webhook kayıtları |
| http://localhost:8081/outbox | Demo kaynak gönderim durumu |
| http://127.0.0.1:15679 | RabbitMQ yönetim arayüzü |

Mailpit, webhook alıcısı ve OrderSource yerel demo araçlarıdır. Canlı Compose bunları yayınlamaz. API readiness veritabanı/şemayı kontrol eder; RabbitMQ veya bütün worker görevlerinin sağlıklı olduğunu kanıtlamaz.

## Doğrulama

### Servis gerektirmeyen kontroller

```bash
dotnet build IntegraOps.slnx -c Release
dotnet test IntegraOps.slnx -c Release --no-build
python3 -m unittest discover -s scripts/tests -v
```

Python testleri yerel sahte bir HTTP sunucusuyla doğrulama istemcisinin oturum, anahtar kapsamı, temizlik ve yönlendirme davranışını sınar. Gerçek backend/kuyruk testinin yerine geçmez.

### Kurulu V1 ortamında

API, PostgreSQL ve worker çalışırken:

```bash
python3 scripts/verify_api.py --session
python3 scripts/verify_delays.py --session
```

Bu komutlar mevcut hesabınızla giriş yapar. E-posta/parola terminalde sorulur; otomatik testte `V1_OWNER_EMAIL` ve `V1_OWNER_PASSWORD` ortam değişkenleri kullanılabilir. Her koşu yeni test projesi oluşturur. Akış oluşturma kullanıcı oturumuyla, olay gönderme `ingest`, veri okuma `read` anahtarıyla yapılır.

Test kayıtları panelde kalır. Bu iki betiğin oluşturduğu geçici anahtarlar ve oturum normal bitişte, kontrol hatasında veya Ctrl+C'de kapatılmaya çalışılır. Zorla süreç sonlandırma/ağ kesintisinde temizleme tamamlanmayabilir; test projesinin Ayarlar ekranından kalan anahtarları iptal edin. Oturumlu koşu ilk kurulumun kayıtlı demo erişimini değiştirmez.

`verify_delays.py` yaklaşık 30 saniyelik gerçek süre aşımını bekler; toplam süre ortamın hızına bağlıdır.

Mailpit, webhook ve kaynak örneği de çalışırken kapsamlı V1 kontrolü:

```bash
python3 scripts/verify_v1.py --load 100
```

Kesinti provası için ayrı, uygun yerel test zamanında:

```bash
python3 scripts/verify_v1.py --load 100 --faults
```

`--faults`, aynı Compose projesindeki API ve RabbitMQ'yu geçici olarak durdurur. Sonlandırma nedeniyle kapalı kalırlarsa `docker compose start api rabbitmq` ile açın. Bu test çalışan yerel servisleri etkiler. Yeni oturumlu istemcinin temizlik davranışı `verify_v1.py` için değiştirilmemiştir; toplu V1 betiği kendi test kayıtlarını ve anahtarlarını inceleme amacıyla bırakır.

### İlk kullanıcıdan önce

`verify_api.py` ve `verify_delays.py` bayraksız çalıştırıldığında eski bootstrap yöntemini kullanır. V1 kurulmuşsa `--session` kullanmanız gerektiğini açıklar. Bayraksız `verify_api.py` yalnızca ilk kurulum sırası içindir; bu koşu kayıtlı demo proje erişimini günceller.

## Elle alarm senaryosu

Kaynak örneği çalışırken yeni bir kimlikle `POST http://localhost:8081/orders` çağrısı yapın:

```json
{"transactionId":"DEMO-MANUAL-001"}
```

`pilot-source` akışında ERP tamamlanır ve fatura beklenir. Yaklaşık 30 saniye ve tarama aralığı sonrasında gecikme alarmı açılır. “Gördüm” alarmı kapatmaz.

Ardından `POST http://localhost:8081/orders/DEMO-MANUAL-001/invoice` adresine `{}` gönderin. İşlem tamamlanmalı, alarm kapanmalı, açılış/kapanış bildirimleri Mailpit ve webhook alıcısında görünmelidir.

## Korunan iş kuralları

- API'den 202 almak, olay ve outbox kaydının kalıcı kaydedildiği anlamına gelir. İşlenmiş olması ayrıca izlenir.
- Aynı projede aynı `eventId` ve aynı içerik tekrar gönderilebilir; farklı içerik 409 üretir.
- `transactionId` aynı işin adımlarında sabittir. Her yeni olay ayrı `eventId` kullanır.
- `occurredAt` saat dilimi içermelidir; adımın durumu teslim sırasına değil olay zamanına göre seçilir.
- `not_observed`, bildirimin görülmediğini belirtir. Dış sistemin işi yapmadığını kanıtlamaz.
- Gecikme `failed` anlamına gelmez; kaynak açıkça hata bildirmelidir.
- Akışlar 2–30 sıralı adımdır. İlk adımda süre yoktur; diğerleri 1–86400 saniyedir.
- Süreler mevcut akışta değiştirilmez. Yeni kodla kopya oluşturulur; kaynak yeni işlemler için yeni kodu kullanır.
- İşlem detayındaki süre farkı iş olaylarının zamanları arasındadır. Negatif fark zaman tutarsızlığı olarak gösterilir; işlem durumunu panel değiştirmez.
- Sistem ekranındaki işlenmemiş olay sayısı, henüz kuyruğa yayımlanmamış olayları da içerir. İki sayaç toplanmaz.
- Panelden oluşturulan TEST- işlemleri gerçek kayıtlardır ve sayılara dahildir.

## Yedek ve geri yükleme

```bash
bash scripts/backup_local.sh
bash scripts/check_restore.sh backups/OLUSAN-KLASOR-ADI
```

İkinci komuttaki yolu ilk komutun oluşturduğu gerçek klasörle değiştirin. Geri yükleme kontrolü ayrı veritabanı oluşturur; çalışan veritabanını değiştirmez. Oluşturduğu kontrol veritabanını inceleme için bırakır.

Veritabanıyla birlikte Data Protection anahtarlarını koruyun. Mevcut kontrol veritabanı geri yükleme, kayıt sayımı ve anahtar arşivi okunmasını sınar; geri yüklenen uygulamadan giriş ve bildirim göndermeyi doğrulayan tam kurtarma provası ayrıca yapılmalıdır. Yerel betikler canlı Compose'u otomatik hedeflemez.

## Canlı hazırlığı ve sıradaki işler

`compose.live.yml`, `deploy/Caddyfile` ve Dockerfile'lar hazırdır. Canlı kurulum için alan adları, ayrı sırlar, SMTP, webhook izin listesi, ilk sahip veya veri taşıma yolu ve canlı yedekleme adımları hazırlanmalıdır. Şablonların varlığı canlı kabulün tamamlandığı anlamına gelmez.

V1 kapanışında: güncel kodu Git'e kaydetme, kesinti testi, tam kurtarma provası, gerçek kaynak bağlantısı ve kontrollü canlı pilot. Sonraki kapsam: arama/tarih filtreleri, kullanıcı daveti/hesap kurtarma, test verisi ayrımı ve veri saklama. Python/Go için bağımsız servis kararı pilot ihtiyacına göre verilecektir.

## Belgeler ve geçmiş doğrulama

- `docs/verification.md`: İlk teslim ve 17 Eylül V1 doğrulama kayıtları. Tarihsel sonuçlardır.
- `docs/v1-paket-01.md`: Bu düzeltme paketinin değişiklikleri ve doğrulama sınırları.
- `IntegraOps_V1_00_Kurulum_ve_Test.md`: V1 modüllerinin ayrıntılı kurulum açıklaması.
- `docs/architecture/0001-first-slice.md` ve `docs/sistem-mimarisi.md`: İlk backend aşamasının kararları; güncel özellik envanteri değildir.
- Tam kod rehberleri tarihsel kaynaklardır. Panel rehberindeki eski dosyaları güncel panelin üzerine topluca yazmayın.

17 Eylül kaydı: 51 birim testi, 28 bütünleşik kontrol, panel derlemesi, elle alarm/bildirim ve veritabanı geri yükleme başarılı. Aynı kayıt kesinti ve canlı ortam provasının yapılmadığını belirtir. Yeni değişiklikler için bu sonuçlar tekrar koşulmuş sayılmaz.
````

## 10. `docs/v1-paket-01.md`

**İşlem:** Yeni dosya oluştur.

````markdown
# V1 Paket 01 — Panel doğruluğu ve oturumlu doğrulama

Tarih: 21 Eylül 2026. Temel alınan sürüm: 18 Eylül'de incelenen proje arşivindeki çalışma dosyaları.

## Değişiklikler

1. İşlem detayında negatif olay zamanı farkı sıfıra sıkıştırılmak yerine açık bir zaman sırası uyarısı gösterir. Pozitif süre “sonra gerçekleşti” olarak adlandırılır; olay zamanı ile bildirim ulaşma zamanı ayrılır. Bu yalnızca sunum düzeltmesidir; işlem ve alarm kuralları değişmez.
2. Sistem ekranı işlenmemiş olay sayısının henüz yayımlanmamış olayları da içerdiğini açıklar. Duran teslimatların yalnızca deneme sınırından değil kalıcı retten de kaynaklanabileceği belirtilir.
3. `verify_api.py` ve `verify_delays.py` için açık `--session` seçeneği eklendi. V1 kullanıcısıyla giriş yapıp her koşuda ayrı test projeleri açar. Proje içi akış oluşturma oturumla, gönderim ingest, okuma read anahtarıyla yapılır.
4. Geçici anahtarlar normal bitişte ve kontrol hatasında iptal edilir; yalnızca betiğin açtığı oturum kapatılır. Temizlik başarısızsa hata raporlanır; asıl test hatası varsa korunur. Ağ kesintisi/zorla kapatma durumunda elle temizlik gerekebilir.
5. Bayraksız ilk kurulum modu korunur. İlk kullanıcı varsa bootstrap 409 için açıkça `--session` önerilir. Oturumlu koşu ilk kurulumun demo erişim dosyasını değiştirmez.
6. README güncel V1 modüllerini ve boş ortam/kurulu ortam ayrımını anlatır. CI'ye doğrulama istemcisinin testleri ve ilk sahip kurulduktan sonraki oturumlu senaryolar eklenir.

Yeni yardımcı: `scripts/verification_client.py`. Yeni otomatik kontroller: `scripts/tests/test_verification_client.py`. Yeni paket bağımlılığı ve SQL migration yoktur.

## Bu pakette gerçekleştirilen doğrulama

| Kontrol | Sonuç |
|---|---|
| Panel TypeScript kontrolü | Geçti (`tsc --noEmit --incremental false`) |
| Doğrulama istemcisi HTTP/oturum sözleşmesi | 9 test geçti; yerel sahte HTTP sunucusu |
| Süre gösterimi | 8 doğrudan kontrol geçti: negatif, saniye altı negatif, sıfır, normal, gecikmiş, zaman farkı ve eksik zaman |
| Python sözdizimi ve CLI yardım seçenekleri | Geçti |

Bu kontroller gerçek .NET API, PostgreSQL veya RabbitMQ'yu çalıştırmaz. Bu ortamda .NET/Docker bulunmadığı için üretim panel derlemesi, tarayıcı görsel kontrolü, backend testleri, gerçek oturumlu senaryolar ve CI koşusu burada yeniden doğrulanmadı. Önceki 51/28 başarılı test kaydı bu paket için yeni test sonucu sayılmaz.

## Mevcut V1'de kabul adımları

API, veritabanı ve worker açıkken proje kökünde:

```bash
python3 -m unittest discover -s scripts/tests -v
python3 scripts/verify_api.py --session
python3 scripts/verify_delays.py --session
```

Panel klasöründe:

```bash
npm run typecheck
npm run build
npm run start
```

Yeni paneli başlatmadan önce eski panel sürecini durdurun. İlk test iki proje, ikinci test bir proje oluşturur. Sonuçları panelden görebilirsiniz; bu projelere bildirim kanalı eklenmez. Gecikme testi 30 saniyelik gerçek süreyi bekler.

Mailpit, webhook alıcısı ve kaynak örneği de çalışırken:

```bash
python3 scripts/verify_v1.py --load 100
```

Kesinti provası ayrı bir adım olarak yerel servislerin durdurulabileceği zamanda `--faults` ile yapılır. Bu paket otomatik olarak servis durdurmaz, veri geri yüklemez veya canlı dağıtım yapmaz.

## Sonraki paket

Bu değişikliklerin yerel kabulünden sonra kesinti sonuçları ve tam uygulama kurtarma provası tamamlanır. Ardından ilk gerçek kaynak sistemi bağlantısı seçilir. Arama, tarih filtreleri, kullanıcı daveti, Python/Go servisleri bu paketin kapsamı değildir.

## Teknik referans

Sayfaların mevcut sunucu bileşeni yapısı korunur. Framework referansı: [Next.js Server and Client Components](https://nextjs.org/docs/app/getting-started/server-and-client-components).
````

## 11. Servis gerektirmeyen kontrol

Proje kökünde Python 3.10+ ile:

```bash
python3 -m unittest discover -s scripts/tests -v
```

Beklenen: 9 test ve `OK`. Bu testler yerel sahte HTTP sunucusunu kullanır; gerçek projelere veya veritabanına yazmaz.

Panel için:

```bash
cd frontend
npm run typecheck
npm run build
```

Bağımlılıklar kurulu değilse önce `npm ci` çalıştır. Paneli geliştirme modunda kullanıyorsan dosya değişiklikleri yeniden derlenir. Üretim modunda kullanıyorsan eski panel sürecini durdurduktan sonra `npm run start` ile yeni derlemeyi aç. 3000 portunda iki paneli birlikte başlatma.

Paneli canlı Compose içinde çalıştırıyorsan bu rehberdeki yerel komutlarla canlı ortamı değiştirme; bu paket önce yerelde kabul edilmelidir.

## 12. Mevcut V1 hesabıyla gerçek kontroller

Terminali tekrar proje kökünde aç. API, PostgreSQL ve worker çalışıyor olmalı. Mevcut yerel servislerin durumunu kontrol et:

```bash
docker compose ps
```

Kapalıysa mevcut kurulumu aç:

```bash
docker compose up -d
```

Sonra:

```bash
python3 scripts/verify_api.py --session
python3 scripts/verify_delays.py --session
```

Her betik e-posta ve parola sorar. Parola yazılırken görünmez. Otomatik ortamlarda mevcut `V1_OWNER_EMAIL` ve `V1_OWNER_PASSWORD` ortam değişkenleri de desteklenir; parolayı komut satırı argümanına veya bu rehbere yazma.

İlk test iki, gecikme testi bir yeni proje açar. Var olan iş projelerinin üyelerini, anahtarlarını veya akışlarını değiştirmez. Bu test projelerinde dış bildirim kanalı açılmaz. Gecikme testi yaklaşık 30 saniyelik süre aşımını gerçekten bekler.

Beklenen son mesajlar:

- API: `Başarılı: kalıcı olay işleme, tekrar gönderim, içerik çakışması, eşzamanlılık, sıra dışı teslim ve proje ayrımı.`
- Gecikme: `Başarılı: periyodik tarama, adım bazlı süre, tekrar gönderim, geç bildirim ve hata ayrımı.`

Panelde yeni test projelerini seçerek kayıtları görebilirsin. Geçici read/ingest anahtarları bitişte iptal edilir; yeni test oturumu kapatılır. Tarayıcıdaki mevcut oturumuna dokunulmaz. Ağ kesilirse veya süreç zorla kapatılırsa test erişimleri temizlenemeyebilir; belirtilen test projesindeki anahtarları Ayarlar'dan iptal et.

## 13. Kapsamlı V1 kontrolü

Mailpit, webhook ve kaynak örneği de hazır olduğunda:

```bash
docker compose --profile demo up -d source
python3 scripts/verify_v1.py --load 100
```

Bu komut kaynak örneğinin daha önce `setup_v1.py` ile yapılandırıldığını varsayar. Kurulum zaten yapıldıysa ilk kullanıcıyı yeniden oluşturmana gerek yok.

Kesinti denemesini ayrı zamanda, servislerin geçici olarak durdurulabileceği yerel ortamda yap:

```bash
python3 scripts/verify_v1.py --load 100 --faults
```

Bu seçenek API ve RabbitMQ'yu durdurup açar; varsayılan kabul komutlarına otomatik eklenmedi. `verify_v1.py` bu pakette değiştirilmedi ve kendi test anahtarlarını/projelerini inceleme için bırakır.

## 14. Elle panel kontrolü

- Sistem ekranında işlenmemiş olayların kuyruk öncesi kayıtları da içerdiği açık olmalı.
- Normal bir işlemde pozitif fark “sonra gerçekleşti” olarak görünmeli.
- Ardışık adımların iş olayı zamanları tersse sarı zaman tutarsızlığı açıklaması görünmeli; sıfır saniyelik normal süre gibi görünmemeli.
- İşlem durumları ve alarm kuralları aynı kalmalı; panel yalnızca gösterimi düzeltir.

## 15. Doğrulama durumu

Hazırlama ortamında panel TypeScript kontrolü, 9 HTTP/oturum testi, 8 süre kontrolü ve Python sözdizimi geçti. Patch'in kaynak çalışma dosyalarına uygulanması, tam dosyalarla eşleşmesi ve tersine uygulanarak başlangıca dönmesi de kontrol edildi.

.NET/Docker burada bulunmadığı için gerçek backend/kuyruk senaryoları, üretim panel derlemesi ve tarayıcı görsel kontrolü burada çalıştırılmadı. Yukarıdaki yerel kabul adımları bunları tamamlamak içindir. Eski 51 birim testi ve 28 V1 kontrolünün başarı kaydı yeni paket için tekrar koşulmuş sayılmaz.

Bu paket V1 kapanışının ilk kod adımıdır. Kesinti kabulü, tam uygulama kurtarma, gerçek ERP bağlantısı ve canlı pilot sonraki işlerdir.

Bu Markdown belgesindeki 10 dosyanın kodları, hazırlanan paket dosyalarıyla birebir karşılaştırıldı. Test sonuçları önceki paket hazırlama kontrollerine aittir; belgeye dönüştürme sırasında gerçek backend testleri yeniden çalıştırılmadı.
