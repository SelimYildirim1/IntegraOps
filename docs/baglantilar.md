# Bağlantılar: tekrar kullanılabilir entegrasyonlar

Bir **bağlantı**, tek bir kaynak sistemin projeye bağlanma kaydıdır. Anahtarlar bağlantıya bağlanır; her istek bağlantının son olay zamanını, istemci adını ve sürümünü, kabul ve red sayılarını günceller. Panelin "Bağlantılar" sekmesi hangi kaynağın çalıştığını, hangisinin sessiz kaldığını ve hangisinin reddedildiğini gösterir.

## Bağlanma yolları

| Tür | Kaynak ne yapar | Ne zaman |
|---|---|---|
| `direct` | Olay biçimini kendisi üretir, `POST /api/v1/events` çağırır | Kaynak sistemde kendi outbox ve gönderici var (`samples/OrderSource`) |
| `agent` | Outbox tablosunu ya da dizini `integraops-agent` taşır | Kaynak HTTP göndermek istemiyor; ajanın `NAME` değeri bağlantı adıyla aynı olmalı |
| `sdk` | Python ya da Node gönderici kütüphanesiyle gönderir | Kaynak kodda küçük bir değişiklik kabul ediliyor |
| `mapped` | Ham JSON verisini `POST /api/v1/connections/{id}/ingest` ile gönderir; IntegraOps şablonla olaya çevirir | Kaynak kendi veri biçimini değiştiremiyor (webhook, dışa aktarım) |

## Eşleme şablonları

Şablon her olay alanı için sabit bir değer, veri içinde bir yol (`$.order.no`) ya da yedekli yol (`$.customer_class|standard`) alır. Diziler `[0]` ile, zarfın kökü `$$.` ile adreslenir. `records` alanı zarfın içindeki kayıt dizisini gösterir.

| Alan | Zorunlu | Not |
|---|---|---|
| `flow`, `transactionId`, `step`, `occurredAt` | evet | `occurredAt` ISO 8601, `YYYY-MM-DD HH:mm:ss` (UTC varsayılır) ya da unix saniye/milisaniye olabilir |
| `eventId` | hayır | Yoksa ya da kayıtta bulunamazsa işlem, adım, durum ve zamandan deterministik olarak türetilir; UUID olmayan kaynak kimlikleri de sabit bir UUID'ye çevrilir |
| `status`, `error`, `group` | hayır | `status` yoksa `completed` |
| `records` | hayır | Toplu zarf için kayıt dizisinin yolu |

Hazır şablonlar (`generic-order`, `erp-document`, `webhook-batch`) başlangıç noktasıdır; müşteri yolları düzenler. "Şablonu dene" bölümü örnek veriyle kuru çalıştırma yapar, hiçbir olay kaydetmez ve her kayıt için sorunları listeler.

Eşlemeli alım her kaydı bağımsız kabul eder: yanıt kabul, yinelenen ve reddedilen sayılarını ve kayıt başına sonucu verir. Aynı veri yeniden gönderilirse aynı olay kimlikleri üretildiği için yinelenen sayılır.

## Gönderici kütüphaneleri

- **Python:** `sdk/python/integraops_sender.py`. Standart kütüphane dışında bağımlılık yok. `IntegraOpsSender(base_url, api_key).send(Event(...))`, `ping()`, `ingest(connection_id, payload)`.
- **Node/TypeScript:** `sdk/typescript` (`@integraops/sender`). Node 20+ `fetch`. `new IntegraOpsSender({ baseUrl, apiKey }).send({...})`.

İkisi de geçici hataları üstel beklemeyle yeniden dener, kalıcı redleri (`400`, `401`, `403`, `404`, `409`) ayrı bir hata türüyle bildirir, yanıttaki olay kimliğini doğrular, `User-Agent` ile sürüm bildirir ve uzak adreslerde https zorunlu tutar. Olay kimliği verilmezse Python, Node ve sunucu aynı UUIDv5 kuralıyla türetir; bu nedenle üç taraf da aynı iş olayı için aynı kimliği üretir.

Kütüphaneler teslimatı garanti etmez; kalıcı teslimat kaynak sistemin outbox'ına ve ajana ya da kendi arka plan işine aittir (`docs/entegrasyon-sozlesmesi.md`).

## Anahtar yenileme

`POST /api/v1/keys/{id}/rotate` `{graceHours}` aynı ad, yetki ve bağlantıyla yeni anahtar üretir. Eski anahtar geçiş süresi (varsayılan 24 saat, en fazla 720) boyunca çalışmaya devam eder, sonra reddedilir. Bir anahtar bir kez yenilenir. Panelde "Yenile" düğmesi yeni anahtarı bir kez gösterir.

## Sürüm ve uyumluluk

Bağlantı, son isteğin `User-Agent` başlığından istemci adını ve sürümünü kaydeder (`integraops-agent/1.2.0`, `integraops-sdk-python/0.1.0`). Ajanlar için yaşam sinyalindeki sürüm de gösterilir. Sunucudaki en düşük uyumlu ana sürüm (`PostgresConnectionStore.MinimumAgentMajor`) altındaki istemciler "Uyumsuz istemci" olarak işaretlenir; sözleşme sürümü şimdilik 1'dir.

## API

| Uç | Yetki | Amaç |
|---|---|---|
| `GET /api/v1/connections` | okuma | Liste, sağlık ve kullanım |
| `GET /api/v1/connections/catalog` | okuma | Hazır şablonlar |
| `POST/PUT/DELETE /api/v1/connections` | sahip | Yönetim; etkin anahtarı olan bağlantı silinemez |
| `POST /api/v1/connections/test` | sahip, operatör | Şablon kuru çalıştırma |
| `POST /api/v1/connections/{id}/ingest` | o bağlantının gönderim anahtarı | Ham veriyi eşleyerek kabul |
| `POST /api/v1/connections/ping` | API anahtarı | Bağlantı testi, sürüm kaydı |
| `POST /api/v1/keys` `connectionId` | sahip | Bağlantıya bağlı anahtar |
| `POST /api/v1/keys/{id}/rotate` | sahip | Yenileme |

## Doğrulama

```bash
python3 scripts/verify_connections.py      # 32 kontrol; Node 20+ ve sdk/typescript derlemesi gerekir
cd sdk/python && python3 -m unittest test_integraops_sender
cd sdk/typescript && npm ci && npm run build && npm test
```

22 Eylül 2026 sonucu: 32 bağlantı kontrolü, 5 C# eşleme testi, 6 Python ve 6 Node SDK testi geçti; migration 010 uygulandı. Bağlantılar sayfası ve şablon denemesi tarayıcıda görüntülendi.

Hazır ERP/e-ticaret bağlayıcıları bu pakette yoktur: hangi bağlayıcının önce geleceğini gerçek müşteri ihtiyacı belirler. Eşleme şablonları ve kütüphaneler, ilk bağlayıcının üzerine kurulacağı zemini sağlar.
