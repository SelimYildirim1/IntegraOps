"""Source adapters. Each returns SourceRecord objects for a time window."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from .compare import SourceRecord


def _aware(value) -> datetime:
    if isinstance(value, datetime):
        return value if value.tzinfo else value.replace(tzinfo=timezone.utc)
    dt = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)


def rows_to_records(rows: Iterable[dict]) -> list[SourceRecord]:
    """Folds step rows into records.

    Each row carries transaction_id and occurred_at; optional step_code, step_status (completed/failed),
    step_at and status. A transaction may appear once (no steps) or once per step.
    """
    records: dict[str, SourceRecord] = {}
    for row in rows:
        tid = str(row["transaction_id"])
        at = _aware(row["occurred_at"])
        rec = records.get(tid)
        if rec is None:
            rec = records[tid] = SourceRecord(tid, at)
        rec.occurred_at = min(rec.occurred_at, at)
        if row.get("status"):
            rec.status = str(row["status"])
        step = row.get("step_code")
        if step:
            step_at = _aware(row.get("step_at") or at)
            if str(row.get("step_status") or "completed").lower() == "failed":
                rec.failed_steps.add(str(step))
            else:
                rec.completed_steps[str(step)] = step_at
    return list(records.values())


class PostgresSource:
    """Runs a user-supplied SQL query with $1 = window start and $2 = window end (timestamptz)."""

    def __init__(self, dsn: str, query: str):
        self.dsn = dsn
        self.query = query

    def fetch(self, window_from: datetime, window_to: datetime) -> list[SourceRecord]:
        import pg8000.dbapi  # imported lazily so unit tests need no driver
        from urllib.parse import urlparse, unquote, parse_qs

        u = urlparse(self.dsn)
        params = parse_qs(u.query)
        ssl = params.get("sslmode", ["prefer"])[0] not in ("disable", "allow")
        conn = pg8000.dbapi.connect(user=unquote(u.username or ""), password=unquote(u.password or ""), host=u.hostname or "localhost",
                                    port=u.port or 5432, database=(u.path or "/").lstrip("/"), ssl_context=True if ssl else None, timeout=30)
        try:
            conn.autocommit = True
            cur = conn.cursor()
            cur.execute("SET statement_timeout = 60000")
            # numeric paramstyle lets $1/$2 appear any number of times in the query.
            pg8000.dbapi.paramstyle = "numeric"
            cur.execute(self.query.replace("$1", ":1").replace("$2", ":2"), (window_from, window_to))
            columns = [d[0] for d in cur.description]
            rows = [dict(zip(columns, r)) for r in cur.fetchall()]
        finally:
            conn.close()
        return rows_to_records(rows)


class JsonSource:
    """Reads a JSON array of row dicts (same columns as the SQL contract); used for tests and file exports."""

    def __init__(self, path: str):
        self.path = Path(path)

    def fetch(self, window_from: datetime, window_to: datetime) -> list[SourceRecord]:
        rows = json.loads(self.path.read_text(encoding="utf-8"))
        selected = [r for r in rows if window_from <= _aware(r["occurred_at"]) < window_to]
        return rows_to_records(selected)
