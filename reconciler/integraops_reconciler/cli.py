"""Reconciliation run: fetch source records for a window, compare with IntegraOps, record the result.

    python -m integraops_reconciler --config reconciler.conf           # one run
    python -m integraops_reconciler --config reconciler.conf --loop    # every INTERVAL
    python -m integraops_reconciler --config reconciler.conf --check   # validate configuration only

Configuration is key=value (RECONCILER_* environment variables override):
  NAME, API_URL, API_KEY, FLOW, SOURCE (postgres|json), POSTGRES_DSN, QUERY_FILE, JSON_FILE,
  WINDOW (e.g. 24h), OVERLAP (e.g. 1h), GRACE (e.g. 30m), INTERVAL (e.g. 15m)
The query receives $1 (window start) and $2 (window end) and returns rows with columns
  transaction_id, occurred_at[, step_code, step_status, step_at, status].
"""
from __future__ import annotations

import argparse
import json
import logging
import os
import re
import sys
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path

from .client import ApiError, IntegraOpsClient
from .compare import compare
from .sources import JsonSource, PostgresSource

VERSION = "0.1.0"
log = logging.getLogger("reconciler")

DURATION = re.compile(r"^(\d+)(s|m|h|d)$")
UNITS = {"s": 1, "m": 60, "h": 3600, "d": 86400}


def parse_duration(value: str) -> timedelta:
    m = DURATION.match(value.strip())
    if not m:
        raise ValueError(f"süre biçimi geçersiz: {value} (örnek: 30m, 24h)")
    return timedelta(seconds=int(m.group(1)) * UNITS[m.group(2)])


def load_config(path: str | None) -> dict:
    values = {"SOURCE": "postgres", "WINDOW": "24h", "OVERLAP": "1h", "GRACE": "30m", "INTERVAL": "15m"}
    if path:
        for n, line in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), 1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if "=" not in line:
                raise ValueError(f"{path}: satır {n} anahtar=değer biçiminde değil")
            k, v = line.split("=", 1)
            values[k.strip().upper()] = v.strip().strip("\"'")
    for k, v in os.environ.items():
        if k.startswith("RECONCILER_"):
            values[k[len("RECONCILER_"):]] = v
    return values


def validate(values: dict) -> dict:
    problems = []
    cfg = dict(values)
    if not re.match(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", cfg.get("NAME", "")):
        problems.append("NAME gerekli (harf/rakam ile başlar, en fazla 64 karakter)")
    url = cfg.get("API_URL", "")
    if not re.match(r"^https?://[^/]+", url):
        problems.append("API_URL http(s)://sunucu biçiminde olmalı")
    elif url.startswith("http://") and not re.match(r"^http://(localhost|127\.0\.0\.1|\[::1\])", url):
        problems.append("API_URL yerel adres dışında https olmalı")
    if not 32 <= len(cfg.get("API_KEY", "")) <= 128:
        problems.append("API_KEY 32–128 karakter olmalı")
    if not re.match(r"^[a-z][a-z0-9-]{0,63}$", cfg.get("FLOW", "")):
        problems.append("FLOW akış kodu olmalı")
    if cfg["SOURCE"] == "digest-only":
        pass  # only --digest is used; no source needed
    elif cfg["SOURCE"] == "postgres":
        if not cfg.get("POSTGRES_DSN"):
            problems.append("POSTGRES_DSN gerekli")
        if not cfg.get("QUERY_FILE") or not Path(cfg["QUERY_FILE"]).is_file():
            problems.append("QUERY_FILE var olan bir SQL dosyası olmalı")
    elif cfg["SOURCE"] == "json":
        if not cfg.get("JSON_FILE") or not Path(cfg["JSON_FILE"]).is_file():
            problems.append("JSON_FILE var olan bir dosya olmalı")
    else:
        problems.append("SOURCE postgres, json veya digest-only olmalı")
    for key in ("WINDOW", "OVERLAP", "GRACE", "INTERVAL"):
        try:
            cfg[key + "_TD"] = parse_duration(cfg[key])
        except ValueError as e:
            problems.append(str(e))
    if not problems:
        if cfg["WINDOW_TD"] < timedelta(minutes=5) or cfg["WINDOW_TD"] > timedelta(days=31):
            problems.append("WINDOW 5m–31d arasında olmalı")
        if cfg["OVERLAP_TD"] > cfg["WINDOW_TD"]:
            problems.append("OVERLAP WINDOW değerini aşamaz")
        if cfg["GRACE_TD"] > timedelta(days=7):
            problems.append("GRACE en fazla 7d olabilir")
        if cfg["INTERVAL_TD"] < timedelta(minutes=1):
            problems.append("INTERVAL en az 1m olmalı")
    if problems:
        raise ValueError("; ".join(problems))
    return cfg


def make_source(cfg: dict):
    if cfg["SOURCE"] == "postgres":
        return PostgresSource(cfg["POSTGRES_DSN"], Path(cfg["QUERY_FILE"]).read_text(encoding="utf-8"))
    return JsonSource(cfg["JSON_FILE"])


def run_once(cfg: dict, client: IntegraOpsClient, source, now: datetime | None = None) -> dict:
    """One reconciliation: the window ends at now - grace and starts at the last run's end minus overlap,
    extended back to the oldest open discrepancy."""
    now = now or datetime.now(timezone.utc)
    window_to = now - cfg["GRACE_TD"]
    last, oldest_open = client.history(cfg["NAME"], cfg["FLOW"])
    window_from = (last - cfg["OVERLAP_TD"]) if last else (window_to - cfg["WINDOW_TD"])
    # Open discrepancies are re-checked every run until they resolve or fall out of WINDOW.
    if oldest_open is not None and oldest_open < window_from:
        window_from = oldest_open - timedelta(minutes=1)
    window_from = max(window_from, window_to - cfg["WINDOW_TD"])
    if window_to <= window_from:
        log.info("aralık henüz büyümedi; koşu atlandı")
        return {"skipped": True}
    records = source.fetch(window_from, window_to)
    lookups = client.lookup(cfg["FLOW"], [r.transaction_id for r in records]) if records else {}
    result = compare(records, lookups, now, cfg["GRACE_TD"])
    outcome = client.record_run(cfg["NAME"], cfg["FLOW"], window_from, window_to, int(cfg["GRACE_TD"].total_seconds()),
                                result.source_count, result.matched_count, result.discrepancies)
    log.info("koşu kaydedildi", extra={"window_from": window_from.isoformat(), "window_to": window_to.isoformat(),
                                       "source": result.source_count, "matched": result.matched_count,
                                       "skipped_recent": result.skipped_recent, "new": outcome["newCount"],
                                       "repeated": outcome["repeatedCount"], "resolved": outcome["resolvedCount"]})
    return {"skipped": False, "windowFrom": window_from, "windowTo": window_to, "sourceCount": result.source_count,
            "matched": result.matched_count, "skippedRecent": result.skipped_recent, **outcome}


class JsonFormatter(logging.Formatter):
    def format(self, record):
        data = {"time": datetime.now(timezone.utc).isoformat(), "level": record.levelname, "message": record.getMessage()}
        for key in ("window_from", "window_to", "source", "matched", "skipped_recent", "new", "repeated", "resolved"):
            if hasattr(record, key):
                data[key] = getattr(record, key)
        return json.dumps(data, ensure_ascii=False)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--config", help="key=value yapılandırma dosyası")
    parser.add_argument("--check", action="store_true", help="yapılandırmayı doğrula ve çık")
    parser.add_argument("--loop", action="store_true", help="INTERVAL aralığıyla sürekli çalış")
    parser.add_argument("--digest", choices=["daily", "weekly"], help="rapor özetini şimdi gönder ve çık (mutabakat çalıştırmaz)")
    parser.add_argument("--version", action="store_true")
    args = parser.parse_args(argv)
    if args.version:
        print(VERSION)
        return 0
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter())
    logging.basicConfig(level=logging.INFO, handlers=[handler])
    try:
        cfg = validate(load_config(args.config))
    except (ValueError, OSError) as e:
        log.error("yapılandırma geçersiz: %s", e)
        return 2
    if args.check:
        print("yapılandırma geçerli")
        return 0
    client = IntegraOpsClient(cfg["API_URL"], cfg["API_KEY"], VERSION)
    if args.digest:
        try:
            result = client.send_digest(args.digest)
        except ApiError as e:
            log.error("özet gönderilemedi: %s", e)
            return 1
        log.info("özet kuyruğa alındı: %s dönem, %s alıcı", result["period"], result["recipients"])
        return 0
    source = make_source(cfg)
    while True:
        try:
            run_once(cfg, client, source)
            if not args.loop:
                return 0
        except ApiError as e:
            log.error("IntegraOps isteği reddedildi: %s", e)
            if not args.loop:
                return 1
        except Exception as e:  # source outage, network: keep looping in service mode
            log.error("koşu tamamlanamadı: %s: %s", type(e).__name__, e)
            if not args.loop:
                return 1
        time.sleep(cfg["INTERVAL_TD"].total_seconds())


if __name__ == "__main__":
    sys.exit(main())
