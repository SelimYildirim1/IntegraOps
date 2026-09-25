"""Local-only webhook receiver. Checks signature, freshness and durable deduplication."""
import hashlib
import hmac
import json
import os
import sqlite3
import time
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

root = Path(os.environ.get("RECEIVER_DATA", "/data"))
root.mkdir(parents=True, exist_ok=True)
db = sqlite3.connect(root / "receipts.sqlite")
db.execute("CREATE TABLE IF NOT EXISTS receipts(id TEXT PRIMARY KEY, body TEXT NOT NULL, received_at REAL NOT NULL)")
db.execute("CREATE TABLE IF NOT EXISTS chat(id TEXT PRIMARY KEY, body TEXT NOT NULL, received_at REAL NOT NULL)")
db.commit()
secret = os.environ["WEBHOOK_SECRET"].encode()
if len(secret) < 32:
    raise RuntimeError("WEBHOOK_SECRET en az 32 karakter olmalı.")
failures = 0

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def reply(self, code, body):
        data = json.dumps(body, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/chat":
            rows = db.execute("SELECT id,body,received_at FROM chat ORDER BY received_at DESC LIMIT 100").fetchall()
            return self.reply(200, [{"id": r[0], "kind": r[0].split(":")[0], "body": json.loads(r[1]), "receivedAt": r[2]} for r in rows])
        if self.path != "/receipts":
            return self.reply(404, {})
        rows = db.execute("SELECT id,body,received_at FROM receipts ORDER BY received_at DESC LIMIT 100").fetchall()
        self.reply(200, [{"id": r[0], "body": json.loads(r[1]), "receivedAt": r[2]} for r in rows])

    def do_POST(self):
        global failures
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 <= length <= 65536:
                return self.reply(413, {})
            body = self.rfile.read(length)
            # Local fault injection only. This receiver is never a public production service.
            if self.path == "/test/fail-next":
                failures = max(0, min(100, int(json.loads(body).get("count", 1))))
                return self.reply(200, {"failures": failures})
            # Local stand-ins for Slack and Teams incoming webhooks: the URL is the credential, no signature.
            if self.path in ("/slack", "/teams"):
                delivery = self.headers.get("X-IntegraOps-Delivery", "")
                db.execute("INSERT OR IGNORE INTO chat(id,body,received_at) VALUES(?,?,?)", (self.path[1:] + ":" + delivery, body.decode(), time.time()))
                db.commit()
                return self.reply(200, {"ok": True})
            if self.path != "/hook":
                return self.reply(404, {})
            delivery = str(uuid.UUID(self.headers.get("X-IntegraOps-Delivery", "")))
            timestamp = self.headers.get("X-IntegraOps-Time", "")
            if abs(time.time() - int(timestamp)) > 300:
                return self.reply(401, {"error": "expired"})
            signed = timestamp.encode() + b"." + delivery.encode() + b"." + body
            expected = "sha256=" + hmac.new(secret, signed, hashlib.sha256).hexdigest()
            if not hmac.compare_digest(expected, self.headers.get("X-IntegraOps-Signature", "")):
                return self.reply(401, {"error": "signature"})
            payload = json.loads(body)
            if failures:
                failures -= 1
                return self.reply(503, {"error": "local_test"})
            # A real receiver performs its business side effect in this same transaction.
            with db:
                created = db.execute("INSERT OR IGNORE INTO receipts VALUES(?,?,?)",
                    (delivery, json.dumps(payload, ensure_ascii=False), time.time())).rowcount == 1
            self.reply(200, {"accepted": True, "duplicate": not created})
        except (ValueError, json.JSONDecodeError):
            self.reply(400, {"error": "invalid_request"})

if __name__ == "__main__":
    HTTPServer((os.environ.get("RECEIVER_HOST", "0.0.0.0"), int(os.environ.get("RECEIVER_PORT", "8080"))), Handler).serve_forever()
