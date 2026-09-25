# Yapay zekâ Dalga 2 — Hafıza, triyaj ve eşik danışmanı

Bu belge, `docs/yapay-zeka.md` bölüm 1'deki ürün önerisinin ilk uygulamasından (alarmı açıklama) çıkarılan dersle
yeniden kurgulanmış teknik plandır. Uygulama sırası ve her fazın kabul ölçütü aşağıdadır.

## Neden yeniden kurguluyoruz

Mevcut "Yapay zekâ ile incele" özelliği, alarm sayfasının zaten gösterdiği kanıtı (alarm, olaylar, adımlar, teslimatlar)
modele verip geri anlattırıyor. Kural motoru "fatura olayı 10 dakikada gelmedi" diye tespit etmişken modelin
"fatura tarafını kontrol edin" demesi bilgi eklemiyor.

**Ölçüt:** Bir yapay zekâ çıktısı ya kullanıcının ekranda görmediği bir şeyi söylemeli ya da elle yapacağı bir işi
yapmalı. İkisi de değilse model çağrılmaz, kart gösterilmez.

**Değişmeyen ilkeler** (`docs/yapay-zeka.md` bölüm 0): tespit ve sayılar backend'de hesaplanır, model yalnızca
anlatır ve önerir; her bulgu kanıt kimliğine bağlanır; ürün yapay zekâ olmadan çalışır. Bu dalgadaki üç özelliğin
hesaplanan kısmı **yapay zekâ kapalıyken de** panelde görünür; model üstüne anlatım ve öncelik ekler.

## Fazlar

| Faz | Özellik | Kullanıcıya somut fayda | Ölçüm |
|---|---|---|---|
| 1 | Çözüm hafızası | "Geçen sefer bu adımda ne olmuştu, nasıl çözüldü" | Neden girilen alarm oranı; hafızalı incelemelerde "faydalı" oranı |
| 2 | Etki alanı ve triyaj | 40 açık alarm → 3 sorun grubu, öncelik, kök neden hipotezi, toplu işlem | Kök nedene ulaşma süresi; grup başına tek atama |
| 3 | Eşik danışmanı | Sahte alarm üreten süre sınırlarını veriyle düzeltme önerisi | Öneri uygulandıktan sonra alarm sayısındaki düşüş |

---

## Faz 1 — Çözüm hafızası

**Sorun:** Alarm kapanınca yalnızca `resolution` (`step_completed` / `cancelled` / `tracking_changed`) kalıyor; bu
"neden oldu, nasıl çözüldü" bilgisi değil. Notlar serbest metin ve aranmıyor.

### Veri (migration `017_ai_memory.sql`)

```sql
ALTER TABLE alarms ADD COLUMN cause varchar(24)
    CHECK (cause IN ('source_outage','source_bug','data_error','threshold','manual_process','external','unknown'));
ALTER TABLE alarms ADD COLUMN fix varchar(1000);
ALTER TABLE alarms ADD COLUMN cause_set_at timestamptz;
ALTER TABLE alarms ADD COLUMN cause_set_by uuid REFERENCES app_users(id) ON DELETE SET NULL;
CREATE INDEX alarms_memory ON alarms(project_id, step_code, opened_at DESC) WHERE cause IS NOT NULL;
```

Neden sınıfları (panelde Türkçe adlarıyla):

| Kod | Ad | Anlamı |
|---|---|---|
| `source_outage` | Kaynak gönderici durdu | Kaynak iş yaptı ama olay göndermedi (outbox, ajan, ağ) |
| `source_bug` | Kaynak sistem hatası | Kaynak sistem işi yapamadı ya da hata bildirdi |
| `data_error` | Veri veya eşleme hatası | Yanlış işlem numarası, eşleme şablonu, alan eksikliği |
| `threshold` | Süre sınırı veya takvim | İş normal sürdü, sınır ya da takvim yanlış (sahte alarm) |
| `manual_process` | Elle tamamlanan iş | İş sistem dışında tamamlandı, olay hiç gelmeyecek |
| `external` | Dış taraf | Kargo, banka, üçüncü taraf gecikmesi |
| `unknown` | Belirlenemedi | Araştırıldı, neden bulunamadı |

### API

- `PUT /alarms/{id}/resolution` `{cause, fix}` — sahip/operatör. Alarm açıkken de kapalıyken de girilebilir.
  `alarm_events` tablosuna `cause_set` kaydı; audit `alarm.cause`.
- `GET /alarms/{id}` yanıtına `cause`, `fix`, `cause_set_at`, `cause_set_by_name` ve **`memory`** eklenir:
  ```json
  { "memory": { "window_days": 90, "total": 7, "with_cause": 5,
      "causes": [{"cause":"source_outage","count":3},{"cause":"threshold","count":2}],
      "recent": [{"id":"…","business_id":"ORDER-1","opened_at":"…","resolved_after_seconds":840,"cause":"source_outage","fix":"ERP outbox servisi yeniden başlatıldı","note":"…"}] } }
  ```
  Aynı akış + adım + tür, son 90 gün, en fazla 5 örnek (nedeni girilmiş olanlar önce). Yapay zekâ kapalıyken de döner.
- `GET /alarms` listesi ve CSV dışa aktarımı `cause` alanını taşır; liste filtresine `cause` eklenir.

### Kanıt ve istem

`PostgresAiStore.Evidence` şu kayıtları ekler:

| Kimlik | Tür | İçerik |
|---|---|---|
| `M1` | memory | Son 90 günde aynı akış+adım+tür: toplam, nedeni girilen sayı, neden dağılımı |
| `H1…H5` | history | Örnek geçmiş alarmlar: açılış, kapanma süresi, neden, çözüm metni, son not (500 karakter) |
| `C1…` | connection | Projedeki bağlantılar: tür, son olay zamanı, son hata, red sayısı (hedef adresler gönderilmez) |
| `G1…` | agent | Ajanlar: son görülme, bekleyen, ölü, son hata |

İstem `insight-v3`: "Geçmiş vakalardaki neden ve çözümü, kanıt kimliğiyle birlikte öner; neden dağılımına göre en
olası sınıfı belirt; bağlantı ya da ajan son 15 dakikadır sessizse bunu bulgu olarak yaz." Çıktıya `likely_cause`
(yukarıdaki sınıflardan biri, `M1`/`H*` kanıtı zorunlu) eklenir; kanıt yoksa `unknown`.

### Panel

- Alarm detayında **"Neden ve çözüm"** kutusu: neden seçimi + çözüm metni. Kapanmış alarmda öne çıkar
  ("Bu alarm nasıl çözüldü? Sonraki sefer için kaydedin.").
- **"Bu adımda daha önce"** bölümü (yapay zekâdan bağımsız): neden dağılımı ve son örnekler, her biri alarmına bağlantılı.
- Yapay zekâ kartı yalnızca söyleyecek yeni şey varsa görünür: `memory.total > 0` ya da benzer alarm > 1 ya da
  bağlantı/ajan sessiz ya da teslimat ölü. Aksi halde kart yerine tek satır: "Bu alarm için ek bağlam yok."
- Sonuçta `likely_cause` rozet olarak; "Bu nedeni alarma kaydet" düğmesi tek tıkla `PUT …/resolution` yapar.

### Kabul

- Nedeni girilmiş 3 geçmiş alarmı olan bir adımda yeni alarm açıldığında: memory bölümü dolu, yapay zekâ çıktısında
  `likely_cause` geçmiş kanıta bağlı, "Bu nedeni kaydet" alarmı günceller.
- Geçmişi olmayan alarmda yapay zekâ kartı görünmez.
- `verify_ai.py` senaryosu güncellenir; birim testi `InsightPrompt.Parse` için `likely_cause` doğrulaması.

---

## Faz 2 — Etki alanı ve triyaj

**Sorun:** Operatör sabah 40 açık alarm görüyor; hangisinin tek kök nedene bağlı, hangisinin sahte, hangisinin
sahipsiz olduğunu tek tek açarak anlıyor.

### Hesaplama (`PostgresTriageQueries`)

`GET /alarms/triage?test=exclude` — açık alarmları gruplar:

- **Grup anahtarı:** akış + adım + tür + hata parmak izi (hata metninin ilk 80 karakteri, sayılar ve kimlikler
  `#` ile maskelenmiş).
- **Grup alanları:** alarm sayısı, ilk/son açılış, en eski açık süresi, görülmemiş sayısı, sorumlusu olmayan sayısı,
  etkilenen işlem örnekleri (5), `baseline`: aynı akış+adım için son 7 günde saatlik ortalama alarm; `ratio =
  count / max(baseline, 1)`.
- **Bağlam:** son 15 dakikada olay göndermeyen ama son 24 saatte göndermiş bağlantılar; 5 dakikadır sinyal
  vermeyen ajanlar; grup adımının son 14 gün p50/p95 süresi ve sınırı (eşik şüphesi için); grup için hafıza
  (Faz 1: baskın geçmiş neden).
- **Öncelik puanı** (0–100, backend): tür `failed` +30; `ratio ≥ 3` +25; sayı ≥ 10 +15; en eski > 2 saat +10;
  sorumsuz oranı > %50 +10; eşik şüphesi (p95 < sınır ve grup `overdue`) −20.
- **Sinyal etiketi:** `spike` (ratio ≥ 3), `source_silent` (grubun akışını besleyen bağlantı/ajan sessiz),
  `threshold_suspect`, `unassigned`, `stale` (en eski > 24 saat).

Yanıt yapay zekâ kapalıyken de kullanılır.

### Yapay zekâ

- `POST /ai/triage` → `ai_analyses.subject_kind='triage'`, `subject_id` NULL, bağlam hash'i grup listesinden.
  Kanıt: `G1…` gruplar, `C1…` bağlantılar, `A1…` ajanlar, `M1…` grup hafızaları.
- İstem `triage-v1`, çıktı:
  ```json
  {"summary":"…","groups":[{"evidence":["G1"],"priority":"critical|high|normal|low","hypothesis":"…","action":"…","likely_cause":"source_outage"}],"unknowns":["…"]}
  ```
  Backend puanını modelin önceliğiyle karşılaştırır; uyuşmazlıkta backend puanı kalır, model gerekçesi gösterilir.
- Deneme profilinde (`TestMode`) yalnızca `test=only` gruplar sağlayıcıya gider.

### Toplu işlem

- `POST /alarms/bulk` `{ids:[…], op:"assign"|"acknowledge", userId}` — en fazla 200 alarm, tek işlem, tek audit.
- Eskalasyon taslağı: grup kartında "Mesaj taslağı" düğmesi; backend `GET /alarms/triage/{key}/draft` hazır metni
  üretir (sipariş numaraları, zaman aralığı, son başarılı olay, hata metni, panel bağlantısı). Model gerekmez;
  yapay zekâ açıksa aynı taslak modelle akıcılaştırılır.

### Panel

- `/alarms/triage` sayfası; alarm listesinin başında "Sorun grupları" özeti ve bağlantı. Grup kartı: etiketler,
  puan, sayı, en eski, sorumsuz; "Hepsini üstlen", "Hepsini ata", "Mesaj taslağı", "Alarmları listele"
  (`/alarms?flow=…&step=…` — liste filtresine `step` eklenir).
- Yapay zekâ açıkken "Değerlendir" düğmesi; sonuç grup kartına hipotez ve öneri olarak yazılır.

### Kabul

- Demo veriyle 3 farklı akış/adımda 30 alarm üretildiğinde 3 grup çıkar, `spike` etiketi doğru gruba düşer,
  "Hepsini üstlen" tek istekte 10 alarmı atar.
- Yapay zekâ çıktısında her grup en az bir `G*` kanıtına bağlı; bağlı olmayan grup "kanıta bağlanmadı" ile işaretlenir.

---

## Faz 3 — Eşik danışmanı

**Sorun:** Süre sınırları kurulumda tahminle giriliyor; sonra kimse veriye bakıp düzeltmiyor. Alarm yorgunluğunun
ana kaynağı bu.

### Hesaplama (`PostgresThresholdAdvisor`)

`GET /flows/{flow}/threshold-advice?days=30` — yayındaki sürümün her adımı için:

- `transaction_step_stats`: `n`, p50, p90, p95, en yüksek, `allowance_seconds`, ihlal sayısı ve oranı.
- Aynı adımın `overdue` alarmları: açılan, `step_completed` ile kapanan (sahte alarm vekili), sınır aşıldıktan
  sonra tamamlanmaya kadar geçen medyan süre.
- Zaman deseni: ihlallerin mesai dışı / hafta sonu oranı (akışın takvimi varsa takvime göre, yoksa 09–18 ve
  Pazartesi–Cuma varsayımıyla). Oran > %60 ise **takvim önerisi**.
- **Öneri kuralı:** ihlal oranı ≥ %20 ve p95 ≤ 3 × sınır → `suggested = ceil(p95 × 1.2)` "güzel" değere
  yuvarlanmış (5/10/15/30/60 dk, 2/4/8/24 sa). Tahmini etki: son 30 günde `elapsed ≤ suggested` olan ihlal sayısı
  → "önlenecek alarm". İhlal oranı < %5 ve p95 < sınır/3 → **sıkılaştırma** önerisi (aynı formül) — gecikmeyi
  daha erken yakalamak için.
- Yeterli veri yoksa (`n < 20`) öneri verilmez, "veri birikiyor" denir.

### Yapay zekâ

- `POST /flows/{flow}/ai-threshold` → `subject_kind='threshold'`, `subject_id=flow_id`. Kanıt: `S1…` adım
  istatistikleri, `P1…` zaman desenleri, `R1` alarm sayıları. İstem `threshold-v1`; çıktı her adım için
  `recommendation` (`raise|lower|calendar|keep|wait`) ve gerekçe; sayılar backend'inkiyle çelişirse backend kazanır.
- Bu fazda model isteğe bağlıdır: hesaplanan tablo tek başına anlaşılır. Model, önerileri tek paragrafta önem
  sırasına koyar ve müşteriye gönderilebilecek dille yazar.

### Uygulama

- Akış sayfasına "Süre sınırı önerileri" bölümü: adım tablosu (mevcut sınır, p50/p95, ihlal, öneri, önlenecek alarm).
- **"Taslağa uygula"**: mevcut taslak yoksa `POST /flows/{flow}/versions` ile taslak açılır, `PUT
  …/versions/{n}` ile önerilen `max_delay_seconds` yazılır; yayınlama her zaman kullanıcıda kalır (mevcut yayın akışı).
- Alarm sayfasında ilgili adım için öneri varsa tek satır: "Bu adımın sınırı için öneri var → akışa git."

### Kabul

- Demo veride bir adımın sınırı 10 dk, p95 14 dk ise öneri 20 dk çıkar; "Taslağa uygula" taslakta 1200 sn yazar.
- `n < 20` adımda öneri yok; `StepStats` birim testi + `ThresholdAdvisor` birim testi (yuvarlama, etki sayımı).

---

## Ortak işler

- `ai_analyses.subject_kind` kısıtı: `alarm, transaction, digest, triage, threshold` (migration 017).
- `InsightPrompt` → her konu için ayrı istem/parse: `AlarmInsightPrompt` (v3), `TriagePrompt`, `ThresholdPrompt`.
  Ortak kurallar tek metinde (`PromptRules`), konuya özel bölüm eklenir.
- Deneme profili sınırları (`AiLimits`) tüm konular için geçerli; triyaj ve eşik çağrıları günlük kotadan düşer.
- `openapi.yaml`: yeni uçlar; `docs/yapay-zeka.md` bölüm 1 bu belgeye bağlanır.
- `scripts/verify_ai.py`: üç faz için senaryo.

## Durum (25 Eylül 2026)

Üç faz da uygulandı ve yerelde Gemini ile uçtan uca doğrulandı (`scripts/verify_ai_wave2.py`). Plana göre farklar:

- Grup anahtarındaki hata parmak izi 6+ karakterlik onaltılık kimlikleri de maskeler.
- Hafıza, konu işlemin test işaretiyle eşleşen alarmlara bakar (test verisi gerçek tavsiyeyi şekillendirmez).
- Eskalasyon taslağı ayrı uç yerine triyaj yanıtında her grupla birlikte gelir; model taslağı akıcılaştırmaz.
- "Taslağa uygula" sunucuda yapılır (`POST …/threshold-advice/apply`), taslak yoksa oluşturur.
- Alarm sayfasındaki "bu adımın sınırı için öneri var" satırı henüz yok (akış sayfasında görülür).

## Uygulama sırası

1. Faz 1 backend (migration, resolution ucu, memory, kanıt, istem v3) → panel → test.
2. Faz 2 hesaplama + toplu işlem + sayfa → yapay zekâ triyaj → taslak.
3. Faz 3 hesaplama + akış sayfası → taslağa uygula → yapay zekâ anlatımı.
4. Belge ve OpenAPI güncellemesi, demo veri üreticisine neden/çözüm örnekleri.

Sonraki adaylar (bu dalgada değil): kaynak tarafını ajanla doğrulama (`docs/yapay-zeka.md` 1.6'ya ek), doğal dille
soru (1.3), günlük özet (1.4).
