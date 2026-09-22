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
