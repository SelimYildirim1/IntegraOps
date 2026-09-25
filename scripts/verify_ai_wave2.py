"""Yapay zekâ Dalga 2 doğrulaması: çözüm hafızası, sorun grupları (triyaj) ve süre sınırı danışmanı.

Yığın ayakta olmalı (docker compose up). Model açıksa (fake ya da gemini) model çağrıları da denenir; Gemini'de
çağrılar arası bekleme süresi nedeniyle betik birkaç dakika sürebilir. Model kapalıysa yalnızca hesaplanan kısımlar
doğrulanır. Panel 3000 portunda çalışıyorsa sayfa render'ı da kontrol edilir; değilse o kontroller atlanır.
"""
import json, os, subprocess, sys, time, urllib.error, urllib.request, uuid
from datetime import datetime, timedelta, timezone

BASE = "http://127.0.0.1:" + os.environ.get("API_PORT", "8080") + "/api/v1"
PANEL = "http://127.0.0.1:" + os.environ.get("PANEL_PORT", "3000")
PASSED = 0


def api(path, data=None, auth=None, method=None, raw=False):
    req = urllib.request.Request(BASE + path, data=None if data is None else json.dumps(data).encode(),
                                 method=method or ("POST" if data is not None else "GET"))
    req.add_header("Content-Type", "application/json")
    for k, v in (auth or {}).items(): req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            body = r.read(); return r.status, (body.decode() if raw else (json.loads(body) if body else None))
    except urllib.error.HTTPError as e:
        body = e.read().decode(); return e.code, (body if raw else (json.loads(body) if body else None))


def ok(name, cond, extra=""):
    global PASSED
    print(("GEÇTİ" if cond else "KALDI") + " · " + name + (f"  [{extra}]" if extra else ""), flush=True)
    if not cond: sys.exit(1)
    PASSED += 1


def signup(label, sfx):
    pw = f"{label}-Parola-{sfx}"; em = f"{label.lower()}-{sfx}@ai.test"
    s, b = api("/auth/signup", {"organizationName": f"{label} {sfx}", "name": "Op", "email": em, "password": pw}); assert s == 201, b
    own = {"Authorization": "Bearer " + b["token"], "X-Project-Id": b["projectId"]}
    for _ in range(30):
        if api("/ai/status", auth=own)[0] == 200: break
        time.sleep(1)
    return own, b["projectId"], em, pw


def ingest_key(own):
    return {"X-API-Key": api("/keys", {"name": "k", "scope": "ingest"}, own)[1]["key"]}


def send(ing, flow, tx, step, status="completed", error=None, at=None):
    s, b = api("/events", {"eventId": str(uuid.uuid4()), "flow": flow, "transactionId": tx, "step": step, "status": status,
                           "occurredAt": (at or datetime.now(timezone.utc)).isoformat(), "error": error}, ing)
    assert s == 202, (s, b)


def wait_for(fn, seconds=90):
    deadline = time.monotonic() + seconds; value = None
    while time.monotonic() < deadline:
        value = fn()
        if value: return value
        time.sleep(1)
    return value


def panel(path, em, pw, pid):
    tok = api("/auth/login", {"email": em, "password": pw})[1]["token"]
    try:
        return subprocess.run(["curl", "-s", "-m", "30", "-b", f"io_session={tok}; io_project={pid}", PANEL + path],
                              capture_output=True, text=True, check=True).stdout or None
    except subprocess.CalledProcessError:
        return None


def panel_has(html, needles):
    if html is None: print("BİLGİ · panel çalışmıyor, sayfa kontrolü atlandı"); return
    for needle in needles: ok("panel: " + needle, needle in html)


def ai_call(path, own):
    """One model call, honouring the trial profile's spacing; None when AI is off, over quota or the call failed."""
    st = api("/ai/status", auth=own)[1]
    if not st["enabled"]: print("BİLGİ · yapay zekâ kapalı, model çağrısı atlandı"); return None
    if st.get("nextAllowedAt"):
        delay = (datetime.fromisoformat(st["nextAllowedAt"].replace("Z", "+00:00")) - datetime.now(timezone.utc)).total_seconds()
        if delay > 0: time.sleep(delay + 1)
    s, b = api(path, {}, own)
    if s != 201: print("UYARI · model çağrısı başarısız:", s, b.get("title") if isinstance(b, dict) else b); return None
    print("model:", b["provider"], b["model"]); return b


# --- Faz 1: çözüm hafızası -----------------------------------------------------------------------
def phase1_memory(sfx):
    own, pid, em, pw = signup("Hafiza", sfx)
    api("/flows", {"code": "invoice", "name": "Fatura", "steps": [{"code": "order", "name": "Sipariş", "maxDelaySeconds": None},
                                                                 {"code": "invoice", "name": "Fatura", "maxDelaySeconds": 3600}]}, own)
    ing = ingest_key(own)
    for n in (1, 2):
        send(ing, "invoice", f"ORD-{sfx}-{n}", "order"); send(ing, "invoice", f"ORD-{sfx}-{n}", "invoice", "failed", "E-fatura servisi 503 döndü")
    alarms = wait_for(lambda: (lambda items: items if len(items) == 2 else None)(api("/alarms?state=open", auth=own)[1]["items"]))
    ok("İki hata alarmı açıldı", alarms and len(alarms) == 2)
    a1 = next(a for a in alarms if a["business_id"].endswith("-1"))["id"]; a2 = next(a for a in alarms if a["business_id"].endswith("-2"))["id"]
    ok("Geçersiz neden 400", api(f"/alarms/{a1}/resolution", {"cause": "gremlin"}, own, "PUT")[0] == 400)
    ok("Neden ve çözüm kaydedilir (204)", api(f"/alarms/{a1}/resolution", {"cause": "source_bug", "fix": "E-fatura servisi yeniden başlatıldı; 10 dk içinde düzeldi."}, own, "PUT")[0] == 204)
    d1 = api(f"/alarms/{a1}", auth=own)[1]
    ok("Alarm detayı neden, çözüm ve kaydedeni döner", d1["alarm"]["cause"] == "source_bug" and d1["alarm"]["fix"].startswith("E-fatura") and d1["alarm"]["cause_set_by_name"] == "Op")
    ok("Geçmiş olayı 'cause_set' kaydı içerir", any(e["kind"] == "cause_set" for e in d1["events"]))
    d2 = api(f"/alarms/{a2}", auth=own)[1]; m = d2["memory"]
    ok("İkinci alarmın hafızası birinciyi neden ve çözümüyle gösterir", m["total"] == 1 and m["with_cause"] == 1 and m["recent"][0]["cause"] == "source_bug" and m["recent"][0]["fix"])
    ok("Bağlam sinyalleri: eş zamanlı benzer alarm 1", d2["context"]["similar_recent"] == 1)
    ok("Liste neden filtresi", api("/alarms?state=all&cause=source_bug", auth=own)[1]["total"] == 1 and api("/alarms?state=all&cause=none", auth=own)[1]["total"] == 1)
    ok("Liste adım filtresi", api("/alarms?state=all&step=invoice", auth=own)[1]["total"] == 2 and api("/alarms?state=all&step=order", auth=own)[1]["total"] == 0)
    csv = api("/alarms/export?state=all", auth=own, raw=True)[1]
    ok("CSV neden ve çözüm sütunlarını taşır", "neden" in csv.splitlines()[0] and "source_bug" in csv)
    ok("İzleyici yetkisi neden kaydedemez (olay anahtarı 403)", api(f"/alarms/{a1}/resolution", {"cause": "unknown"}, ing, "PUT")[0] == 403)
    an = ai_call(f"/alarms/{a2}/ai-analyses", own)
    if an:
        types = {e["type"] for e in an["evidence"]}
        ok("Kanıt hafıza, geçmiş ve kaynak kayıtlarını içerir", {"memory", "history"} <= types, ",".join(sorted(types)))
        r = an["result"]; print("ÖZET:", r["summary"]); print("likelyCause:", r.get("likelyCause"), r.get("likelyCauseEvidence"))
        ok("Model geçmiş nedeni M/H kanıtıyla bağlıyor", r.get("likelyCause") == "source_bug" and set(r.get("likelyCauseEvidence") or []) <= {"M1", "H1"} and r.get("likelyCauseEvidence"))
    panel_has(panel(f"/alarms/{a2}", em, pw, pid), ["Neden ve çözüm", "Bu adımda daha önce", "Kaynak sistem hatası"])


# --- Faz 2: sorun grupları ------------------------------------------------------------------------
def phase2_triage(sfx):
    own, pid, em, pw = signup("Triyaj", sfx)
    api("/flows", {"code": "order", "name": "Sipariş", "steps": [{"code": "order", "name": "Sipariş", "maxDelaySeconds": None},
                                                               {"code": "invoice", "name": "Fatura", "maxDelaySeconds": 3600},
                                                               {"code": "ship", "name": "Kargo", "maxDelaySeconds": 3600}]}, own)
    ing = ingest_key(own)
    for n in range(12):
        tx = f"ORD-{sfx}-{n}"; send(ing, "order", tx, "order"); send(ing, "order", tx, "invoice", "failed", f"ERP outbox timeout after 30000 ms (job 48{n}21)")
    for n in range(2):
        tx = f"ORD-{sfx}-x{n}"; send(ing, "order", tx, "order"); send(ing, "order", tx, "invoice", "failed", "Vergi numarası geçersiz")
    for n in range(3):
        tx = f"ORD-{sfx}-s{n}"; send(ing, "order", tx, "order"); send(ing, "order", tx, "invoice"); send(ing, "order", tx, "ship", "failed", "Kargo API 502")
    total = wait_for(lambda: (lambda t: t if t == 17 else None)(api("/alarms?state=open", auth=own)[1]["total"]))
    ok("17 alarm açıldı", total == 17, str(total))
    s, tr = api("/alarms/triage", auth=own); groups = tr["groups"]
    ok("Üç sorun grubu çıktı", s == 200 and len(groups) == 3 and tr["openAlarms"] == 17, str([(g["step"], g["count"]) for g in groups]))
    top = groups[0]
    ok("En büyük grup 12 alarm; sayılar maskelendiği için tek grup", top["count"] == 12 and top["step"] == "invoice")
    ok("Ani artış ve sahipsiz sinyalleri, puan 100", "spike" in top["signals"] and "unassigned" in top["signals"] and top["priority"] == 100, f"{top['signals']} {top['priority']}")
    ok("Küçük grup daha düşük puan", groups[-1]["priority"] < top["priority"])
    ok("Taslak mesaj sayı, örnek ve hata taşır", "12 işlemde hata" in top["draft"] and "ORD-" in top["draft"] and "ERP outbox" in top["draft"])
    ok("Toplu işlem: geçersiz op 400", api("/alarms/bulk", {"ids": top["ids"], "op": "delete"}, own)[0] == 400)
    ok("Toplu işlem: 200'den fazla 400", api("/alarms/bulk", {"ids": [str(uuid.uuid4()) for _ in range(201)], "op": "acknowledge"}, own)[0] == 400)
    s, r = api("/alarms/bulk", {"ids": top["ids"], "op": "assign", "userId": "me"}, own); ok("12 alarm tek istekte üstlenildi", s == 200 and r["changed"] == 12, str(r))
    ok("Tekrar üstlenme değişiklik saymaz", api("/alarms/bulk", {"ids": top["ids"], "op": "assign", "userId": "me"}, own)[1]["changed"] == 0)
    ok("12 alarm görüldü", api("/alarms/bulk", {"ids": top["ids"], "op": "acknowledge"}, own)[1]["changed"] == 12)
    g2 = api("/alarms/triage", auth=own)[1]["groups"][0]
    ok("Grup artık sahipli ve görülmüş; puan düştü", g2["unassigned"] == 0 and g2["unacknowledged"] == 0 and "unassigned" not in g2["signals"] and g2["priority"] == 90, str(g2["priority"]))
    ok("Başka projenin alarmı toplu işlemde sayılmaz", api("/alarms/bulk", {"ids": [str(uuid.uuid4())], "op": "acknowledge"}, own)[1]["changed"] == 0)
    ok("Olay anahtarı toplu işlem yapamaz (403)", api("/alarms/bulk", {"ids": top["ids"], "op": "acknowledge"}, ing)[0] == 403)
    ok("Liste adım filtresi grup bağlantısıyla uyumlu", api("/alarms?state=open&step=invoice", auth=own)[1]["total"] == 14)
    an = ai_call("/ai/triage", own)
    if an:
        ok("Kanıt grup, kaynak ve toplam kayıtlarını taşır", {e["type"] for e in an["evidence"]} >= {"group", "totals"} and all("key" in e for e in an["evidence"] if e["type"] == "group"))
        r = an["result"]; print("ÖZET:", r["summary"])
        for g in r["groups"]: print(" -", g["priority"], g["likelyCause"], g["evidence"], "|", g["hypothesis"], "→", g["action"])
        ok("Her grup en az bir G kanıtına bağlı", r["groups"] and all(g["grounded"] for g in r["groups"]))
        h = api("/ai/triage", auth=own)[1]; ok("Triyaj geçmişi saklanır ve günceldir", len(h) == 1 and h[0]["subject_kind"] == "triage" and h[0]["stale"] is False)
        ok("Alarm geçmişine karışmaz", len(api(f"/alarms/{top['ids'][0]}/ai-analyses", auth=own)[1]) == 0)
    panel_has(panel("/alarms/triage", em, pw, pid), ["Sorun grupları", "Ani artış", "Hepsini üstlen", "mesaj taslağı"])
    panel_has(panel("/alarms", em, pw, pid), ["sorun grubu", "Grupları aç"])


# --- Faz 3: süre sınırı danışmanı ---------------------------------------------------------------
def phase3_threshold(sfx):
    own, pid, em, pw = signup("Esik", sfx)
    s, b = api("/flows", {"code": "order", "name": "Sipariş", "steps": [{"code": "order", "name": "Sipariş", "maxDelaySeconds": None},
                                                                      {"code": "invoice", "name": "Fatura", "maxDelaySeconds": 600},
                                                                      {"code": "ship", "name": "Kargo", "maxDelaySeconds": 7200}]}, own); assert s in (200, 201), b
    ing = ingest_key(own)
    start = datetime.now(timezone.utc) - timedelta(days=2)
    for i in range(30):  # invoice takes 8–15 min against a 10 min limit; shipping 3–5 min against 2 h
        t0 = start + timedelta(minutes=10 * i); tx = f"ORD-{sfx}-{i}"
        send(ing, "order", tx, "order", at=t0); send(ing, "order", tx, "invoice", at=t0 + timedelta(seconds=480 + i * 14))
        send(ing, "order", tx, "ship", at=t0 + timedelta(seconds=480 + i * 14 + 180 + i * 4))
    adv = wait_for(lambda: (lambda a: a if a and a["steps"] and a["steps"][0]["n"] >= 30 else None)(api("/flows/order/threshold-advice?days=30", auth=own)[1]))
    inv = next(x for x in adv["steps"] if x["code"] == "invoice"); shp = next(x for x in adv["steps"] if x["code"] == "ship")
    ok("30 tamamlanma sayıldı", inv["n"] == 30)
    ok("Fatura: sahte alarm üreten sınır için 'yükselt' → 20 dk", inv["recommendation"] == "raise" and inv["suggestedSeconds"] == 1200 and inv["impact"] == inv["breached"] and inv["breached"] > 15, inv["reason"])
    ok("Kargo: gevşek sınır için 'düşür' → 10 dk", shp["recommendation"] == "lower" and shp["suggestedSeconds"] == 600 and shp["breachRate"] == 0, shp["reason"])
    ok("Geçersiz süre 400", api("/flows/order/threshold-advice/apply", {"steps": {"invoice": 0}}, own)[0] == 400)
    ok("Bilinmeyen adım 400 ve taslak açılmaz", api("/flows/order/threshold-advice/apply", {"steps": {"yok": 600}}, own)[0] == 400 and not api("/flows/order", auth=own)[1]["hasDraft"])
    s, r = api("/flows/order/threshold-advice/apply", {"steps": {"invoice": 1200, "ship": 600}}, own)
    ok("Öneri taslağa uygulandı; taslak oluşturuldu", s == 200 and r["created"] and r["changed"] == 2 and r["version"] == 2, str(r))
    v = api("/flows/order/versions/2", auth=own)[1]; steps = {x["code"]: x["maxDelaySeconds"] for x in v["steps"]}
    ok("Taslak 2. sürümde yeni sınırlar yazılı; yayın dokunulmadı", v["state"] == "draft" and steps["invoice"] == 1200 and steps["ship"] == 600 and api("/flows/order", auth=own)[1]["version"] == 1 and "Süre sınırı" in v["note"])
    s, r = api("/flows/order/threshold-advice/apply", {"steps": {"invoice": 1800}}, own); ok("Var olan taslak yeniden kullanılır", s == 200 and not r["created"] and r["version"] == 2)
    ok("Olay anahtarı uygulayamaz (403)", api("/flows/order/threshold-advice/apply", {"steps": {"invoice": 600}}, ing)[0] == 403)
    an = ai_call("/flows/order/ai-threshold?days=30", own)
    if an:
        ok("Kanıt adım istatistiklerini ve akışı taşır", {e["type"] for e in an["evidence"]} == {"step_stats", "flow"})
        r = an["result"]; print("ÖZET:", r["summary"])
        for x in r["steps"]: print(" -", x["recommendation"], x["evidence"], "|", x["reason"])
        ok("Model sistemin önerilerini korur ve kanıta bağlar", r["steps"] and all(x["grounded"] for x in r["steps"]) and {x["recommendation"] for x in r["steps"]} >= {"raise", "lower"})
        ok("Eşik geçmişi saklanır", len(api("/flows/order/ai-threshold?days=30", auth=own)[1]) == 1)
    panel_has(panel("/flows/view/order", em, pw, pid), ["Süre sınırı önerileri", "Yükselt", "Düşür", "gecikme alarmı önlenebilirdi"])


if __name__ == "__main__":
    print("== Faz 1: çözüm hafızası"); phase1_memory(uuid.uuid4().hex[:6])
    print("== Faz 2: sorun grupları"); phase2_triage(uuid.uuid4().hex[:6])
    print("== Faz 3: süre sınırı danışmanı"); phase3_threshold(uuid.uuid4().hex[:6])
    print(f"TAMAM · {PASSED} kontrol geçti")
