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
