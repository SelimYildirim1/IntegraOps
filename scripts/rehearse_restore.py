"""Local V1 recovery rehearsal. Source containers are only read, never stopped.

Restores a fresh backup to a unique Compose project, with no external network.
Uses stdlib only; Docker Compose and the existing local V1 setup are required.
"""
import argparse
from datetime import datetime, timezone
import getpass
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import socket
import subprocess
import sys
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from prepare_v1 import ROOT, read_env

RUNS = ROOT / ".local" / "restore-checks"
COMPOSE = ROOT / "compose.restore.yml"
PUBLISHED_SERVICES = {"api", "panel", "mailpit", "webhook"}


class RecoveryError(RuntimeError):
    pass


def ensure(value, message):
    if not value:
        raise RecoveryError(message)


def run_identity(run_id):
    ensure(re.fullmatch(r"[a-f0-9]{12}", run_id or ""), "Prova kimliği geçersiz.")
    return "integraops-restore-" + run_id


def clean_env():
    # Explicit Compose files and env files; ignore ambient Compose/restore overrides.
    return {k: v for k, v in os.environ.items()
            if not k.startswith(("COMPOSE_", "RESTORE_"))}


def command(args, *, data=None, stdin=None, stdout=None, timeout=600):
    try:
        result = subprocess.run(args, cwd=ROOT, env=clean_env(), input=data,
                                stdin=stdin, stdout=stdout or subprocess.PIPE,
                                stderr=subprocess.PIPE, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise RecoveryError("Komut zaman aşımına uğradı; prova ortamını inceleyin.") from None
    except OSError:
        raise RecoveryError("Docker çalıştırılamadı; Docker Desktop ve Compose gerekli.") from None
    # Never echo commands, SQL, raw tool errors or credentials.
    ensure(result.returncode == 0, f"Docker işlemi başarısız (çıkış {result.returncode}). Prova günlüklerini yerelde inceleyin.")
    return result.stdout or b""


def private_json(path, value):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def write_env(path, values):
    # All generated values are single-line; do not allow dotenv expansion/quoting.
    ensure(all(not any(c in str(v) for c in "'\n\r$") for v in values.values()),
           "Prova ayarı güvenli env biçimine dönüştürülemedi.")
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        stream.write("".join(f"{k}='{v}'\n" for k, v in values.items()))


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def key_inventory(archive):
    inventory = {}
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as bundle:
        total = 0
        for member in bundle:
            path = PurePosixPath(member.name)
            ensure(not path.is_absolute() and ".." not in path.parts,
                   "Anahtar arşivinde geçersiz yol var.")
            if member.isdir():
                continue
            ensure(member.isfile() and path.suffix == ".xml" and member.size <= 1024 * 1024,
                   "Anahtar arşivinde beklenmeyen dosya türü/boyutu var.")
            total += member.size
            ensure(total <= 16 * 1024 * 1024, "Anahtar arşivi sınırı aşıldı.")
            name = str(path)
            ensure(name not in inventory, "Anahtar arşivinde yinelenen dosya var.")
            inventory[name] = hashlib.sha256(bundle.extractfile(member).read()).hexdigest()
    ensure(inventory, "Anahtar arşivi boş; şifrelenmiş kanallar doğrulanamaz.")
    return inventory


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def http(base, path, body=None, headers=None, method=None, expected=(200,), raw=False):
    # This client must never send restored credentials to a remote host or proxy.
    parsed = urllib.parse.urlsplit(base)
    ensure(parsed.scheme == "http" and parsed.hostname == "127.0.0.1"
           and not parsed.username and not parsed.password and parsed.path in ("", "/")
           and not parsed.query and not parsed.fragment, "Yalnızca yerel prova HTTP adresi kullanılabilir.")
    ensure(path.startswith("/") and not path.startswith("//"), "Geçersiz HTTP yolu.")
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data,
        headers={"Content-Type": "application/json", **(headers or {})},
        method=method or ("POST" if body is not None else "GET"))
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        with opener.open(req, timeout=15) as response:
            status, payload = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, payload = error.code, error.read()
    ensure(status in expected, f"Prova HTTP isteği başarısız: {status}; beklenen {expected}.")
    if raw:
        return payload.decode("utf-8")
    return json.loads(payload) if payload else None


def wait_for(label, predicate, timeout=180):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        try:
            if predicate():
                return
        except (OSError, RecoveryError):
            pass
        time.sleep(1)
    raise RecoveryError(label + ": zaman aşımı.")


def event(flow, transaction, step, status="completed"):
    return {"eventId": str(uuid.uuid4()), "flow": flow, "transactionId": transaction,
            "step": step, "status": status, "occurredAt": datetime.now(timezone.utc).isoformat(),
            "error": "Yerel kurtarma provası" if status == "failed" else None}


def validate_isolation(config, project):
    ensure(config.get("name") == project, "Compose proje kimliği uyuşmuyor.")
    networks = config.get("networks", {})
    ensure(set(networks) == {"default", "host_access"}, "Prova ağ listesi beklenenden farklı.")
    network = networks["default"]
    ensure(network.get("internal") is True and not network.get("external")
           and network.get("name") == project + "_default", "Prova ağı izole değil.")
    # Docker publishes no ports on internal networks; loopback-only services need this bridge.
    bridge = networks["host_access"]
    ensure(not bridge.get("external") and bridge.get("name") == project + "_host_access",
           "Prova port ağı başka projeye bağlı.")
    volumes = config.get("volumes", {})
    ensure(set(volumes) == {"postgres_data", "rabbitmq_data", "protection_keys", "mailpit_data", "webhook_data"},
           "Prova disk listesi beklenenden farklı.")
    for name, volume in volumes.items():
        ensure(not volume.get("external") and volume.get("name") == project + "_" + name,
               "Prova diski başka projeye bağlı.")
    services = config.get("services", {})
    ensure(set(services) == {"postgres", "rabbitmq", "keys", "api", "worker", "panel", "mailpit", "webhook"},
           "Prova servis listesi beklenenden farklı.")
    for name, service in services.items():
        allowed = {"default", "host_access"} if name in PUBLISHED_SERVICES else {"default"}
        ensure(set(service.get("networks", {})) == allowed and not service.get("network_mode")
               and not service.get("container_name") and not service.get("privileged"),
               "Prova servisinin ağ/kimlik ayarı beklenenden farklı.")
        ensure(name in PUBLISHED_SERVICES or not service.get("ports"),
               "Yalnızca panel/API/Mailpit/webhook port açabilir.")
        for port in service.get("ports", []):
            ensure(port.get("host_ip") == "127.0.0.1", "Prova portu yalnızca yerel adrese bağlanmalı.")
        for mount in service.get("volumes", []):
            if mount.get("type") == "volume":
                ensure(mount.get("source") in volumes, "Bilinmeyen prova diski.")
            else:
                ensure(mount.get("type") == "bind" and mount.get("read_only") is True and
                       ((name == "keys" and mount.get("target") == "/backup") or
                        (name == "webhook" and mount.get("target") == "/app/receiver.py")),
                       "Beklenmeyen yazılabilir dosya bağlantısı.")


class Rehearsal:
    def __init__(self, run_id):
        self.project = run_identity(run_id)
        self.directory = RUNS / run_id
        self.env_file = self.directory / "compose.env"
        self.safe = False
        self.report = {"run_id": run_id, "compose_project": self.project,
                       "status": "running", "checks": [], "scope": "local_integraops_recovery"}

    def compose(self, *args, **kwargs):
        return command(["docker", "compose", "--project-name", self.project,
                        "--env-file", str(self.env_file), "-f", str(COMPOSE), *args], **kwargs)

    def sql(self, statement):
        return self.compose("exec", "-T", "postgres", "psql", "-X", "-q", "-t", "-A",
                            "-U", "integraops", "-d", "integraops", "-v", "ON_ERROR_STOP=1",
                            data=statement.encode()).decode().strip()

    def scalar(self, statement):
        return json.loads(self.sql(statement))

    def check(self, label, value=True):
        ensure(value, label)
        self.report["checks"].append(label)
        print("GEÇTİ ·", label, flush=True)

    def save(self):
        private_json(self.directory / "report.json", self.report)

    def validate(self):
        config = json.loads(self.compose("--profile", "maintenance", "config", "--format", "json"))
        validate_isolation(config, self.project)
        self.safe = True

    def requeue(self):
        # Require the worker to be absent/stopped before recovery writes.
        running = self.compose("ps", "--status", "running", "--services").decode().splitlines()
        ensure("worker" not in running, "Yeniden kuyruklama için prova worker'ı durmuş olmalı.")
        self.sql("SET integraops.restore_rehearsal = 'yes';\n" +
                 (ROOT / "scripts" / "restore_requeue.sql").read_text())


def source_containers(project):
    ensure(re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,62}", project), "Kaynak Compose proje adı geçersiz.")
    ensure(not project.startswith("integraops-restore-"), "Prova ortamından yedek alınamaz.")
    containers, images = {}, {}
    for service in ("postgres", "api", "worker", "rabbitmq", "mailpit"):
        ids = command(["docker", "ps", "--filter", f"label=com.docker.compose.project={project}",
                       "--filter", f"label=com.docker.compose.service={service}", "--format", "{{.ID}}"])
        matches = ids.decode().splitlines()
        ensure(len(matches) == 1, f"Kaynak {service} için tek çalışan konteyner bulunmalı.")
        containers[service] = matches[0]
        images[service] = command(["docker", "inspect", "--format", "{{.Image}}", matches[0]]).decode().strip()
        ensure(re.fullmatch(r"sha256:[a-f0-9]{64}", images[service]), "Kaynak imaj kimliği okunamadı.")
    return containers, images


def local_docker():
    ensure(not os.environ.get("DOCKER_HOST"), "Bu prova için DOCKER_HOST ayarını kaldırıp yerel Docker bağlamını seçin.")
    endpoint = command(["docker", "context", "inspect", "--format", "{{.Endpoints.docker.Host}}"])
    ensure(endpoint.decode().strip().startswith(("unix://", "npipe://")),
           "Prova yerel Docker Desktop/Engine bağlamında çalıştırılmalı.")


def reserve_check(ports):
    ensure(len(set(ports)) == len(ports), "Prova portları birbirinden farklı olmalı.")
    for port in ports:
        ensure(1024 <= port <= 65535, "Port 1024–65535 arasında olmalı.")
        try:
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", port))
        except OSError:
            raise RecoveryError(f"Prova portu kullanımda: {port}.") from None


def prepare(run, args, source):
    values = read_env(ROOT / ".env")
    project = str(uuid.UUID(values.get("PILOT_PROJECT_ID", "")))
    secret = values.get("LOCAL_WEBHOOK_SECRET", "")
    ingest_key = values.get("SOURCE_API_KEY", "")
    ensure(re.fullmatch(r"[A-Za-z0-9_-]{32,128}", secret), "Yerel webhook sırrı bulunamadı veya biçimi desteklenmiyor.")
    ensure(ingest_key, "Yerel kaynak API anahtarı bulunamadı; önce mevcut V1 kaynak kurulumunu tamamlayın.")
    containers, images = source_containers(source)
    published = json.loads(command(["docker", "inspect", "--format", "{{json .NetworkSettings.Ports}}", containers["api"]]))
    bindings = published.get("8080/tcp") or []
    ensure(bindings, "Kaynak API'nin yerel HTTP portu bulunamadı.")
    source_port = int(bindings[0]["HostPort"])
    reserve_check([args.api_port, args.panel_port, args.mail_port, args.webhook_port])
    run.directory.mkdir(parents=True, mode=0o700, exist_ok=False)
    backup = run.directory / "backup"
    backup.mkdir(mode=0o700)
    run.report.update(source_project=source, pilot_project_id=project, source_images=images,
                      source_api_url=f"http://127.0.0.1:{source_port}")
    write_env(run.env_file, {
        "RESTORE_POSTGRES_IMAGE": images["postgres"], "RESTORE_API_IMAGE": images["api"],
        "RESTORE_WORKER_IMAGE": images["worker"], "RESTORE_RABBIT_IMAGE": images["rabbitmq"],
        "RESTORE_MAILPIT_IMAGE": images["mailpit"], "RESTORE_BACKUP_DIR": str(backup),
        "RESTORE_DB_PASSWORD": secrets.token_hex(24), "RESTORE_RABBIT_PASSWORD": secrets.token_hex(24),
        "RESTORE_BOOTSTRAP_KEY": secrets.token_hex(32), "RESTORE_WEBHOOK_SECRET": secret,
        "RESTORE_API_PORT": args.api_port, "RESTORE_PANEL_PORT": args.panel_port,
        "RESTORE_MAIL_PORT": args.mail_port, "RESTORE_WEBHOOK_PORT": args.webhook_port,
    })
    run.validate()
    run.save()
    # Never combine source and restored Compose commands. Source access is read-only docker exec.
    print("Yerel kurulumdan veritabanı ve anahtar yedeği alınıyor…", flush=True)
    with (backup / "integraops.dump").open("wb") as stream:
        command(["docker", "exec", containers["postgres"], "pg_dump", "-U", "integraops",
                 "-d", "integraops", "-Fc"], stdout=stream)
    with (backup / "protection-keys.tar.gz").open("wb") as stream:
        command(["docker", "exec", containers["api"], "tar", "-C", "/var/lib/integraops/keys",
                 "-czf", "-", "."], stdout=stream)
    for path in backup.iterdir():
        path.chmod(0o600)
        ensure(path.stat().st_size > 0, "Yedek dosyası boş.")
    keys = key_inventory((backup / "protection-keys.tar.gz").read_bytes())
    run.report["backup"] = {"created_at": datetime.now(timezone.utc).isoformat(),
                            "sha256": {p.name: sha256(p) for p in backup.iterdir()}, "key_files": keys}
    run.save()
    print("Panel imajı derleniyor; bu adım birkaç dakika sürebilir…", flush=True)
    run.compose("build", "panel", timeout=1200)
    run.compose("pull", "webhook", timeout=300)
    return project, ingest_key, keys


def restore(run, key_hashes):
    started = time.monotonic()
    for name, expected_hash in run.report["backup"]["sha256"].items():
        ensure(sha256(run.directory / "backup" / name) == expected_hash, "Yedek dosyası hazırlama sonrasında değişmiş.")
    # UUID-based project names plus no external/named volumes prevent reuse of source disks.
    existing = command(["docker", "volume", "ls", "--filter",
                        f"label=com.docker.compose.project={run.project}", "--format", "{{.Name}}"])
    ensure(not existing.strip(), "Bu prova projesinin diski zaten var; yeni prova başlatın.")
    run.compose("up", "-d", "--wait", "--wait-timeout", "180", "postgres", "rabbitmq", "mailpit", "webhook")
    ensure(run.sql("SELECT count(*) FROM information_schema.tables WHERE table_schema='public';") == "0",
           "Hedef veritabanı boş değil; geri yükleme durduruldu.")
    with (run.directory / "backup" / "integraops.dump").open("rb") as stream:
        run.compose("exec", "-T", "postgres", "pg_restore", "-U", "integraops", "-d", "integraops",
                    "--exit-on-error", "--single-transaction", "--no-owner", "--no-privileges", stdin=stream)
    run.check("Yedek boş ve ayrı veritabanına hatasız geri yüklendi")
    run.compose("run", "--rm", "--no-deps", "keys")
    restored_keys = run.compose("run", "--rm", "--no-deps", "--entrypoint", "tar", "keys",
                                "-C", "/var/lib/integraops/keys", "-czf", "-", ".")
    run.check("Şifreleme anahtar dosyaları yedekle birebir aynı", key_inventory(restored_keys) == key_hashes)
    tables = run.sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;").splitlines()
    ensure(all(re.fullmatch(r"[a-z_]+", name) for name in tables), "Beklenmeyen tablo adı.")
    run.report["restored_table_counts"] = {name: int(run.sql(f'SELECT count(*) FROM "{name}";')) for name in tables}
    migrations = run.scalar("SELECT json_agg(version ORDER BY version) FROM schema_migrations;")
    run.check("V1 migration kayıtları mevcut", migrations == [1, 2, 3])
    run.check("Kullanıcı, proje ve olay kayıtları mevcut", all(
        run.report["restored_table_counts"].get(t, 0) > 0 for t in ("app_users", "projects", "transaction_events")))
    # Save inventory before changing the CLONE. Never replay old outbound notifications.
    run.sql("""BEGIN;
        DELETE FROM user_sessions;
        DELETE FROM worker_heartbeats;
        UPDATE notification_channels SET enabled=false;
        UPDATE notification_deliveries SET state='cancelled',lease_token=NULL,lease_until=NULL,
            last_error='restore_rehearsal_quarantine' WHERE state IN ('pending','sending');
        COMMIT;""")
    run.check("Kopyadaki eski oturumlar ve bekleyen bildirimler prova için kapatıldı")
    run.report["restore_seconds_before_application_start"] = round(time.monotonic() - started, 2)
    run.compose("up", "-d", "--no-deps", "api", "panel")


def verify(run, args, project, ingest_key):
    base = f"http://127.0.0.1:{args.api_port}"
    panel = f"http://127.0.0.1:{args.panel_port}"
    webhook = f"http://127.0.0.1:{args.webhook_port}"
    mailpit = f"http://127.0.0.1:{args.mail_port}"
    session = {}
    run.report["urls"] = {"api": base, "panel": panel, "webhook": webhook + "/receipts", "mailpit": mailpit}
    try:
        wait_for("API hazır", lambda: http(base, "/health/ready"))
        run.check("Geri yüklenen API hazır")
        # Resolve credentials only after isolated ports and restored services are ready.
        email = (os.environ.get("V1_OWNER_EMAIL") or input("Mevcut sahip e-posta: ")).strip()
        password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Mevcut sahip parolası: ")
        result = http(base, "/api/v1/auth/login", {"email": email, "password": password})
        password = None
        token = result.get("token", "")
        ensure(re.fullmatch(r"[a-fA-F0-9]{64}", token), "Giriş yanıtı geçersiz.")
        session = {"Authorization": "Bearer " + token}
        headers = session | {"X-Project-Id": project}
        projects = http(base, "/api/v1/me/projects", headers=session)
        run.check("Yedekteki kullanıcıyla giriş ve proje sahipliği doğrulandı",
                  any(p["id"] == project and p["role"] == "owner" for p in projects))
        channels = http(base, "/api/v1/channels", headers=headers)
        email_channel = next((c for c in channels if c["kind"] == "email" and c["target"].endswith("@local.test")), None)
        hook_channel = next((c for c in channels if c["kind"] == "webhook" and c["target"] == "http://webhook:8080"), None)
        run.check("Yedekteki yerel kanal hedefleri eski anahtarlarla çözüldü", email_channel and hook_channel)
        # API only exposes webhook origin, not path/secret. Actual signed delivery checks both later.
        pid = str(uuid.UUID(project))
        key_hash = hashlib.sha256(ingest_key.encode()).hexdigest()
        run.check("Yedeklenmiş kaynak anahtarı doğru projeye ait ve etkin", run.sql(
            f"SELECT count(*) FROM api_keys WHERE project_id='{pid}' AND key_hash='{key_hash}' "
            "AND scope='ingest' AND revoked_at IS NULL;") == "1")
        sample = run.scalar(f"""SELECT COALESCE((SELECT row_to_json(s) FROM (
            SELECT t.business_id,f.code flow,
              (SELECT count(*) FROM transaction_events e WHERE e.transaction_id=t.id) accepted,
              (SELECT count(*) FROM transaction_events e WHERE e.transaction_id=t.id AND e.processed_at IS NOT NULL) processed
            FROM transactions t JOIN flows f ON f.id=t.flow_id
            WHERE t.project_id='{pid}' AND t.status='Completed'
              AND NOT EXISTS(SELECT 1 FROM transaction_events e WHERE e.transaction_id=t.id AND e.processed_at IS NULL)
            ORDER BY t.created_at LIMIT 1) s), 'null'::json);""")
        ensure(sample, "Pilot projede yedeklenmiş tamamlanmış işlem bulunmalı.")
        def detail(flow, tx):
            return http(base, "/api/v1/flows/" + urllib.parse.quote(flow, safe="") + "/transactions/" +
                        urllib.parse.quote(tx, safe=""), headers=headers)
        old = detail(sample["flow"], sample["business_id"])
        run.check("Yedekteki tamamlanmış işlem API'den aynı olay sayılarıyla okunuyor",
                  old["status"] == "completed" and old["acceptedEventCount"] == sample["accepted"]
                  and old["processedEventCount"] == sample["processed"])
        run.report["preserved_sample"] = sample
        run.requeue()
        run.compose("up", "-d", "--no-deps", "worker")
        wait_for("Yedekte bekleyen olaylar işlendi", lambda: run.sql(
            "SELECT count(*) FROM transaction_events WHERE processed_at IS NULL;") == "0", timeout=300)
        wait_for("Yeni worker yaşam sinyali", lambda: run.sql(
            "SELECT count(*) FROM worker_heartbeats WHERE seen_at>clock_timestamp()-interval '20 seconds';") != "0")
        run.check("Yedek kuyruğu boşaldı ve yeni worker yaşam sinyali yazdı")
        for channel in (email_channel, hook_channel):
            http(base, "/api/v1/channels/" + channel["id"], {"enabled": True}, headers,
                 method="PUT", expected=(204,))
        flow = "restore-" + run.report["run_id"]
        http(base, "/api/v1/flows", {"code": flow, "name": "Kurtarma provası", "steps": [
            {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
            {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 86400}]}, headers, expected=(201,))
        ingest = {"X-API-Key": ingest_key}
        # Reproduce both pending and published-but-unprocessed pointers only in this clone.
        run.compose("stop", "worker")
        tx = "RESTORE-QUEUE-" + run.report["run_id"]
        pair = [event(flow, tx, "start"), event(flow, tx, "finish")]
        for body in pair:
            http(base, "/api/v1/events", body, ingest, expected=(202,))
        event_id = str(uuid.UUID(pair[0]["eventId"]))
        run.sql(f"UPDATE outbox_messages SET published_at=clock_timestamp() WHERE project_id='{pid}' AND event_id='{event_id}';")
        run.check("Kaybolan broker işaretçisi ve yayımlanmamış olay provada oluşturuldu",
                  run.sql(f"SELECT count(*) FROM transaction_events WHERE project_id='{pid}' AND processed_at IS NULL;") == "2")
        run.requeue()
        run.requeue()  # Must be safe to run twice before starting the worker.
        run.check("Yeniden kuyruklama iki kez çalışınca kayıt çoğalmadı", run.sql(
            f"SELECT count(*) FROM outbox_messages o JOIN transaction_events e USING(project_id,event_id) "
            f"WHERE e.project_id='{pid}' AND e.processed_at IS NULL AND o.published_at IS NULL;") == "2")
        run.compose("start", "worker")
        wait_for("Kurtarılan iki olay tamamlandı", lambda: detail(flow, tx)["status"] == "completed")
        duplicate = http(base, "/api/v1/events", pair[0], ingest)
        run.check("Yedek kaynak anahtarı çalışıyor; aynı olay tekrar işlenmiyor",
                  duplicate.get("duplicate") is True and detail(flow, tx)["acceptedEventCount"] == 2
                  and detail(flow, tx)["processedEventCount"] == 2)
        alarm_tx = "RESTORE-ALARM-" + run.report["run_id"]
        http(base, "/api/v1/events", event(flow, alarm_tx, "start"), ingest, expected=(202,))
        http(base, "/api/v1/events", event(flow, alarm_tx, "finish", "failed"), ingest, expected=(202,))
        def alarms():
            return http(base, "/api/v1/alarms?state=all&flow=" + flow, headers=headers)["items"]
        wait_for("Hata alarmı açıldı", lambda: any(a["business_id"] == alarm_tx and a["kind"] == "failed" for a in alarms()))
        aid = next(a["id"] for a in alarms() if a["business_id"] == alarm_tx and a["kind"] == "failed")
        def alarm():
            return http(base, "/api/v1/alarms/" + aid, headers=headers)
        wait_for("Açılış iki eski kanala gönderildi", lambda: len(alarm()["deliveries"]) == 2 and
                 all(d["state"] == "sent" for d in alarm()["deliveries"]))
        http(base, "/api/v1/events", event(flow, alarm_tx, "finish"), ingest, expected=(202,))
        wait_for("Alarm kapandı ve dört gönderim tamamlandı", lambda: alarm()["alarm"]["resolution"] == "step_completed"
                 and len(alarm()["deliveries"]) == 4 and all(d["state"] == "sent" for d in alarm()["deliveries"]))
        deliveries = alarm()["deliveries"]
        receipts = http(webhook, "/receipts")
        received_ids = {r["id"] for r in receipts}
        run.check("Yedekteki webhook sırrıyla açılış/kapanış imzaları alıcıda doğrulandı",
                  all(d["id"] in received_ids for d in deliveries if d["kind"] == "webhook"))
        messages = http(mailpit, "/api/v1/messages").get("messages", [])
        run.check("Açılış/kapanış e-postaları yerel Mailpit'e ulaştı",
                  sum(alarm_tx in m.get("Subject", "") for m in messages) == 2)
        delay_flow = "restore-delay-" + run.report["run_id"]
        delay_tx = "RESTORE-DELAY-" + run.report["run_id"]
        http(base, "/api/v1/flows", {"code": delay_flow, "name": "Kurtarma süre kontrolü", "steps": [
            {"code": "start", "name": "Başlangıç", "maxDelaySeconds": None},
            {"code": "finish", "name": "Bitiş", "maxDelaySeconds": 2}]}, headers, expected=(201,))
        http(base, "/api/v1/events", event(delay_flow, delay_tx, "start"), ingest, expected=(202,))
        wait_for("Geri yüklenen gecikme tarayıcısı", lambda: detail(delay_flow, delay_tx)["delay"]["state"] == "overdue")
        run.check("Gecikme tarayıcısı süre aşımını belirledi; işlem failed olmadı",
                  detail(delay_flow, delay_tx)["status"] == "inProgress")
        delay_alarms = http(base, "/api/v1/alarms?state=open&flow=" + delay_flow, headers=headers)["items"]
        ensure(len(delay_alarms) == 1 and delay_alarms[0]["kind"] == "overdue", "Gecikme alarmı oluşmadı.")
        delay_aid = delay_alarms[0]["id"]
        http(base, "/api/v1/events", event(delay_flow, delay_tx, "finish"), ingest, expected=(202,))
        wait_for("Gecikme alarmı çözüldü", lambda: http(base, "/api/v1/alarms/" + delay_aid,
                 headers=headers)["alarm"]["resolution"] == "step_completed")
        run.check("Geç tamamlanma geri yüklenen ortamda gecikme alarmını kapattı")
        old_after = detail(sample["flow"], sample["business_id"])
        stable_fields = ("id", "flow", "transactionId", "status", "acceptedEventCount", "processedEventCount", "steps", "events")
        run.check("Eski tamamlanmış işlem prova sonunda korundu", all(old_after[k] == old[k] for k in stable_fields))
        wait_for("Panel giriş sayfası", lambda: "IntegraOps" in http(panel, "/login", raw=True))
        html = http(panel, "/?flow=" + flow,
                    headers={"Cookie": f"io_session={token}; io_project={pid}"}, raw=True)
        run.check("Panel sunucusu geri yüklenen API'den prova işlemini gösterdi", tx in html and alarm_tx in html)
        run.report.update(probe_flow=flow, probe_transactions=[tx, alarm_tx], probe_alarm_id=aid,
                          delay_probe={"flow": delay_flow, "transaction": delay_tx, "alarm_id": delay_aid},
                          status="automated_passed_manual_pending")
        print("Panelde elle giriş ve sayfa kontrolü:", panel, flush=True)
    finally:
        if session:
            http(base, "/api/v1/me/logout", headers=session, method="POST", expected=(204,))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    start = sub.add_parser("run", help="Yeni yedek al ve ayrı yerel ortamda doğrula")
    start.add_argument("--source-project", default="integraops", help="Yerel kaynak Compose proje adı")
    start.add_argument("--api-port", type=int, default=18080)
    start.add_argument("--panel-port", type=int, default=13000)
    start.add_argument("--mail-port", type=int, default=18025)
    start.add_argument("--webhook-port", type=int, default=18082)
    for name in ("status", "logs", "cleanup"):
        item = sub.add_parser(name)
        item.add_argument("--run-id", required=True)
    args = parser.parse_args()
    os.umask(0o077)
    local_docker()
    if args.action != "run":
        run = Rehearsal(args.run_id)
        saved = json.loads((run.directory / "report.json").read_text())
        ensure(saved.get("compose_project") == run.project, "Prova kimliği raporla uyuşmuyor.")
        run.validate()
        if args.action == "cleanup":
            run.compose("down", "--volumes", "--remove-orphans")
            saved["environment_removed"] = True
            private_json(run.directory / "report.json", saved)
            print("Yalnızca prova konteynerleri ve diskleri kaldırıldı. Yedek ve rapor korundu.")
        elif args.action == "logs":
            print(run.compose("logs", "--tail", "80", "api", "worker", "panel").decode())
        else:
            print(run.compose("ps").decode())
        return
    run = Rehearsal(uuid.uuid4().hex[:12])
    started = time.monotonic()
    print("Prova kimliği:", run.report["run_id"], flush=True)
    try:
        project, ingest_key, keys = prepare(run, args, args.source_project)
        restore(run, keys)
        verify(run, args, project, ingest_key)
        source_containers(args.source_project)
        http(run.report["source_api_url"], "/health/ready")
        run.check("Kaynak kurulumun servisleri çalışıyor ve API hâlâ hazır")
        print(f"{len(run.report['checks'])} otomatik kontrol geçti. Tarayıcı kabulü bekleniyor.", flush=True)
    except BaseException:
        run.report["status"] = "failed"
        if run.safe:
            try:
                run.compose("stop", "worker", timeout=60)
            except RecoveryError:
                print("Prova worker'ı durdurulamadı; status/logs ile kontrol edin.", file=sys.stderr)
        raise
    finally:
        if run.directory.exists():
            run.report["total_seconds_including_build_and_input"] = round(time.monotonic() - started, 2)
            run.save()
            print("Rapor:", run.directory / "report.json", flush=True)
            print("Temizlik: python3 scripts/rehearse_restore.py cleanup --run-id " + run.report["run_id"], flush=True)


if __name__ == "__main__":
    try:
        main()
    except (RecoveryError, ValueError, OSError, KeyError, tarfile.TarError) as error:
        # Detailed HTTP/DB bodies and environment values must never be printed.
        message = str(error) if isinstance(error, RecoveryError) else type(error).__name__
        print("Prova tamamlanamadı:", message, file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print("Prova kesildi; rapordaki kimlikle durum/temizlik komutunu kullanın.", file=sys.stderr)
        sys.exit(130)
