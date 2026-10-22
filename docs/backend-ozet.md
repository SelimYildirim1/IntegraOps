# Backend kısa özet

IntegraOps, kaynak sistemlerden (e-ticaret, ERP…) gelen adım olaylarını alır, her işlemin akışta nerede olduğunu izler, geciken ya da hata veren adım için alarm açar.

## Klasörler (`backend/src`)

| Proje | Ne yapar |
|---|---|
| `IntegraOps.Domain` | Saf iş kuralları, veritabanı yok. İşlem durumu (`Tracking.cs`), gecikme hesabı (`DelayEvaluator.cs`), alarm kuralları, iş takvimi, akış sürüm farkı. |
| `IntegraOps.Application` | Giriş doğrulama, API sözleşmeleri, olay eşleme, listeleme sorguları. Domain'i kullanır. |
| `IntegraOps.Infrastructure` | Dış dünya: PostgreSQL depoları (`Postgres*Store`), SQL migration'ları (`Migrations/`), RabbitMQ, e-posta/Slack/Teams gönderimi, Gemini yapay zekâ, şablon akışlar. |
| `IntegraOps.Api` | HTTP uçları: olay alma, panel, akış/sürüm, bağlantı, kurumsal (organizasyon, plan, SSO, MFA), yapay zekâ. `openapi.yaml` sözleşme. `--migrate` ile migration çalıştırır. |
| `IntegraOps.Worker` | Arka plan işleri: kuyruğu tüketir, gecikmeleri tarar, bildirim/e-posta gönderir, rapor özeti, veri saklama temizliği. |
| `tests/IntegraOps.Tests` | xUnit birim testleri (çoğu Domain kuralları). |

Bağımlılık yönü: `Api` / `Worker` → `Infrastructure` → `Application` → `Domain`.

## Teknolojiler

| Teknoloji | Neden |
|---|---|
| .NET 10, ASP.NET Core Minimal API | API ve worker |
| PostgreSQL (Npgsql, ham SQL) | Tüm veri; ORM yok, sorgular elle yazılı |
| RabbitMQ | API ile worker arasında olay kuyruğu |
| Outbox deseni | Olay ve kuyruk kaydı aynı DB işleminde yazılır, mesaj kaybolmaz |
| OpenTelemetry → Prometheus/Grafana | Metrik ve izleme |
| OpenID Connect | Kurumsal SSO girişi |
| Gemini | Alarm analizi, triyaj, eşik önerisi (isteğe bağlı) |
| Docker Compose | Yerel ve canlı çalıştırma |

## Akış: bir olayın yolculuğu

```
Kaynak sistem ──POST /events──▶ API
   1. Anahtar + plan limiti kontrolü, doğrulama
   2. events + outbox_messages tek DB işlemiyle yazılır (eventId aynıysa → duplicate, 200)
   3. 202 döner
                                  │
Worker / OutboxPublisher ─────────┘ outbox'u RabbitMQ'ya yayınlar
Worker / EventConsumer   ── olayı işler: işlem durumunu yeniden hesaplar, alarm açar/kapatır
Worker / DelayMonitor    ── süresi dolan adımları tarar → "süre aşımı" alarmı
Worker / NotificationDispatcher ── alarmı e-posta / Slack / Teams / webhook'a gönderir
Panel (Next.js) ──▶ API ── işlemleri, alarmları, raporları okur
```

## Temel kurallar

- **Akış:** Sıralı adımlar ve aşamalar. Aynı aşamadaki adımlar paralel ilerler; her adımın bir süresi vardır, `group` (ör. vip) ile farklı süre tanımlanabilir.
- **İşlem durumu:** Bir adım `cancelled` ise işlem iptal sayılır. İptal yoksa ve bir adım `failed` ise başarısız sayılır. Beklenen adımların hepsi geldiyse tamamlanır, aksi hâlde devam eder.
- **Alarm:** Koşul ortadan kalkınca kendiliğinden kapanır.
- **Sıra:** Olaylar `occurredAt` (iş zamanı) ile sıralanır, geliş sırası önemsizdir.
- **Geç teslim:** 5 dk'dan eski bir olay gelirse yeni alarmlar 2 dk bekletilir; toplu geri yüklemede alarm yağmurunu önler.
