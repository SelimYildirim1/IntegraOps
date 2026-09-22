import json
import os
import sys
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from integraops_reconciler.cli import load_config, parse_duration, run_once, validate  # noqa: E402
from integraops_reconciler.client import IntegraOpsClient  # noqa: E402
from integraops_reconciler.compare import Lookup, SourceRecord, compare  # noqa: E402
from integraops_reconciler.sources import JsonSource, rows_to_records  # noqa: E402

NOW = datetime(2026, 9, 22, 12, 0, tzinfo=timezone.utc)
GRACE = timedelta(minutes=30)


def rec(tid, minutes_ago, steps=None, status=None):
    at = NOW - timedelta(minutes=minutes_ago)
    return SourceRecord(tid, at, {s: at + timedelta(minutes=i) for i, s in enumerate(steps or [])}, set(), status)


class CompareTests(unittest.TestCase):
    def test_missing_transaction_and_step(self):
        records = [rec("A", 120, ["start", "finish"]), rec("B", 120, ["start", "finish"]), rec("C", 120, ["start"])]
        lookups = {"A": Lookup("A", True, "completed", {"start", "finish"}), "B": Lookup("B", True, "inProgress", {"start"})}
        result = compare(records, lookups, NOW, GRACE)
        kinds = {(d.transaction_id, d.kind, d.step_code) for d in result.discrepancies}
        self.assertEqual(kinds, {("B", "missing_step", "finish"), ("C", "missing_transaction", "")})
        self.assertEqual((result.source_count, result.matched_count, result.skipped_recent), (3, 1, 0))

    def test_recent_records_and_steps_are_skipped(self):
        fresh = rec("F", 5, ["start"])
        older = SourceRecord("O", NOW - timedelta(hours=2), {"start": NOW - timedelta(hours=2), "finish": NOW - timedelta(minutes=3)})
        result = compare([fresh, older], {"O": Lookup("O", True, "inProgress", {"start"})}, NOW, GRACE)
        self.assertEqual(result.skipped_recent, 1)
        self.assertEqual(result.discrepancies, [])
        self.assertEqual(result.matched_count, 1)

    def test_status_mismatch_only_without_step_problems(self):
        records = [rec("S", 120, ["start"], status="completed")]
        result = compare(records, {"S": Lookup("S", True, "inProgress", {"start"})}, NOW, GRACE)
        self.assertEqual([d.kind for d in result.discrepancies], ["status_mismatch"])
        failed = compare(records, {"S": Lookup("S", True, "failed", set(), {"start"})}, NOW, GRACE)
        self.assertEqual([d.kind for d in failed.discrepancies], ["status_mismatch"])

    def test_naive_now_rejected(self):
        with self.assertRaises(ValueError):
            compare([], {}, datetime(2026, 1, 1), GRACE)


class SourceTests(unittest.TestCase):
    def test_rows_fold_into_records(self):
        rows = [
            {"transaction_id": "X", "occurred_at": "2026-09-22T10:00:00Z", "step_code": "start", "step_at": "2026-09-22T10:00:00Z"},
            {"transaction_id": "X", "occurred_at": "2026-09-22T10:00:00Z", "step_code": "finish", "step_at": "2026-09-22T11:00:00+03:00", "status": "completed"},
            {"transaction_id": "X", "occurred_at": "2026-09-22T09:59:00Z", "step_code": "fail", "step_status": "failed"},
            {"transaction_id": "Y", "occurred_at": datetime(2026, 9, 22, 9, 0)},
        ]
        records = {r.transaction_id: r for r in rows_to_records(rows)}
        self.assertEqual(records["X"].occurred_at, datetime(2026, 9, 22, 9, 59, tzinfo=timezone.utc))
        self.assertEqual(set(records["X"].completed_steps), {"start", "finish"})
        self.assertEqual(records["X"].completed_steps["finish"], datetime(2026, 9, 22, 8, 0, tzinfo=timezone.utc))
        self.assertEqual(records["X"].failed_steps, {"fail"})
        self.assertEqual(records["X"].status, "completed")
        self.assertEqual(records["Y"].occurred_at.tzinfo, timezone.utc)

    def test_json_source_filters_window(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "rows.json"
            path.write_text(json.dumps([
                {"transaction_id": "in", "occurred_at": "2026-09-22T10:00:00Z"},
                {"transaction_id": "out", "occurred_at": "2026-09-20T10:00:00Z"}]))
            records = JsonSource(str(path)).fetch(datetime(2026, 9, 22, tzinfo=timezone.utc), datetime(2026, 9, 23, tzinfo=timezone.utc))
            self.assertEqual([r.transaction_id for r in records], ["in"])


class FakeApi(BaseHTTPRequestHandler):
    runs = []
    lookups = {}
    open_since = []

    def log_message(self, *_):
        pass

    def _send(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.headers.get("X-API-Key") != "k" * 40:
            return self._send(401, {"title": "anahtar"})
        self._send(200, {"items": [], "lastRuns": [{"name": "t", "flow": "f", "window_to": r["windowTo"]} for r in FakeApi.runs[-1:]],
                         "openSince": FakeApi.open_since})

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if self.path.endswith("/lookup"):
            return self._send(200, {"flow": body["flow"], "items": [
                {"transactionId": t, "exists": t in FakeApi.lookups, **FakeApi.lookups.get(t, {"status": "", "completedSteps": [], "failedSteps": []})}
                for t in body["transactionIds"]]})
        FakeApi.runs.append(body)
        self._send(201, {"runId": "r", "newCount": len(body["discrepancies"]), "repeatedCount": 0, "resolvedCount": 0})


class CliTests(unittest.TestCase):
    def test_config_and_durations(self):
        self.assertEqual(parse_duration("90m"), timedelta(minutes=90))
        with self.assertRaises(ValueError):
            parse_duration("1 week")
        with tempfile.TemporaryDirectory() as tmp:
            conf = Path(tmp) / "r.conf"
            data = Path(tmp) / "rows.json"
            data.write_text("[]")
            conf.write_text(f"NAME=t\nAPI_URL=https://api.example.com\nAPI_KEY={'k' * 40}\nFLOW=f\nSOURCE=json\nJSON_FILE={data}\nGRACE=10m\n")
            os.environ["RECONCILER_WINDOW"] = "2h"
            try:
                cfg = validate(load_config(str(conf)))
            finally:
                del os.environ["RECONCILER_WINDOW"]
            self.assertEqual(cfg["WINDOW_TD"], timedelta(hours=2))
            bad = dict(cfg)
            bad["API_URL"] = "http://api.example.com"
            with self.assertRaises(ValueError):
                validate(bad)

    def test_run_once_against_fake_api(self):
        server = HTTPServer(("127.0.0.1", 0), FakeApi)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        FakeApi.runs.clear()
        FakeApi.lookups = {"known": {"status": "completed", "completedSteps": ["start"], "failedSteps": []}}
        try:
            with tempfile.TemporaryDirectory() as tmp:
                data = Path(tmp) / "rows.json"
                data.write_text(json.dumps([
                    {"transaction_id": "known", "occurred_at": (NOW - timedelta(hours=2)).isoformat(), "step_code": "start"},
                    {"transaction_id": "lost", "occurred_at": (NOW - timedelta(hours=3)).isoformat()},
                    {"transaction_id": "fresh", "occurred_at": (NOW - timedelta(minutes=2)).isoformat()}]))
                cfg = validate({"NAME": "t", "API_URL": f"http://127.0.0.1:{server.server_port}", "API_KEY": "k" * 40, "FLOW": "f",
                                "SOURCE": "json", "JSON_FILE": str(data), "WINDOW": "24h", "OVERLAP": "1h", "GRACE": "30m", "INTERVAL": "15m"})
                client = IntegraOpsClient(cfg["API_URL"], cfg["API_KEY"], "test")
                first = run_once(cfg, client, JsonSource(str(data)), now=NOW)
                self.assertEqual((first["sourceCount"], first["matched"], first["newCount"]), (2, 1, 1))
                self.assertEqual(FakeApi.runs[-1]["discrepancies"][0]["transactionId"], "lost")
                # Second run starts from the previous window end minus the overlap.
                second = run_once(cfg, client, JsonSource(str(data)), now=NOW + timedelta(minutes=15))
                self.assertEqual(second["windowFrom"], first["windowTo"] - timedelta(hours=1))
                # An open discrepancy older than that pulls the window back so it is re-checked.
                FakeApi.open_since = [{"flow": "f", "oldest": (NOW - timedelta(hours=3)).isoformat(), "open": 1}]
                third = run_once(cfg, client, JsonSource(str(data)), now=NOW + timedelta(minutes=30))
                self.assertEqual(third["windowFrom"], NOW - timedelta(hours=3, minutes=1))
                self.assertEqual(third["sourceCount"], 3)  # "fresh" is now inside the window and only skipped by grace
                self.assertEqual(third["skippedRecent"], 0)  # at NOW+30m "fresh" is older than the grace period
        finally:
            server.shutdown()


if __name__ == "__main__":
    unittest.main()
