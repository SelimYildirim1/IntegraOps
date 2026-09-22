"""IntegraOps sender for Python source systems (no dependencies beyond the standard library).

    from integraops_sender import IntegraOpsSender, Event
    sender = IntegraOpsSender("https://api.example.com", "io_v1_...")
    receipt = sender.send(Event(flow="erp-order-invoice", transaction_id="SO-1001", step="invoice-created",
                                occurred_at=datetime.now(timezone.utc)))

The sender is deliberately small: it builds the request, retries transient failures with backoff,
classifies permanent rejections, verifies the receipt's eventId and reports the client version.
Durable delivery still belongs to the caller: write the event to your own outbox in the same
database transaction as the business record, then call send() from a background process
(or run the IntegraOps agent against that outbox). See docs/entegrasyon-sozlesmesi.md.
"""
from __future__ import annotations

import json
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone

VERSION = "0.1.0"
_NAMESPACE = uuid.UUID("6f4a3c2e-8b71-4d5e-9a0f-2c1d3e4f5a6b")


class PermanentRejection(Exception):
    """4xx other than 408/425/429: fix the data or configuration; retrying cannot help."""

    def __init__(self, status: int, title: str):
        super().__init__(f"HTTP {status}: {title}")
        self.status = status
        self.title = title


class TemporaryFailure(Exception):
    """Network error, timeout, 5xx or throttling after all retries."""


@dataclass
class Event:
    flow: str
    transaction_id: str
    step: str
    occurred_at: datetime
    status: str = "completed"          # completed | failed | skipped | cancelled
    error: str | None = None
    group: str | None = None
    event_id: uuid.UUID | None = None  # generated deterministically when omitted

    def __post_init__(self):
        if self.occurred_at.tzinfo is None:
            raise ValueError("occurred_at must be timezone-aware")
        if self.event_id is None:
            # Same business fact → same id → safe retries and safe re-sends from a rebuilt outbox.
            self.event_id = uuid.uuid5(_NAMESPACE, "\n".join([self.flow, self.transaction_id, self.step, self.status, self.occurred_at.isoformat()]))

    def payload(self) -> dict:
        return {"eventId": str(self.event_id), "flow": self.flow, "transactionId": self.transaction_id, "step": self.step,
                "status": self.status, "occurredAt": self.occurred_at.isoformat(), "error": self.error, "group": self.group}


@dataclass
class Receipt:
    event_id: uuid.UUID
    duplicate: bool
    accepted_at: str


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


@dataclass
class IntegraOpsSender:
    base_url: str
    api_key: str
    timeout: float = 15.0
    max_attempts: int = 5
    client_name: str = "integraops-sdk-python"
    _opener: urllib.request.OpenerDirector = field(default_factory=lambda: urllib.request.build_opener(_NoRedirect()), repr=False)

    def __post_init__(self):
        self.base_url = self.base_url.rstrip("/")
        if not self.base_url.startswith(("https://", "http://localhost", "http://127.0.0.1")):
            raise ValueError("base_url must use https (http is allowed only for localhost)")
        if not 32 <= len(self.api_key) <= 128:
            raise ValueError("api_key length looks wrong")

    def _request(self, method: str, path: str, body: dict | None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base_url + path, data=data, method=method, headers={
            "Content-Type": "application/json", "X-API-Key": self.api_key, "User-Agent": f"{self.client_name}/{VERSION}"})
        with self._opener.open(req, timeout=self.timeout) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)

    def send(self, event: Event) -> Receipt:
        """Delivers one event. Raises PermanentRejection or TemporaryFailure."""
        body = event.payload()
        delay = 1.0
        for attempt in range(1, self.max_attempts + 1):
            try:
                status, receipt = self._request("POST", "/api/v1/events", body)
                if str(receipt.get("eventId", "")).lower() != str(event.event_id).lower():
                    raise TemporaryFailure("receipt eventId mismatch")  # never mark the wrong record as delivered
                return Receipt(uuid.UUID(receipt["eventId"]), bool(receipt.get("duplicate")), receipt.get("acceptedAt", ""))
            except urllib.error.HTTPError as e:
                if e.code in (408, 425, 429) or e.code >= 500:
                    last = f"HTTP {e.code}"
                else:
                    try:
                        title = json.loads(e.read()).get("title", "")
                    except Exception:
                        title = ""
                    raise PermanentRejection(e.code, title or "reddedildi") from None
            except (urllib.error.URLError, TimeoutError, OSError, TemporaryFailure) as e:
                last = str(e)
            if attempt < self.max_attempts:
                time.sleep(delay)
                delay = min(delay * 2, 30)
        raise TemporaryFailure(f"{self.max_attempts} denemeden sonra teslim edilemedi: {last}")

    def ping(self) -> dict:
        """Verifies the key and records this client's version on the connection."""
        return self._request("POST", "/api/v1/connections/ping", {})[1]

    def ingest(self, connection_id: str, payload) -> dict:
        """Posts a raw payload to a mapped connection; IntegraOps applies the connection's template."""
        return self._request("POST", f"/api/v1/connections/{connection_id}/ingest", payload)[1]
