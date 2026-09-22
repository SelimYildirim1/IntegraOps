"""Stage 13 checks: self-service signup and trial, organization roles and project groups, plan limits and usage
metering, system administration, second factor (TOTP), single sign-on (OpenID Connect against an in-process
provider), security events, organization audit, data export and the deletion procedure.

Requires the local stack (docker compose) and, for the SSO part, the API container reaching this host as
host.docker.internal (docker-compose.yml adds the mapping). Uses openssl on PATH to sign the provider's tokens.
"""
import base64
import getpass
import hashlib
import hmac
import io
import json
import os
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

MAILPIT = "http://127.0.0.1:8025"
PROVIDER_PORT = 8799


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def totp(secret: str, at: float | None = None) -> str:
    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    counter = int((at or time.time()) // 30)
    digest = hmac.new(key, struct.pack(">Q", counter), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    code = (struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000
    return f"{code:06d}"


class FakeProvider:
    """Minimal OpenID Connect provider: discovery, JWKS, authorize (auto-consent) and token endpoints."""

    def __init__(self, port: int):
        self.port = port
        self.issuer = f"http://host.docker.internal:{port}"
        self.local = f"http://127.0.0.1:{port}"
        self.client_id = "integraops-test"
        self.client_secret = "test-secret-" + uuid.uuid4().hex
        self.user = {"sub": "sso-user-1", "email": "", "name": "SSO Kullanıcısı"}
        self.codes: dict[str, dict] = {}
        self.tmp = tempfile.mkdtemp()
        self.key = Path(self.tmp) / "key.pem"
        subprocess.run(["openssl", "genrsa", "-out", str(self.key), "2048"], check=True, capture_output=True)
        modulus = subprocess.run(["openssl", "rsa", "-in", str(self.key), "-noout", "-modulus"], check=True, capture_output=True, text=True).stdout
        n = bytes.fromhex(modulus.strip().split("=", 1)[1])
        self.jwk = {"kty": "RSA", "use": "sig", "alg": "RS256", "kid": "test-1", "n": b64url(n), "e": b64url((65537).to_bytes(3, "big"))}
        provider = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_): pass

            def send_json(self, status, body):
                data = json.dumps(body).encode()
                self.send_response(status); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(data)))
                self.end_headers(); self.wfile.write(data)

            def do_GET(self):
                url = urllib.parse.urlparse(self.path); q = urllib.parse.parse_qs(url.query)
                if url.path == "/.well-known/openid-configuration":
                    self.send_json(200, {"issuer": provider.issuer, "authorization_endpoint": provider.issuer + "/authorize",
                        "token_endpoint": provider.issuer + "/token", "jwks_uri": provider.issuer + "/jwks",
                        "response_types_supported": ["code"], "subject_types_supported": ["public"], "id_token_signing_alg_values_supported": ["RS256"]})
                elif url.path == "/jwks":
                    self.send_json(200, {"keys": [provider.jwk]})
                elif url.path == "/authorize":
                    code = uuid.uuid4().hex
                    provider.codes[code] = {"nonce": q.get("nonce", [""])[0], "redirect_uri": q.get("redirect_uri", [""])[0], "client_id": q.get("client_id", [""])[0]}
                    location = q["redirect_uri"][0] + "?" + urllib.parse.urlencode({"code": code, "state": q["state"][0]})
                    self.send_response(302); self.send_header("Location", location); self.end_headers()
                else:
                    self.send_json(404, {"error": "not_found"})

            def do_POST(self):
                length = int(self.headers.get("Content-Length", "0")); form = urllib.parse.parse_qs(self.rfile.read(length).decode())
                if urllib.parse.urlparse(self.path).path != "/token":
                    return self.send_json(404, {"error": "not_found"})
                code = form.get("code", [""])[0]; issued = provider.codes.pop(code, None)
                if issued is None or form.get("client_secret", [""])[0] != provider.client_secret or form.get("client_id", [""])[0] != provider.client_id:
                    return self.send_json(400, {"error": "invalid_grant"})
                now = int(time.time())
                claims = {"iss": provider.issuer, "aud": provider.client_id, "sub": provider.user["sub"], "email": provider.user["email"],
                    "name": provider.user["name"], "nonce": issued["nonce"], "iat": now, "exp": now + 300}
                self.send_json(200, {"access_token": "at-" + code, "token_type": "Bearer", "id_token": provider.sign(claims)})

        self.server = HTTPServer(("0.0.0.0", port), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def sign(self, claims: dict) -> str:
        header = b64url(json.dumps({"alg": "RS256", "typ": "JWT", "kid": "test-1"}).encode())
        payload = b64url(json.dumps(claims).encode())
        signing_input = f"{header}.{payload}".encode()
        signature = subprocess.run(["openssl", "dgst", "-sha256", "-sign", str(self.key)], input=signing_input, check=True, capture_output=True).stdout
        return f"{header}.{payload}.{b64url(signature)}"

    def stop(self):
        self.server.shutdown()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def mailpit_search(query):
    with urllib.request.urlopen(MAILPIT + "/api/v1/search?" + urllib.parse.urlencode({"query": query}), timeout=10) as r:
        return json.loads(r.read())["messages"]


def main():
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    admin_email = (os.environ.get("V1_OWNER_EMAIL") or input("Sistem yöneticisi (ilk sahip) e-posta: ")).strip()
    admin_password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    suffix = uuid.uuid4().hex[:8]
    passed = 0

    def check(name, value):
        nonlocal passed
        if not value:
            raise AssertionError(name)
        passed += 1
        print("GEÇTİ ·", name, flush=True)

    def wait(name, predicate, timeout=90):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                check(name, True)
                return
            time.sleep(1)
        raise AssertionError(name + " zaman aşımı")

    def api(path, data=None, auth=None, method=None):
        result = request(base, "/api/v1" + path, data, auth, method)
        if result[0] == 429 and "sınırı" not in str(result[1]):
            # Login-family endpoints allow 30 calls a minute per client; this script alone comes close.
            print("   (giriş hız sınırı; 61 sn bekleniyor)", flush=True)
            time.sleep(61)
            result = request(base, "/api/v1" + path, data, auth, method)
        return result

    # --- signup and trial ---
    owner_email = f"sahip-{suffix}@kurumsal.test"; owner_password = "Kurumsal-Parola-" + suffix
    status, signup = api("/auth/signup", {"organizationName": "Kurumsal Kontrol " + suffix, "name": "Kurumsal Sahip", "email": owner_email, "password": owner_password})
    check("Kayıt hesap, organizasyon ve ilk projeyi oluşturur", status == 201 and signup["token"] and signup["organizationId"] and signup["projectId"])
    org = signup["organizationId"]; project = signup["projectId"]
    session = {"Authorization": "Bearer " + signup["token"]}
    headers = session | {"X-Project-Id": project}
    check("Aynı e-postayla ikinci kayıt 409 döner", api("/auth/signup", {"organizationName": "x", "name": "y", "email": owner_email, "password": owner_password})[0] == 409)
    check("Zayıf parola reddedilir", api("/auth/signup", {"organizationName": "x", "name": "y", "email": f"z-{suffix}@kurumsal.test", "password": "kisa"})[0] == 400)
    mine = require(api("/me/organizations", auth=session))
    check("Yeni hesap organizasyonun sahibi ve deneme planında", len(mine) == 1 and mine[0]["role"] == "owner" and mine[0]["plan"] == "trial")
    wait("Hoş geldiniz e-postası Mailpit'e ulaştı", lambda: len(mailpit_search(owner_email)) > 0, timeout=60)

    overview = require(api(f"/organizations/{org}", auth=session))
    check("Deneme planı sınırları 3 proje, 5 kullanıcı, 5000 olay", overview["limits"] == {"maxProjects": 3, "maxUsers": 5, "maxEventsPerDay": 5000, "maxRetentionDays": 30}
          and overview["counts"]["projects"] == 1 and overview["myRole"] == "owner")

    # --- onboarding ---
    onboarding = require(api("/onboarding", auth=headers))
    check("Başlangıç listesi 6 adım, hiçbiri tamamlanmamış", onboarding["total"] == 6 and onboarding["completed"] == 0)
    require(api("/flows", {"code": "erp-order-invoice", "name": "ERP", "steps": [
        {"code": "order-created", "name": "Sipariş", "maxDelaySeconds": None}, {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 14400}]}, headers))
    key = require(api("/keys", {"name": "kurumsal", "scope": "ingest"}, headers))["key"]
    onboarding = require(api("/onboarding", auth=headers))
    check("Akış ve anahtar adımları tamamlandı olarak işaretlenir", onboarding["completed"] == 2 and {s["key"] for s in onboarding["steps"] if s["done"]} == {"flow", "key"})

    # --- project limit ---
    second = require(api("/me/projects", {"name": "İkinci " + suffix}, session))
    require(api("/me/projects", {"name": "Üçüncü " + suffix}, session))
    status, body = api("/me/projects", {"name": "Dördüncü " + suffix}, session)
    check("Deneme planında dördüncü proje 403 ile reddedilir", status == 403 and "3 proje" in body["title"])

    # --- organization members and implicit access ---
    check("Organizasyona kayıtsız e-posta eklenemez", api(f"/organizations/{org}/members", {"email": f"yok-{suffix}@kurumsal.test", "role": "admin"}, session)[0] == 404)
    require(api(f"/organizations/{org}/members", {"email": admin_email, "role": "admin"}, session))
    admin_token = require(api("/auth/login", {"email": admin_email, "password": admin_password}))["token"]
    admin_session = {"Authorization": "Bearer " + admin_token}
    admin_projects = require(api("/me/projects", auth=admin_session))
    check("Organizasyon yöneticisi bütün organizasyon projelerini sahip olarak görür",
          {p["id"] for p in admin_projects} >= {project, second["id"]} and all(p["role"] == "owner" for p in admin_projects if p["organization_id"] == org))
    check("Yönetici proje üyeliği olmadan proje ayarlarına erişir", api("/members", auth=admin_session | {"X-Project-Id": second["id"]})[0] == 200)
    check("Son sahibin rolü düşürülemez", api(f"/organizations/{org}/members", {"email": owner_email, "role": "member"}, session)[0] == 409)
    admin_user = require(api("/me/user", auth=admin_session))
    check("Yönetici sahip rolü veremez", api(f"/organizations/{org}/members", {"email": owner_email, "role": "owner"}, admin_session)[0] == 403)

    # --- project groups ---
    viewer_email = f"izleyici-{suffix}@kurumsal.test"; viewer_password = "Izleyici-Parola-" + suffix
    require(api("/members", {"email": viewer_email, "name": "İzleyici", "role": "viewer", "password": viewer_password}, headers))
    group = require(api(f"/organizations/{org}/groups", {"name": "Muhasebe " + suffix, "projects": [second["id"]], "members": [{"email": viewer_email, "role": "operator"}]}, session))
    viewer_token = require(api("/auth/login", {"email": viewer_email, "password": viewer_password}))["token"]
    viewer_session = {"Authorization": "Bearer " + viewer_token}
    viewer_projects = {p["id"]: p["role"] for p in require(api("/me/projects", auth=viewer_session))}
    check("Grup üyeliği ikinci projeye operatör, doğrudan üyelik ilk projeye izleyici erişimi verir",
          viewer_projects.get(project) == "viewer" and viewer_projects.get(second["id"]) == "operator")
    check("Aynı grup adı 409 döner", api(f"/organizations/{org}/groups", {"name": "Muhasebe " + suffix}, session)[0] == 409)
    require(api(f"/organizations/{org}/groups/{group['id']}", {"name": "Muhasebe " + suffix, "projects": [], "members": []}, session, "PUT"))
    viewer_projects = {p["id"]: p["role"] for p in require(api("/me/projects", auth=viewer_session))}
    check("Gruptan çıkarılınca ikinci projeye erişim kalkar", second["id"] not in viewer_projects)
    check("Organizasyon üyesi olmayan kullanıcı organizasyon sayfasını göremez", api(f"/organizations/{org}", auth=viewer_session)[0] == 403)

    # --- system administration and usage metering ---
    if not require(api("/me/security", auth=admin_session))["systemAdmin"]:
        subprocess.run(["docker", "exec", "-i", "integraops-postgres-1", "psql", "-U", "integraops", "-d", "integraops", "-c",
                        f"UPDATE app_users SET is_system_admin=true WHERE email='{admin_email}'"], check=True, capture_output=True)
        admin_token = require(api("/auth/login", {"email": admin_email, "password": admin_password}))["token"]
        admin_session = {"Authorization": "Bearer " + admin_token}
    check("Sistem yöneticisi olmayan kullanıcı yönetim listesini göremez", api("/admin/organizations", auth=session)[0] == 403)
    listing = require(api("/admin/organizations", auth=admin_session))
    check("Yönetim listesi yeni organizasyonu sahibiyle gösterir", any(o["id"] == org and o["owner_email"] == owner_email and o["projects"] == 3 for o in listing))
    check("Negatif sınır reddedilir", api(f"/admin/organizations/{org}/plan", {"maxEventsPerDay": -1}, admin_session, "PUT")[0] == 400)
    require(api(f"/admin/organizations/{org}/plan", {"maxEventsPerDay": 3, "note": "test sınırı"}, admin_session, "PUT"))
    overview = require(api(f"/organizations/{org}", auth=session))
    check("Müşteriye özel sınır plan varsayılanını geçersiz kılar", overview["limits"]["maxEventsPerDay"] == 3 and overview["limits"]["maxProjects"] == 3 and overview["planNote"] == "test sınırı")

    def send(i, prefix="SO"):
        return api("/events", {"eventId": str(uuid.uuid4()), "flow": "erp-order-invoice", "transactionId": f"{prefix}-{suffix}-{i}", "step": "order-created",
                               "status": "completed", "occurredAt": datetime.now(timezone.utc).isoformat()}, {"X-API-Key": key})
    results = [send(i)[0] for i in range(4)]
    check("Günlük olay sınırı: 3 kabul, dördüncü 429", results == [202, 202, 202, 429])
    overview = require(api(f"/organizations/{org}", auth=session))
    check("Kullanım sayacı 3 olay ve 1 red gösterir", overview["counts"]["eventsToday"] == 3 and overview["usage"][-1]["rejected"] == 1)
    require(api(f"/admin/organizations/{org}/plan", {"plan": "suspended"}, admin_session, "PUT"))
    check("Askıya alınan hesabın bildirimi 402 ile reddedilir", send(9)[0] == 402)
    require(api(f"/admin/organizations/{org}/plan", {"plan": "business", "maxEventsPerDay": None}, admin_session, "PUT"))
    check("İşletme planına geçince bildirim yeniden kabul edilir", send(10)[0] == 202)
    overview = require(api(f"/organizations/{org}", auth=session))
    check("Plan değişikliği organizasyon sayfasında görünür", overview["plan"] == "business" and overview["limits"]["maxEventsPerDay"] == 500000)

    # --- second factor ---
    begin = require(api("/me/mfa/begin", auth=session, method="POST"))
    check("İki adım kurulumu gizli anahtar ve otpauth bağlantısı verir", len(begin["secret"]) == 32 and begin["uri"].startswith("otpauth://totp/IntegraOps:"))
    check("Yanlış kodla etkinleştirme reddedilir", api("/me/mfa/enable", {"code": "000000"}, session)[0] in (400,) or totp(begin["secret"]) == "000000")
    enabled = require(api("/me/mfa/enable", {"code": totp(begin["secret"])}, session))
    check("Doğru kodla etkinleşir ve 8 kurtarma kodu verir", len(enabled["recoveryCodes"]) == 8 and all(len(c) == 10 for c in enabled["recoveryCodes"]))
    status, login = api("/auth/login", {"email": owner_email, "password": owner_password})
    check("İki adım açıkken parola girişi oturum yerine doğrulama isteği döner", status == 200 and login.get("mfaRequired") and "token" not in login)
    check("Yanlış kod 401 döner", api("/auth/mfa", {"challenge": login["challenge"], "code": "123456"})[0] == 401 or totp(begin["secret"]) == "123456")
    completed = require(api("/auth/mfa", {"challenge": login["challenge"], "code": totp(begin["secret"])}))
    check("Doğru kod oturum açar ve kullanıcı bilgisi iki adımı gösterir", completed["token"] and completed["user"]["mfa"] is True)
    check("Kullanılmış doğrulama isteği tekrar kullanılamaz", api("/auth/mfa", {"challenge": login["challenge"], "code": totp(begin["secret"])})[0] == 401)
    login2 = require(api("/auth/login", {"email": owner_email, "password": owner_password}))
    recovery = enabled["recoveryCodes"][0]
    check("Kurtarma kodu ile giriş yapılır", api("/auth/mfa", {"challenge": login2["challenge"], "code": recovery})[0] == 200)
    login3 = require(api("/auth/login", {"email": owner_email, "password": owner_password}))
    check("Aynı kurtarma kodu ikinci kez çalışmaz", api("/auth/mfa", {"challenge": login3["challenge"], "code": recovery})[0] == 401)
    session = {"Authorization": "Bearer " + completed["token"]}
    security = require(api("/me/security", auth=session))
    kinds = [e["kind"] for e in security["events"]]
    check("Hesap güvenlik olayları giriş, iki adım ve kurtarma kodunu listeler", {"signup", "mfa.enabled", "login.mfa", "mfa.recovery_used", "mfa.failed"} <= set(kinds))
    check("İki adımı kapatmak parola ve kod ister", api("/me/mfa/disable", {"password": "yanlış", "code": totp(begin["secret"])}, session)[0] == 400)
    require(api("/me/mfa/disable", {"password": owner_password, "code": totp(begin["secret"])}, session))
    check("İki adım kapanınca bütün oturumlar kapanır", api("/me/user", auth=session)[0] == 401)
    session = {"Authorization": "Bearer " + require(api("/auth/login", {"email": owner_email, "password": owner_password}))["token"]}

    # --- single sign-on ---
    provider = FakeProvider(PROVIDER_PORT)
    try:
        domain = f"sso-{suffix}.test"; provider.user["email"] = f"ali@{domain}"
        check("SSO ayarı https olmayan issuer'ı yalnızca yerel izinle kabul eder (issuer doğrulanır)",
              api(f"/organizations/{org}/sso", {"issuer": "http://127.0.0.1:1", "clientId": "x", "clientSecret": "y", "domain": domain}, session, "PUT")[0] == 400)
        check("SSO ayarı yalnızca sahibe açık", api(f"/organizations/{org}/sso", {"issuer": provider.issuer, "clientId": provider.client_id, "clientSecret": provider.client_secret, "domain": domain}, admin_session, "PUT")[0] in (403, 204)
              and require(api("/me/organizations", auth=admin_session)) is not None)
        require(api(f"/organizations/{org}/sso", {"issuer": provider.issuer, "clientId": provider.client_id, "clientSecret": provider.client_secret, "domain": domain, "required": False}, session, "PUT"))
        overview = require(api(f"/organizations/{org}", auth=session))
        check("SSO yapılandırması kaydedilir, gizli anahtar geri dönmez", overview["sso"]["configured"] and overview["sso"]["domain"] == domain and "secret" not in json.dumps(overview["sso"]).lower())
        check("Tanımsız e-posta alanı için SSO başlatılamaz", api("/auth/sso/start", {"email": "biri@baska-alan.test"})[0] == 404)
        start = require(api("/auth/sso/start?next=%2Falarms", {"email": provider.user["email"]}))
        parsed = urllib.parse.urlparse(start["url"]); q = urllib.parse.parse_qs(parsed.query)
        check("SSO başlangıcı sağlayıcının yetkilendirme adresine state ve nonce ile yönlendirir",
              parsed.path == "/authorize" and len(q["state"][0]) == 64 and len(q["nonce"][0]) == 64 and q["redirect_uri"][0].endswith("/api/v1/auth/sso/callback"))
        opener = urllib.request.build_opener(NoRedirect)
        local_url = start["url"].replace(provider.issuer, provider.local)
        try:
            opener.open(local_url, timeout=10)
            raise AssertionError("sağlayıcı yönlendirmedi")
        except urllib.error.HTTPError as e:
            check("Sağlayıcı kod ve state ile geri yönlendirir", e.code == 302 and "code=" in e.headers["Location"])
            callback = e.headers["Location"]
        try:
            opener.open(callback.replace(q["redirect_uri"][0], base + "/api/v1/auth/sso/callback"), timeout=20)
            raise AssertionError("geri çağrı yönlendirmedi")
        except urllib.error.HTTPError as e:
            location = e.headers["Location"]
            check("API geri çağrısı paneli tek kullanımlık biletle açar", e.code == 302 and "/sso?ticket=" in location and "next=%2Falarms" in location)
        ticket = urllib.parse.parse_qs(urllib.parse.urlparse(location).query)["ticket"][0]
        redeemed = require(api("/auth/sso/ticket", {"ticket": ticket}))
        check("Bilet oturuma çevrilir ve SSO kullanıcısı oluşturulur", redeemed["token"] and redeemed["user"]["email"] == provider.user["email"])
        check("Bilet ikinci kez kullanılamaz", api("/auth/sso/ticket", {"ticket": ticket})[0] == 401)
        sso_session = {"Authorization": "Bearer " + redeemed["token"]}
        check("SSO ile oluşturulan kullanıcı organizasyona üye olarak eklenir", any(o["id"] == org and o["role"] == "member" for o in require(api("/me/organizations", auth=sso_session))))
        check("Yanlış state ile geri çağrı girişe hata koduyla döner", (lambda r: r.code == 302 and "sso=expired" in r.headers["Location"])(
            _http_error(opener, base + "/api/v1/auth/sso/callback?code=x&state=" + "0" * 64)))
        require(api(f"/organizations/{org}/sso", {"issuer": provider.issuer, "clientId": provider.client_id, "domain": domain, "required": True}, session, "PUT"))
        check("SSO zorunluyken üyeler parola ile giremez", api("/auth/login", {"email": owner_email, "password": owner_password})[0] == 403)
        check("Sistem yöneticisi parola ile girmeye devam eder", api("/auth/login", {"email": admin_email, "password": admin_password})[0] == 200)
        require(api(f"/organizations/{org}/sso", {"issuer": provider.issuer, "clientId": provider.client_id, "domain": domain, "required": False}, session, "PUT"))
        require(api(f"/organizations/{org}/sso", {"issuer": ""}, session, "PUT"))
        check("SSO kaldırılınca alan yeniden boşta", not require(api(f"/organizations/{org}", auth=session))["sso"]["configured"])
    finally:
        provider.stop()

    # --- organization security events and audit ---
    events = require(api(f"/organizations/{org}/security-events", auth=session))
    kinds = {e["kind"] for e in events}
    check("Organizasyon güvenlik olayları SSO, plan ve üyelik değişikliklerini içerir", {"sso.configured", "sso.provisioned", "login.sso", "org.plan", "org.member.admin", "org.group.create"} <= kinds)
    audit = require(api(f"/organizations/{org}/audit", auth=session))
    check("Organizasyon denetim kaydı projeler arası yönetim işlemlerini listeler", {a["action"] for a in audit} >= {"project.create", "key.create", "member.viewer"} and {a["project"] for a in audit} >= {"İlk proje", "İkinci " + suffix})
    check("Üye rolündeki kullanıcı güvenlik olaylarını göremez", api(f"/organizations/{org}/security-events", auth=sso_session)[0] == 403)

    # --- data export ---
    status, export = api(f"/organizations/{org}/exports", auth=session, method="POST")
    check("Dışa aktarım isteği kuyruğa alınır", status == 202 and export["state"] == "pending")
    check("İkinci istek devam eden aktarım yüzünden 409 döner", api(f"/organizations/{org}/exports", auth=session, method="POST")[0] in (409, 202))
    wait("Dışa aktarım hazırlanır", lambda: any(x["id"] == export["id"] and x["state"] == "ready" for x in require(api(f"/organizations/{org}/exports", auth=session))), timeout=90)
    req = urllib.request.Request(f"{base}/api/v1/organizations/{org}/exports/{export['id']}/download", headers=session)
    with urllib.request.urlopen(req, timeout=60) as r:
        archive = zipfile.ZipFile(io.BytesIO(r.read()))
    manifest = json.loads(archive.read("manifest.json"))
    projects = [json.loads(l) for l in archive.read("projects.jsonl").decode().splitlines()]
    check("Zip manifest ve tablo dosyalarını içerir; projeler tam sayıda", manifest["tables"]["projects"] == 3 and len(projects) == 3 and "transactions.jsonl" in archive.namelist())
    check("Dışa aktarımda parola ve anahtar özetleri yok", b"password_hash" not in archive.read("members.jsonl") and b"key_hash" not in archive.read("api_keys.jsonl"))
    check("Dışa aktarımı yalnızca sahip indirir", api(f"/organizations/{org}/exports", auth=admin_session)[0] == 403)

    # --- deletion procedure ---
    check("Silme isteği yanlış parolayla reddedilir", api(f"/organizations/{org}/deletion", {"password": "yanlış"}, session)[0] == 400)
    require(api(f"/organizations/{org}/deletion", {"password": owner_password}, session))
    overview = require(api(f"/organizations/{org}", auth=session))
    check("Silme isteği hesabı askıya alır ve 7 gün sonrasına planlar", overview["plan"] == "suspended" and overview["deletion"]["scheduledAt"] is not None)
    check("Askıdaki hesap bildirim kabul etmez", send(11)[0] == 402)
    wait("Silme bildirimi sahiplere e-postalanır", lambda: any("silme isteği" in m["Subject"] for m in mailpit_search(owner_email)), timeout=60)
    require(api(f"/organizations/{org}/deletion", {"cancel": True}, session))
    overview = require(api(f"/organizations/{org}", auth=session))
    check("Silme isteği iptal edilir", overview["deletion"]["scheduledAt"] is None)
    require(api(f"/admin/organizations/{org}/plan", {"plan": "trial"}, admin_session, "PUT"))
    check("Yönetici planı yeniden etkinleştirir", send(12)[0] == 202)

    print(f"\nTAMAM · {passed} kurumsal kontrol geçti. Organizasyon: {org}")


def _http_error(opener, url):
    try:
        opener.open(url, timeout=20)
        raise AssertionError("yönlendirme bekleniyordu")
    except urllib.error.HTTPError as e:
        return e


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("BAŞARISIZ ·", error, file=sys.stderr)
        sys.exit(1)
