"""Pure comparison logic: source records versus IntegraOps lookups. No I/O here."""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone


@dataclass
class SourceRecord:
    """One business transaction as the source system sees it."""
    transaction_id: str
    occurred_at: datetime                      # when the transaction started in the source
    completed_steps: dict[str, datetime] = field(default_factory=dict)  # step code -> when it happened
    failed_steps: set[str] = field(default_factory=set)
    status: str | None = None                  # optional source status: completed/failed/inProgress


@dataclass
class Lookup:
    transaction_id: str
    exists: bool
    status: str = ""
    completed_steps: set[str] = field(default_factory=set)
    failed_steps: set[str] = field(default_factory=set)


@dataclass
class Discrepancy:
    transaction_id: str
    kind: str          # missing_transaction | missing_step | status_mismatch
    step_code: str = ""
    detail: str = ""
    source_occurred_at: datetime | None = None

    def payload(self) -> dict:
        return {"transactionId": self.transaction_id, "kind": self.kind, "stepCode": self.step_code, "detail": self.detail,
                "sourceOccurredAt": self.source_occurred_at.isoformat() if self.source_occurred_at else None}


@dataclass
class Comparison:
    source_count: int
    matched_count: int
    skipped_recent: int
    discrepancies: list[Discrepancy]


def compare(records: list[SourceRecord], lookups: dict[str, Lookup], now: datetime, grace: timedelta) -> Comparison:
    """Compares each source record with IntegraOps.

    Anything that happened in the source less than `grace` ago is skipped: the event may simply
    still be on its way, and reporting it would produce discrepancies that resolve themselves.
    """
    if now.tzinfo is None:
        raise ValueError("now must be timezone-aware")
    cutoff = now - grace
    out: list[Discrepancy] = []
    matched = skipped = 0
    for r in records:
        if r.occurred_at > cutoff:
            skipped += 1
            continue
        found = lookups.get(r.transaction_id)
        if found is None or not found.exists:
            out.append(Discrepancy(r.transaction_id, "missing_transaction", detail="Kaynakta var, IntegraOps'ta kayıt yok", source_occurred_at=r.occurred_at))
            continue
        problems = 0
        for step, at in sorted(r.completed_steps.items(), key=lambda kv: kv[1]):
            if at > cutoff:
                continue  # the step itself may still be in flight
            if step not in found.completed_steps and step not in found.failed_steps:
                out.append(Discrepancy(r.transaction_id, "missing_step", step, "Kaynakta tamamlandı, IntegraOps'a bildirilmedi", at))
                problems += 1
        if r.status and problems == 0:
            expected = r.status.lower()
            actual = (found.status or "").lower()
            if expected in ("completed", "failed") and actual != expected:
                out.append(Discrepancy(r.transaction_id, "status_mismatch", "", f"Kaynak: {expected}, IntegraOps: {actual or 'yok'}", r.occurred_at))
                problems += 1
        if problems == 0:
            matched += 1
    return Comparison(len(records), matched, skipped, out)
