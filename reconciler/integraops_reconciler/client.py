"""IntegraOps API client for the reconciliation contract."""
from __future__ import annotations

import json
import urllib.error
import urllib.request
from datetime import datetime

from .compare import Discrepancy, Lookup


class ApiError(RuntimeError):
    def __init__(self, status: int, message: str):
        super().__init__(f"HTTP {status}: {message}")
        self.status = status


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class IntegraOpsClient:
    def __init__(self, base_url: str, api_key: str, version: str, timeout: int = 30):
        self.base = base_url.rstrip("/")
        self.key = api_key
        self.version = version
        self.timeout = timeout
        self.opener = urllib.request.build_opener(NoRedirect())

    def _call(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method, headers={
            "Content-Type": "application/json", "X-API-Key": self.key, "User-Agent": "integraops-reconciler/" + self.version})
        try:
            with self.opener.open(req, timeout=self.timeout) as r:
                raw = r.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            raw = e.read()
            try:
                title = json.loads(raw).get("title", "")
            except Exception:
                title = ""
            raise ApiError(e.code, title or "istek reddedildi") from None

    def lookup(self, flow: str, transaction_ids: list[str]) -> dict[str, Lookup]:
        out: dict[str, Lookup] = {}
        for i in range(0, len(transaction_ids), 500):
            batch = transaction_ids[i:i + 500]
            result = self._call("POST", "/api/v1/reconciliation/lookup", {"flow": flow, "transactionIds": batch})
            for item in result["items"]:
                out[item["transactionId"]] = Lookup(item["transactionId"], item["exists"], item.get("status", ""),
                                                    set(item.get("completedSteps", [])), set(item.get("failedSteps", [])))
        return out

    def record_run(self, name: str, flow: str, window_from: datetime, window_to: datetime, grace_seconds: int,
                   source_count: int, matched_count: int, discrepancies: list[Discrepancy]) -> dict:
        return self._call("POST", "/api/v1/reconciliation/runs", {
            "name": name, "flow": flow, "windowFrom": window_from.isoformat(), "windowTo": window_to.isoformat(),
            "graceSeconds": grace_seconds, "sourceCount": source_count, "matchedCount": matched_count,
            "version": self.version, "discrepancies": [d.payload() for d in discrepancies[:5000]]})

    def history(self, name: str, flow: str) -> tuple[datetime | None, datetime | None]:
        """Returns (end of this reconciler's last window, oldest open discrepancy time) for the flow."""
        result = self._call("GET", "/api/v1/reconciliation/runs?flow=" + flow)
        last = oldest = None
        for run in result.get("lastRuns", []):
            if run["name"] == name and run["flow"] == flow:
                last = datetime.fromisoformat(str(run["window_to"]).replace("Z", "+00:00"))
        for row in result.get("openSince", []):
            if row["flow"] == flow and row.get("oldest"):
                oldest = datetime.fromisoformat(str(row["oldest"]).replace("Z", "+00:00"))
        return last, oldest
