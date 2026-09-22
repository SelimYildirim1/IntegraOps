import json
import sys
import threading
import unittest
import uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from integraops_sender import Event, IntegraOpsSender, PermanentRejection, TemporaryFailure  # noqa: E402

KEY = "k" * 40


class Fake(BaseHTTPRequestHandler):
    script = []
    seen = []

    def log_message(self, *_):
        pass

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])) or b"{}")
        Fake.seen.append((self.path, self.headers.get("User-Agent"), body))
        status, reply = Fake.script.pop(0) if Fake.script else (202, {"eventId": body.get("eventId"), "duplicate": False, "acceptedAt": "now"})
        if callable(reply):
            reply = reply(body)
        data = json.dumps(reply).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


class SenderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = HTTPServer(("127.0.0.1", 0), Fake)
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()
        cls.sender = IntegraOpsSender(f"http://127.0.0.1:{cls.server.server_port}", KEY, max_attempts=3)
        cls.sender.send.__func__  # noqa: B018

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()

    def setUp(self):
        Fake.script.clear(); Fake.seen.clear()

    def event(self):
        return Event("f", "T-1", "s", datetime(2026, 9, 22, 10, 0, tzinfo=timezone.utc))

    def test_deterministic_event_id_and_payload(self):
        a, b = self.event(), self.event()
        self.assertEqual(a.event_id, b.event_id)
        self.assertEqual(a.payload()["occurredAt"], "2026-09-22T10:00:00+00:00")
        with self.assertRaises(ValueError):
            Event("f", "T", "s", datetime(2026, 1, 1))

    def test_success_and_user_agent(self):
        receipt = self.sender.send(self.event())
        self.assertFalse(receipt.duplicate)
        self.assertTrue(Fake.seen[0][1].startswith("integraops-sdk-python/"))
        self.assertEqual(Fake.seen[0][0], "/api/v1/events")

    def test_transient_then_success(self):
        Fake.script[:] = [(503, {"title": "down"})]
        self.sender.max_attempts = 3
        import integraops_sender
        integraops_sender.time.sleep = lambda s: None
        receipt = self.sender.send(self.event())
        self.assertEqual(len(Fake.seen), 2)
        self.assertFalse(receipt.duplicate)

    def test_permanent_rejection_is_not_retried(self):
        Fake.script[:] = [(400, {"title": "step yok"})]
        with self.assertRaises(PermanentRejection) as ctx:
            self.sender.send(self.event())
        self.assertEqual(ctx.exception.status, 400)
        self.assertEqual(len(Fake.seen), 1)

    def test_receipt_mismatch_is_retried_then_fails(self):
        import integraops_sender
        integraops_sender.time.sleep = lambda s: None
        Fake.script[:] = [(202, {"eventId": str(uuid.uuid4())})] * 3
        with self.assertRaises(TemporaryFailure):
            self.sender.send(self.event())
        self.assertEqual(len(Fake.seen), 3)

    def test_rejects_insecure_remote_url(self):
        with self.assertRaises(ValueError):
            IntegraOpsSender("http://api.example.com", KEY)


if __name__ == "__main__":
    unittest.main()
