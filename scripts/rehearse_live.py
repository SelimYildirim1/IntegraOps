"""Rehearse the live operations cycle on this machine with the real compose.live.yml and scripts/live/*.sh.

Steps: build two release tags locally, first install behind Caddy HTTPS, create data, deploy the
second tag (with automatic pre-deploy backup), roll back, take an off-site backup to a local S3
server (rclone S3), destroy the whole stack including its volumes and local backups (server loss),
restore from the off-site copy and verify users, transactions and new event processing.

Everything runs in a separate Compose project with its own ports and volumes; the local
development stack is not touched. Real servers, DNS and certificates are outside this rehearsal.
"""
import argparse
import json
import os
import secrets
import shutil
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RCLONE_IMAGE = "rclone/rclone:1.71.0"
REGISTRY = "integraops-rehearsal"


class Rehearsal:
    def __init__(self, run_id, ports):
        self.run_id = run_id
        self.dir = ROOT / ".local" / "live-rehearsal" / run_id
        self.state = self.dir / "state"
        self.env_file = self.dir / ".env.live"
        self.project = f"integraops-liverehearsal-{run_id}"
        self.s3 = f"integraops-liverehearsal-s3-{run_id}"
        self.http_port, self.https_port, self.grafana, self.s3_port = ports
        self.passed = []
        self.started = time.monotonic()

    # ---------- helpers ----------
    def check(self, name, value):
        if not value:
            raise AssertionError(name)
        self.passed.append(name)
        print("GEÇTİ ·", name, flush=True)

    def sh(self, *cmd, env=None, capture=False, check=True):
        result = subprocess.run(cmd, cwd=ROOT, env={**os.environ, **(env or {})}, text=True,
                                stdout=subprocess.PIPE if capture else None, stderr=subprocess.STDOUT if capture else None)
        if check and result.returncode != 0:
            if capture:
                print(result.stdout[-3000:])
            raise SystemExit(f"Komut başarısız ({result.returncode}): {' '.join(cmd[:4])} …")
        return result.stdout if capture else result.returncode

    def live(self, script, *args, check=True):
        env = {"LIVE_ENV": str(self.env_file), "LIVE_SKIP_PULL": "1",
               "RCLONE_DOCKER_ARGS": "--add-host=host.docker.internal:host-gateway"}
        return self.sh("bash", f"scripts/live/{script}", *args, env=env, check=check)

    def compose(self, *args, capture=False, check=True):
        return self.sh("docker", "compose", "--project-directory", str(ROOT), "-f", "compose.live.yml",
                       "--env-file", str(self.env_file), "-p", self.project, *args, capture=capture, check=check)

    def https(self, host, path, data=None, headers=None, method=None):
        cmd = ["curl", "-sS", "-k", "--max-time", "20", "--resolve", f"{host}:{self.https_port}:127.0.0.1",
               "-o", "-", "-w", "\n%{http_code}", f"https://{host}:{self.https_port}{path}"]
        for k, v in (headers or {}).items():
            cmd += ["-H", f"{k}: {v}"]
        if data is not None:
            cmd += ["-H", "Content-Type: application/json", "--data", json.dumps(data)]
        if method:
            cmd += ["-X", method]
        out = subprocess.run(cmd, text=True, capture_output=True)
        body, _, code = out.stdout.rpartition("\n")
        try:
            parsed = json.loads(body) if body.strip().startswith(("{", "[")) else body
        except json.JSONDecodeError:
            parsed = body
        return int(code or 0), parsed

    def api(self, path, data=None, headers=None, method=None):
        return self.https("api.localhost", path, data, headers, method)

    def wait(self, name, predicate, timeout=120):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                self.check(name, True)
                return
            time.sleep(2)
        raise AssertionError(name + " zaman aşımı")

    # ---------- setup ----------
    def build(self, tag):
        revision = self.sh("git", "rev-parse", "HEAD", capture=True).strip()
        args = ["--build-arg", f"APP_VERSION={tag}", "--build-arg", f"APP_REVISION={revision}"]
        for target in ("api", "worker"):
            self.sh("docker", "build", "-q", "-f", "deploy/docker/Dockerfile", "--target", target,
                    "-t", f"{REGISTRY}/{target}:{tag}", *args, ".", capture=True)
        self.sh("docker", "build", "-q", "-t", f"{REGISTRY}/panel:{tag}", *args, "frontend", capture=True)

    def prepare(self):
        self.state.mkdir(parents=True, exist_ok=True)
        (self.state / "rclone").mkdir(exist_ok=True)
        os.chmod(self.dir, 0o700)
        s3_user, s3_pass = "rehearsal", secrets.token_hex(16)
        # A separate S3 server stands in for the off-site store (rclone serve s3, same image as the backups).
        self.sh("docker", "run", "-d", "--name", self.s3, "-p", f"127.0.0.1:{self.s3_port}:9000",
                RCLONE_IMAGE, "serve", "s3", "/data", "--addr", ":9000", "--auth-key", f"{s3_user},{s3_pass}",
                capture=True)
        (self.state / "rclone" / "rclone.conf").write_text(
            "[offsite]\ntype = s3\nprovider = Rclone\n"
            f"access_key_id = {s3_user}\nsecret_access_key = {s3_pass}\n"
            f"endpoint = http://host.docker.internal:{self.s3_port}\n")
        values = {
            "IMAGE_REGISTRY": REGISTRY, "IMAGE_TAG": "", "COMPOSE_PROJECT_NAME": self.project,
            "DEPLOYMENT_ENVIRONMENT": "rehearsal",
            "POSTGRES_PASSWORD": secrets.token_hex(24), "RABBITMQ_PASSWORD": secrets.token_hex(24),
            "BOOTSTRAP_KEY": secrets.token_hex(32), "GRAFANA_ADMIN_PASSWORD": secrets.token_urlsafe(18),
            "PANEL_HOST": "panel.localhost", "API_HOST": "api.localhost",
            "HTTP_PORT": str(self.http_port), "HTTPS_PORT": str(self.https_port), "GRAFANA_PORT": str(self.grafana),
            "SMTP_HOST": "host.docker.internal", "SMTP_PORT": "1025", "SMTP_STARTTLS": "false",
            "SMTP_FROM": "rehearsal@local.test", "SMTP_USER": "", "SMTP_PASSWORD": "", "WEBHOOK_ORIGINS": "",
            "OPS_ALERT_EMAIL": "ops@local.test", "OPS_ALERT_WEBHOOK_URL": "",
            "LIVE_STATE_DIR": str(self.state), "BACKUP_KEEP_DAYS": "14",
            "BACKUP_REMOTE": f"offsite:integraops-{self.run_id}/prod", "BACKUP_REMOTE_KEEP_DAYS": "90",
        }
        fd = os.open(self.env_file, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w") as f:
            f.write("".join(f"{k}={v}\n" for k, v in values.items()))
        self.env = values
        for _ in range(30):
            if subprocess.run(["curl", "-s", "-o", "/dev/null", f"http://127.0.0.1:{self.s3_port}/"]).returncode == 0:
                break
            time.sleep(1)
        self.sh("docker", "run", "--rm", "--add-host=host.docker.internal:host-gateway",
                "-v", f"{self.state / 'rclone'}:/config/rclone", RCLONE_IMAGE, "mkdir",
                f"offsite:integraops-{self.run_id}", capture=True)

    def version(self):
        code, body = self.api("/health/version")
        return body.get("version") if code == 200 and isinstance(body, dict) else None

    def releases(self):
        path = self.state / "releases.log"
        return [line.split("\t") for line in path.read_text().splitlines()] if path.exists() else []

    # ---------- scenario ----------
    def run(self, tag_a, tag_b):
        print(f"Prova kimliği: {self.run_id}\nİmajlar derleniyor: {tag_a}, {tag_b}", flush=True)
        self.build(tag_a)
        self.build(tag_b)
        self.check("İki sürümün api/worker/panel imajları derlendi", True)
        self.prepare()
        self.check("Sunucu dışı yedek deposu (S3) hazır", True)

        self.live("deploy.sh", tag_a)
        self.check(f"İlk kurulum {tag_a} ile tamamlandı", self.version() == tag_a)
        self.check("API Caddy üzerinden HTTPS ile yanıt veriyor", self.api("/health/ready")[0] == 200)
        self.check("Panel Caddy üzerinden HTTPS ile açılıyor", self.https("panel.localhost", "/login")[0] == 200)
        labels = json.loads(self.sh("docker", "image", "inspect", f"{REGISTRY}/api:{tag_a}", "--format",
                                    "{{json .Config.Labels}}", capture=True))
        self.check("İmaj sürüm ve kaynak commit etiketlerini taşıyor",
                   labels.get("org.opencontainers.image.version") == tag_a and len(labels.get("org.opencontainers.image.revision", "")) == 40)

        bootstrap = {"X-Bootstrap-Key": self.env["BOOTSTRAP_KEY"]}
        code, project = self.api("/api/v1/bootstrap/projects", {"organizationName": "Prova", "projectName": "Canlı prova"}, bootstrap)
        self.check("İlk proje kuruldu", code == 201)
        owner = {"email": f"owner-{self.run_id}@local.test", "name": "Prova Sahibi", "password": secrets.token_urlsafe(18)}
        code, _ = self.api("/api/v1/bootstrap/owner", owner, bootstrap | {"X-API-Key": project["apiKey"]})
        self.check("İlk sahip hesabı oluşturuldu", code in (200, 201))
        token = self.api("/api/v1/auth/login", {"email": owner["email"], "password": owner["password"]})[1]["token"]
        session = {"Authorization": "Bearer " + token, "X-Project-Id": project["projectId"]}
        code, _ = self.api("/api/v1/flows", {"code": "erp-order-invoice", "name": "ERP sipariş → fatura", "steps": [
            {"code": "order-created", "name": "Sipariş kaydı", "maxDelaySeconds": None},
            {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 3600}]}, session)
        self.check("Akış oluşturuldu", code == 201)
        ingest = {"X-API-Key": self.api("/api/v1/keys", {"name": "Prova kaynağı", "scope": "ingest"}, session)[1]["key"]}

        def send(tx, step):
            event = {"eventId": str(uuid.uuid4()), "flow": "erp-order-invoice", "transactionId": tx, "step": step,
                     "status": "completed", "occurredAt": datetime.now(timezone.utc).isoformat()}
            return self.api("/api/v1/events", event, ingest)[0]

        txs = [f"ORD-{self.run_id}-{i}" for i in range(5)]
        self.check("10 olay HTTPS üzerinden kabul edildi", all(send(t, s) == 202 for t in txs for s in ("order-created", "invoice-created")))
        summary = lambda: self.api("/api/v1/transactions/summary?flow=erp-order-invoice", headers=session)[1]
        self.wait("5 işlem tamamlandı", lambda: isinstance(summary(), dict) and summary().get("completed") == 5)
        open_tx = f"ORD-{self.run_id}-open"
        send(open_tx, "order-created")
        self.wait("Açık işlem işlendi", lambda: self.api(f"/api/v1/flows/erp-order-invoice/transactions/{open_tx}", headers=session)[0] == 200)

        self.live("deploy.sh", tag_b)
        self.check(f"{tag_b} yayını tamamlandı", self.version() == tag_b)
        history = self.releases()
        self.check("Yayın geçmişi sürüm ve şema kaydı tutuyor", history and history[-1][1:4] == ["deployed", tag_b, tag_a])
        self.check("Yayın öncesi yedek alındı", any((self.state / "backups").glob("20*Z/manifest.json")))
        self.check("Veri yayından sonra korunuyor", summary().get("completed") == 5)

        self.live("rollback.sh")
        self.check(f"Önceki sürüme ({tag_a}) dönüldü", self.version() == tag_a)
        self.check("Geri dönüşte veri korunuyor", summary().get("completed") == 5)

        self.live("backup.sh")
        manifests = sorted((self.state / "backups").glob("20*Z/manifest.json"))
        stamp = manifests[-1].parent.name
        manifest = json.loads(manifests[-1].read_text())
        self.check("Yedek bildirimi sürüm, şema ve özetleri içeriyor",
                   manifest["image_tag"] == tag_a and manifest["schema_version"] >= 3 and len(manifest["sha256"]) == 2)
        metric = (self.state / "metrics" / "integraops_backup.prom").read_text()
        self.check("Son başarılı yedek ölçümü yazıldı", "integraops_backup_last_success_timestamp_seconds" in metric)

        # Server loss: containers, volumes (database, keys, broker) and local backups are gone.
        self.compose("down", "-v", "--remove-orphans", capture=True)
        shutil.rmtree(self.state / "backups")
        (self.state / "releases.log").unlink()
        volumes = self.sh("docker", "volume", "ls", "-q", "--filter", f"label=com.docker.compose.project={self.project}", capture=True)
        self.check("Sunucu kaybı: servisler, diskler ve yerel yedekler silindi", not volumes.strip())

        started = time.monotonic()
        self.live("restore.sh", f"remote:{stamp}")
        restore_seconds = time.monotonic() - started
        self.check(f"Sunucu dışı yedekten ({stamp}) geri yüklendi ve {tag_a} hazır", self.version() == tag_a)
        token = self.api("/api/v1/auth/login", {"email": owner["email"], "password": owner["password"]})[1]
        self.check("Mevcut sahip hesabıyla giriş yapılabiliyor", isinstance(token, dict) and "token" in token)
        session["Authorization"] = "Bearer " + token["token"]
        self.check("Tamamlanmış işlemler geri geldi", summary().get("completed") == 5)
        send(open_tx, "invoice-created")
        self.wait("Geri yüklenen sistemde yeni olay işlendi (yeni RabbitMQ)",
                  lambda: self.api(f"/api/v1/flows/erp-order-invoice/transactions/{open_tx}", headers=session)[1].get("status") == "completed")
        self.check("Eski gönderim anahtarı geri yüklemeden sonra çalışıyor", send(f"ORD-{self.run_id}-after", "order-created") == 202)

        report = {"run_id": self.run_id, "status": "passed", "tags": [tag_a, tag_b], "checks": self.passed,
                  "restored_backup": stamp, "restore_seconds": round(restore_seconds, 1),
                  "total_seconds": round(time.monotonic() - self.started, 1)}
        (self.dir / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=1))
        print(f"\n{len(self.passed)} kontrol geçti. Geri yükleme süresi {restore_seconds:.0f} sn (bu bilgisayardaki ölçüm).")
        print(f"Rapor: {self.dir / 'report.json'}")

    def cleanup(self):
        if self.env_file.exists():
            self.compose("down", "-v", "--remove-orphans", capture=True, check=False)
        subprocess.run(["docker", "rm", "-f", "-v", self.s3], capture_output=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--keep", action="store_true", help="Leave the rehearsal stack running for inspection")
    parser.add_argument("--ports", default="18480,18443,13001,19000", help="HTTP,HTTPS,Grafana,S3 host ports")
    args = parser.parse_args()
    ports = [int(p) for p in args.ports.split(",")]
    run = Rehearsal(uuid.uuid4().hex[:8], ports)
    ok = False
    try:
        run.run("v0.9.0-rehearsal.1", "v0.9.0-rehearsal.2")
        ok = True
    finally:
        if not args.keep:
            run.cleanup()
            print("Prova ortamı kaldırıldı." if ok else "Prova ortamı kaldırıldı; hata çıktısı yukarıda.")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
