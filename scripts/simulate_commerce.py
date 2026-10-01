"""Send realistic e-commerce order events for the `ecommerce-order` flow template through the Python sender.

Fills a project with orders in every state (completed, in progress, failed and recovered, failed and open,
late, stuck, cancelled; standard, vip and export groups) so the panel can be evaluated before a real source is connected.

  python3 scripts/simulate_commerce.py --base-url https://API_HOST --key-file .local/simulator.json --orders 140 --hours 72
  python3 scripts/simulate_commerce.py ... --orders 8 --hours 0 --prefix CANLI     # a small batch that starts now

--key-file is a JSON file with "apiKey" (an ingest key). Re-running with the same --seed and --anchor re-sends
the same event ids, which the API reports as duplicates.
"""
import argparse
import collections
import json
import random
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "sdk" / "python"))
from integraops_sender import Event, IntegraOpsSender, PermanentRejection, TemporaryFailure  # noqa: E402

FLOW = "ecommerce-order"
M, H, D = timedelta(minutes=1), timedelta(hours=1), timedelta(days=1)

# (name, weight). Every scenario is a variation of the healthy timeline built in order_events().
SCENARIOS = [
    ("healthy", 60), ("payment-retry", 6), ("payment-cancelled", 4), ("payment-missing", 3), ("stock-retry", 5),
    ("invoice-retry", 6), ("invoice-failed", 3), ("invoice-late", 5), ("ship-missing", 4), ("ship-retry", 4),
]
ERRORS = {
    "payment-completed": ["3D Secure doğrulaması zaman aşımına uğradı.", "Banka işlemi reddetti: yetersiz bakiye (kod 51).",
                          "Ödeme sağlayıcı 502 döndü; işlem sonucu alınamadı."],
    "stock-reserved": ["Stok yetersiz: SKU ana depoda bulunamadı.", "WMS rezervasyon kuyruğu yanıt vermiyor (zaman aşımı 30 sn)."],
    "invoice-created": ["e-Fatura entegratörü 503 döndü.", "Vergi numarası doğrulanamadı; fatura oluşturulamadı.",
                        "Fatura seri numarası üretilemedi: sayaç kilitli."],
    "shipped": ["Kargo API'si etiket üretemedi (HTTP 500).", "Adres doğrulaması başarısız: posta kodu eksik."],
}


def order_events(rng, order_id, start, scenario, group):
    """Returns [(occurred_at, step, status, error)] for one order; the caller drops events that lie in the future."""
    ev = []
    add = lambda at, step, status="completed", error=None: ev.append((at, step, status, error))
    fail = lambda at, step: add(at, step, "failed", rng.choice(ERRORS[step]))
    add(start, "order-created")

    pay = start + rng.uniform(1, 6) * M
    if scenario == "payment-retry":
        fail(pay, "payment-completed"); pay += rng.uniform(4, 9) * M; add(pay, "payment-completed")
    elif scenario == "payment-cancelled":
        fail(pay, "payment-completed"); add(pay + rng.uniform(25, 40) * M, "payment-completed", "cancelled")
        return ev
    elif scenario == "payment-missing":
        pay = None
    else:
        add(pay, "payment-completed")

    stock = start + rng.uniform(2, 12) * M
    if scenario == "stock-retry":
        fail(stock, "stock-reserved"); stock += rng.uniform(15, 50) * M
    add(stock, "stock-reserved")
    if rng.random() < 0.15:
        add(stock + rng.uniform(5, 25) * M, "gift-wrap")
    if pay is None:
        return ev  # the order waits for a payment that never arrives

    invoice = max(pay, stock) + rng.uniform(8, 80) * M * (0.4 if group == "vip" else 1)
    if scenario == "invoice-failed":
        fail(invoice, "invoice-created"); return ev
    if scenario == "invoice-retry":
        fail(invoice, "invoice-created"); invoice += rng.uniform(20, 120) * M
    if scenario == "invoice-late":
        invoice = max(pay, stock) + rng.uniform(4.5, 8) * H
    add(invoice, "invoice-created")

    ready = invoice
    if group == "export":
        ready = invoice + rng.uniform(2, 10) * H; add(ready, "customs-declared")
    if scenario == "ship-missing":
        return ev
    ship = ready + rng.uniform(2, 18) * H * (0.2 if group == "vip" else 1)
    if scenario == "ship-retry":
        fail(ship, "shipped"); ship += rng.uniform(1, 4) * H
    add(ship, "shipped")
    add(ship + rng.uniform(1, 3) * D, "delivered")
    return ev


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--base-url", required=True)
    p.add_argument("--key-file", required=True)
    p.add_argument("--orders", type=int, default=140)
    p.add_argument("--hours", type=float, default=72, help="orders start within the last N hours; 0 = all start now")
    p.add_argument("--prefix", default="DEMO-SIM", help="DEMO- prefixed orders can be removed with DELETE /flows/{flow}/demo")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--anchor", help="ISO time treated as 'now' (default: current time); fix it to make re-runs idempotent")
    p.add_argument("--only", help="comma separated scenario names; default is the weighted mix")
    a = p.parse_args()

    now = datetime.fromisoformat(a.anchor) if a.anchor else datetime.now(timezone.utc)
    rng = random.Random(a.seed)
    names = a.only.split(",") if a.only else None
    sender = IntegraOpsSender(a.base_url, json.loads(Path(a.key_file).read_text())["apiKey"], client_name="integraops-simulator")
    print("bağlantı:", sender.ping())

    events, plan = [], collections.Counter()
    for i in range(1, a.orders + 1):
        # Recent hours are denser than older ones, like a shop that is busy today.
        start = now - timedelta(hours=a.hours * rng.random() ** 1.6) if a.hours else now - timedelta(seconds=(a.orders - i) * 20)
        scenario = names[(i - 1) % len(names)] if names else rng.choices([s for s, _ in SCENARIOS], [w for _, w in SCENARIOS])[0]
        group = rng.choices([None, "vip", "export"], [78, 12, 10])[0]
        order_id = f"{a.prefix}-{start:%m%d}-{i:04d}"
        plan[scenario] += 1
        for at, step, status, error in order_events(rng, order_id, start, scenario, group):
            if at <= now:
                events.append(Event(FLOW, order_id, step, at, status, error, group))
    events.sort(key=lambda e: e.occurred_at)

    result, rejected = collections.Counter(), []
    for e in events:
        try:
            result["yinelenen" if sender.send(e).duplicate else "kabul"] += 1
        except PermanentRejection as x:
            result[f"red {x.status}"] += 1
            if len(rejected) < 10: rejected.append((e.transaction_id, e.step, e.status, str(x)))
        except TemporaryFailure as x:
            result["geçici hata"] += 1
            if len(rejected) < 10: rejected.append((e.transaction_id, e.step, e.status, str(x)))
    print("senaryolar:", dict(plan))
    print("olaylar:", len(events), dict(result))
    for r in rejected: print("  red:", *r)
    return 1 if rejected else 0


if __name__ == "__main__":
    raise SystemExit(main())
