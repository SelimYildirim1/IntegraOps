# Dalga 1 — Göster ve ikna et

Tarih: 24 Eylül 2026. Migration: `015_wave1.sql`. Kaynak plan: `docs/urun-eksikleri-ve-pazarlama.md` bölüm 7. Bu dalga yeni bir iş kuralı eklemez; ürünün değerini bir şirketin ilk toplantıda görmesini sağlayan katmanı ekler. Yapay zekâ model adaptörü (B5) ayrı çalışmada Gemini ile ekleniyor (`docs/yapay-zeka.md`).

## 1. Slack ve Microsoft Teams bildirim kanalları (A1)

`notification_channels.kind` iki yeni değer alır: `slack` ve `teams`. Her ikisi de "incoming webhook" adresiyle çalışır; adres sırrın kendisidir, ayrıca ortak sır tutulmaz. Panelde hedef yalnızca sunucu adıyla gösterilir.

| Tür | Kabul edilen adres | İleti biçimi |
|---|---|---|
| `slack` | `https://hooks.slack.com/…` ya da sunucunun izin listesindeki bir adres | Block Kit: başlık, akış/adım/neden/son zaman alanları, hata metni, "Ne yapılmalı" bölümü, **Panelde aç** ve **Kaynak sistemde aç** düğmeleri |
| `teams` | `https://*.webhook.office.com/…`, `https://*.logic.azure.com/…` (Workflows) ya da izin listesi | Adaptive Card 1.4 (`attachments[].contentType = application/vnd.microsoft.card.adaptive`); hem eski Incoming Webhook bağlayıcısı hem Workflows kabul eder |

Kurulum panelde Ayarlar → Bildirim kanalları altında anlatılır. Slack ve Teams sunucuları izin listesinde olmasa da https ile kabul edilir; SSRF koruması (özel IP reddi) aynen uygulanır. Yerel yığında `http://webhook:8080/slack` ve `/teams` yolları Slack/Teams yerine geçer (`samples/webhook/receiver.py`, `GET /chat` ile son iletiler).

E-posta iletisi de yeniden düzenlendi: adım adı, "Ne yapılmalı", kaynak sistem ve panel bağlantısı eklendi. İmzalı webhook gövdesi değişmedi; yalnızca yeni alanlar eklendi (`step_name`, `instructions`, `link`), `schema_version` 1 kaldı.

## 2. Adım başına çalışma talimatı ve kaynak sistem bağlantısı (B6)

`flow_version_steps` iki sütun kazandı: `instructions` (en çok 2000 karakter) ve `link_template` (en çok 1024). Sürümle birlikte yaşar; yayınlanan sürüm değişmez, fark görünümü `instructions` ve `link` değişikliklerini listeler.

Bağlantı şablonu https ile başlayan tam bir adrestir; `{transactionId}`, `{flow}` ve `{step}` yer tutucuları doldurulur. Örnek: `https://erp.sirket.com/orders/{transactionId}`.

Nerede görünür:

- **Alarm iletileri:** e-posta, Slack, Teams ve webhook gövdesi (alarm açıkken talimat, her durumda bağlantı).
- **Alarm sayfası:** "Ne yapılmalı" kutusu ve "Kaynak sistemde aç" düğmesi.
- **İşlem detayı:** beklenen ya da hata veren adımın altında talimat; bağlantısı olan her adımda düğme (`GET /flows/{flow}/transactions/{id}` yanıtında `steps[].instructions`, `steps[].link`).
- **Akış sayfası ve taslak düzenleyici:** adım kartında "Çalışma talimatı ve kaynak sistem bağlantısı" bölümü; yeni akış formu da aynı alanları alır (`POST /flows` `steps[].instructions`, `steps[].link`).

## 3. Sektör şablonları ve örnek veri (A6)

**Katalog** (`GET /flows/catalog`) altı şablon verir; her biri aşamalı adımlar, süreler, grup süreleri, koşullu/isteğe bağlı adımlar ve her adım için çalışma talimatı içerir:

| Kod | Sektör | Adım / aşama | Gruplar |
|---|---|---|---|
| `ecommerce-order` | E-ticaret ve pazar yeri | 8 / 6 | `export`, `vip` |
| `erp-order-invoice` | Toptan ve dağıtım | 6 / 6 | `kurumsal` |
| `logistics-delivery` | Lojistik | 6 / 6 | `express` |
| `payment-settlement` | Ödeme ve fintek | 6 / 5 | — |
| `insurance-claim` | Sigorta | 5 / 5 | `kasko` |
| `manufacturing-order` | Üretim | 6 / 5 | — |

`POST /flows/from-template {template, code?, name?}` şablonu projeye 1. sürüm olarak yayınlar; panelde Akışlar → Yeni akış sayfasındaki şablon kartları bu ucu kullanır. Şablonlar statiktir (`FlowTemplates.cs`); her şablon `WaveOneTests` içinde sürüm doğrulamasından geçer.

**Örnek veri** (`POST /flows/{flow}/demo {count ≤ 200, days ≤ 30}`) `DEMO-` önekli işlemler üretir. Olaylar gerçek kabul yolundan geçer (outbox → kuyruk → worker); yalnızca `transactions.created_at` dönem içine geri alınır ki eğilim grafikleri dolu görünsün. Senaryo dağılımı: %58 zamanında, %12 süreyi aşarak, %10 bir adımda takılı, %8 hata, %6 hata sonra düzelme, %3 iptal, %3 atlanan adım. Grup tanımlı akışlarda işlemlerin dörtte biri bir gruba düşer; koşullu ve isteğe bağlı adımlar buna göre üretilir. Şablondan gelen akışlarda hata metinleri adıma özeldir.

Örnek veri gerçek veri gibi görünür (test verisi sayılmaz), plan sayacına yazılmaz, alarmları etkin kanallara gider. `DELETE /flows/{flow}/demo` bütün örnek işlemleri bağlı kayıtlarıyla siler; işlenmemiş olay varsa 409 döner. Akış sayfası örnek işlem sayısını gösterir. Bir akışta en çok 1000 örnek işlem tutulur.

## 4. OpenAPI belgesi ve Geliştirici sayfası (A5)

`GET /openapi.yaml` (kimlik gerekmez) entegrasyon yüzeyini OpenAPI 3.1 olarak verir: olaylar, akışlar ve şablonlar, işlemler, alarmlar, anahtarlar ve kanallar, bağlantılar ve eşlemeli alım, ajan yaşam sinyali, mutabakat, raporlar; imzalı webhook gövdesi `WebhookNotification` şemasında. Kaynak dosya `backend/src/IntegraOps.Api/openapi.yaml`, API imajına gömülüdür; Postman ve Swagger UI doğrudan içe aktarır.

Paneldeki **Geliştirici** sayfası (`/developers`): beş adımda ilk bildirim, `curl` örneği, alan kuralları, kayıpsızlık sözleşmesi, Python ve Node gönderici örnekleri, alarm webhook imzası ve uç nokta tablosu. `/developers/openapi.yaml` belgeyi panel üzerinden indirir.

## API özeti

| Uç | Yetki | Amaç |
|---|---|---|
| `POST /api/v1/channels` `kind: slack\|teams` | sahip | Sohbet kanalı |
| `GET /api/v1/flows/catalog` | okuma | Şablonlar |
| `POST /api/v1/flows/from-template` | sahip, operatör | Şablondan akış |
| `POST /api/v1/flows/{flow}/demo` | sahip, operatör | Örnek veri üret |
| `DELETE /api/v1/flows/{flow}/demo` | sahip, operatör | Örnek veriyi sil |
| `PUT /api/v1/flows/{flow}/versions/{n}` `steps[].instructions`, `steps[].link` | sahip, operatör | Talimat ve bağlantı |
| `GET /openapi.yaml` | açık | OpenAPI belgesi |

Denetim kaydı: `channel.create`, `flow.template`, `demo.generate`, `demo.delete`.

## Doğrulama

```bash
dotnet test IntegraOps.slnx -c Release          # WaveOneTests: bağlantı şablonu, fark, Slack/Teams gövdesi, şablon geçerliliği
python3 scripts/verify_wave1.py                 # yerel yığın; Slack/Teams teslimatı webhook örneğinin /chat ucundan okunur
```

Doğrulama betiği: OpenAPI belgesi; Slack/Teams adres kuralları ve kanal listesi; katalog ve şablondan akış; talimat ve bağlantı doğrulaması, sürüm farkı; hata olayı → alarm → Slack ve Teams iletisinde talimat ve düğmeler → düzelme ile kapanış; örnek veri üretimi, durum dağılımı, döneme yayılma, alarm ve rapor doluluğu, silme; rol ve anahtar kısıtları; denetim kaydı.

24 Eylül 2026 sonucu: 42 kontrol geçti; 125 birim testi geçti (10 yeni); migration 015 uygulandı. Panel typecheck ve üretim derlemesi geçti; Geliştirici, yeni akış (şablon kartları), ayarlar (Slack/Teams), akış görünümü (örnek veri), alarm ve işlem detayı sayfaları sunucu tarafında doğrulandı.
