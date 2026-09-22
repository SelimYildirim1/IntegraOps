"""Recovery boundaries. No Docker daemon or real project is used by these tests."""
import copy
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import threading
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import rehearse_restore as recovery


def archive(entries):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w:gz") as bundle:
        for name, kind, value in entries:
            item = tarfile.TarInfo(name)
            item.type = kind
            if kind == tarfile.REGTYPE:
                item.size = len(value)
                bundle.addfile(item, io.BytesIO(value))
            else:
                item.linkname = value
                bundle.addfile(item)
    return stream.getvalue()


def isolated_config():
    project = recovery.run_identity("abcdef123456")
    volumes = {n: {"name": project + "_" + n} for n in
               ("postgres_data", "rabbitmq_data", "protection_keys", "mailpit_data", "webhook_data")}
    services = {name: {"networks": {"default": {}}} for name in
                ("postgres", "rabbitmq", "keys", "api", "worker", "panel", "mailpit", "webhook")}
    for name in recovery.PUBLISHED_SERVICES:
        services[name]["networks"]["host_access"] = {}
    services["api"]["ports"] = [{"host_ip": "127.0.0.1", "target": 8080, "published": "18080"}]
    return {"name": project, "networks": {"default": {"internal": True, "name": project + "_default"},
                                          "host_access": {"name": project + "_host_access"}},
            "volumes": volumes, "services": services}


class RecoveryBoundaries(unittest.TestCase):
    def test_source_project_cannot_be_cleanup_target(self):
        for value in ("integraops", "integraops-live", "../abcdef123456", "", "ABCDEF123456"):
            with self.subTest(value=value), self.assertRaises(recovery.RecoveryError):
                recovery.run_identity(value)

    def test_env_overrides_are_removed(self):
        with patch.dict(os.environ, {"COMPOSE_FILE": "live.yml", "RESTORE_DB_PASSWORD": "wrong", "PATH": "/bin"}):
            result = recovery.clean_env()
        self.assertNotIn("COMPOSE_FILE", result)
        self.assertNotIn("RESTORE_DB_PASSWORD", result)
        self.assertEqual(result["PATH"], "/bin")

    def test_valid_archive_hashes_exact_file_bytes(self):
        data = b"<key>test-only</key>"
        result = recovery.key_inventory(archive([("./key-example.xml", tarfile.REGTYPE, data)]))
        self.assertEqual(result, {"key-example.xml": hashlib.sha256(data).hexdigest()})

    def test_archive_rejects_traversal_absolute_links_and_unexpected_files(self):
        for entry in (("../key.xml", tarfile.REGTYPE, b"x"), ("/key.xml", tarfile.REGTYPE, b"x"),
                      ("key.xml", tarfile.SYMTYPE, "/etc/passwd"),
                      ("key.xml", tarfile.LNKTYPE, "other.xml"), ("notes.txt", tarfile.REGTYPE, b"x")):
            with self.subTest(name=entry[0], kind=entry[1]), self.assertRaises(recovery.RecoveryError):
                recovery.key_inventory(archive([entry]))

    def test_archive_rejects_empty_and_duplicate_files(self):
        for items in ([], [("key.xml", tarfile.REGTYPE, b"a"), ("./key.xml", tarfile.REGTYPE, b"b")]):
            with self.assertRaises(recovery.RecoveryError):
                recovery.key_inventory(archive(items))

    def test_private_env_rejects_expansion_and_newlines(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "compose.env"
            for value in ("${SOURCE_KEY}", "a\nb", "a'b"):
                with self.assertRaises(recovery.RecoveryError):
                    recovery.write_env(path, {"RESTORE_TEST": value})
            recovery.write_env(path, {"RESTORE_TEST": "abc123"})
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_valid_isolated_config(self):
        config = isolated_config()
        recovery.validate_isolation(config, config["name"])

    def test_rejects_external_network_and_shared_volume(self):
        config = isolated_config()
        cases = []
        value = copy.deepcopy(config); value["networks"]["default"]["internal"] = False; cases.append(value)
        value = copy.deepcopy(config); value["volumes"]["postgres_data"]["name"] = "integraops_postgres_data"; cases.append(value)
        value = copy.deepcopy(config); value["volumes"]["postgres_data"]["external"] = True; cases.append(value)
        value = copy.deepcopy(config); value["services"]["api"]["ports"][0]["host_ip"] = "0.0.0.0"; cases.append(value)
        value = copy.deepcopy(config); value["services"]["worker"]["network_mode"] = "host"; cases.append(value)
        value = copy.deepcopy(config); value["services"]["api"]["volumes"] = [{"type": "bind", "source": "/", "target": "/host"}]; cases.append(value)
        value = copy.deepcopy(config); value["services"]["worker"]["networks"]["host_access"] = {}; cases.append(value)
        value = copy.deepcopy(config); value["services"]["postgres"]["ports"] = [{"host_ip": "127.0.0.1", "target": 5432}]; cases.append(value)
        value = copy.deepcopy(config); value["networks"]["host_access"]["name"] = "integraops_default"; cases.append(value)
        for value in cases:
            with self.assertRaises(recovery.RecoveryError):
                recovery.validate_isolation(value, config["name"])

    def test_requeue_refuses_running_worker(self):
        run = recovery.Rehearsal("abcdef123456")
        with patch.object(run, "compose", return_value=b"postgres\nworker\n"), patch.object(run, "sql") as sql:
            with self.assertRaises(recovery.RecoveryError):
                run.requeue()
            sql.assert_not_called()

    def test_requeue_routes_only_to_isolated_project(self):
        run = recovery.Rehearsal("abcdef123456")
        with patch.object(recovery, "command", return_value=b"") as command:
            run.sql("SELECT 1;")
        args = command.call_args.args[0]
        self.assertEqual(args[args.index("--project-name") + 1], "integraops-restore-abcdef123456")
        self.assertEqual(args[args.index("-f") + 1], str(recovery.COMPOSE))
        self.assertEqual(command.call_args.kwargs["data"], b"SELECT 1;")

    def test_http_refuses_remote_destinations_before_network(self):
        for base in ("https://example.com", "http://example.com", "http://user@127.0.0.1", "http://127.0.0.1/path"):
            with self.assertRaises(recovery.RecoveryError):
                recovery.http(base, "/login", {"password": "do-not-send"})


class LocalHTTP(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.visited = []
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                cls.visited.append(self.path)
                if self.path == "/redirect":
                    self.send_response(302)
                    self.send_header("Location", "/destination")
                    self.end_headers()
                    return
                self.send_response(200 if self.path == "/ok" else 500)
                self.end_headers()
                self.wfile.write(json.dumps({"secret": "MUST_NOT_APPEAR"}).encode())
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = "http://127.0.0.1:" + str(cls.server.server_port)

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def test_redirect_is_not_followed(self):
        with self.assertRaises(recovery.RecoveryError):
            recovery.http(self.base, "/redirect", headers={"Authorization": "Bearer test-only"})
        self.assertNotIn("/destination", self.visited)

    def test_http_error_does_not_expose_response_body(self):
        with self.assertRaises(recovery.RecoveryError) as caught:
            recovery.http(self.base, "/error")
        self.assertNotIn("MUST_NOT_APPEAR", str(caught.exception))

    def test_local_http_ignores_proxy_settings(self):
        with patch.dict(os.environ, {"http_proxy": "http://127.0.0.1:1", "no_proxy": ""}):
            self.assertEqual(recovery.http(self.base, "/ok"), {"secret": "MUST_NOT_APPEAR"})


if __name__ == "__main__":
    unittest.main()
