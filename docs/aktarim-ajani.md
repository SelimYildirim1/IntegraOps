# Aktarım ajanı (Go)

`integraops-agent`, müşteri sisteminin yanında çalışan ve kalıcı olay kayıtlarını IntegraOps API'sine taşıyan bağımsız bir Go programıdır. Kaynak sistemin kendi veritabanı işlemiyle yazdığı outbox kaydını okur; ağ kesintisinde kayıtlar kaynakta bekler, bağlantı gelince aynı olay kimlikleriyle gönderilir. Ajan işlem durumu ya da alarm kuralı içermez; bunlar IntegraOps çekirdeğinde kalır.

## Ne zaman kullanılır

Kaynak sistem outbox tablosunu ya da olay dosyalarını üretir ancak IntegraOps'a HTTP ile göndermek için kendi göndericisini yazmak istemez. `samples/OrderSource` içindeki .NET göndericinin yerine, farklı dillerdeki sistemlerde yeniden kullanılır.

## Kaynaklar

**PostgreSQL outbox (`SOURCE=postgres`):** Kaynak sistem iş kaydıyla aynı işlemde şu tabloya yazar:

```sql
CREATE TABLE integraops_outbox (
  id uuid PRIMARY KEY,                       -- eventId ile aynı
  body text NOT NULL,                        -- gönderilecek JSON (docs/entegrasyon-sozlesmesi.md)
  state varchar(16) NOT NULL DEFAULT 'pending',
  attempts integer NOT NULL DEFAULT 0,
  available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
  lease_token uuid, lease_until timestamptz, last_error text,
  created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX integraops_outbox_due ON integraops_outbox(available_at) WHERE state IN ('pending','sending');
```

Ajan `FOR UPDATE SKIP LOCKED` ile kayıtları kiralar; aynı tabloyu birden fazla ajan okuyabilir. `sent` kayıtlar kaynak sistemin kendi temizlik kuralıyla silinir. `dead` kayıtlar düzeltme sonrası `state='pending'` yapılarak yeniden gönderilir.

**Dizin (`SOURCE=directory`):** Her olay bir JSON dosyasıdır. Kaynak sistem dosyayı geçici adla yazıp `<DIRECTORY>/pending/` altına yeniden adlandırır. Ajan dosyayı `sending/` üzerinden `sent/` (ya da silme) veya `dead/` + `.error` dosyasına taşır. Tekrar denemeler dosya adındaki `<zaman>__<deneme>__<ad>` önekiyle planlanır.

## Gönderim kuralları

| Yanıt | Sonuç |
|---|---|
| 202, 200 | `sent`; yanıttaki `eventId` gönderilenle aynı olmalı, değilse tekrar denenir |
| 408, 425, 429, 5xx, ağ hatası | `pending`, üstel bekleme: 2, 4, 8 … 64 saniye |
| 400, 401, 403, 404, 409 | `dead`, `last_error=http_XXX` |
| Gövdedeki `eventId` kayıt kimliğiyle uyuşmuyor ya da UUID değil | `dead`, istek gönderilmez |

API'ye hiç ulaşılamıyorsa parti bekletilir, deneme sayacı hızla artmaz ve 5 saniye sonra yeniden denenir. 50 başarısız denemeden sonra kayıt `dead` olur.

## Yapılandırma

`agent/agent.conf.example` dosyasındaki anahtarlar `AGENT_` önekiyle ortam değişkeni olarak da verilebilir. Uzak adresler için `API_URL` https olmalıdır. `-check` seçeneği yapılandırmayı doğrulayıp çıkar.

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| `NAME` | makine adı | Projede tekil ajan adı |
| `BATCH` | 50 | Her turda kiralanan kayıt |
| `CONCURRENCY` | 4 | Paralel gönderim |
| `RATE_PER_SEC` | 50 | Saniyedeki en fazla gönderim; 0 sınırsız |
| `LEASE` | 60s | Kiralanan kaydın başka ajana açılma süresi |
| `LISTEN` | 127.0.0.1:9464 | `/status`, `/metrics`, `/health` |
| `HEARTBEAT` | 30s | Merkeze yaşam sinyali aralığı |

## Merkeze raporlama

Ajan `POST /api/v1/agents/heartbeat` ile adını, sürümünü, makine adını, kaynak türünü, bekleyen ve gönderilen sayılarını, son hatayı ve gizli değer içermeyen yapılandırma özetini bildirir. Panelin Sistem ekranındaki "Aktarım ajanları" tablosu bunları gösterir; 2 dakikadır sinyal vermeyen ajan "Sinyal yok" olur. `/metrics` ucu Prometheus biçiminde `integraops_agent_pending`, `integraops_agent_connected`, `integraops_agent_sent_total`, `integraops_agent_dead_total` ölçümlerini verir.

## Çalıştırma

```bash
cd agent && go build -o integraops-agent ./cmd/integraops-agent
./integraops-agent -config agent.conf
```

Konteyner: `deploy/docker/Dockerfile.agent` (distroless, root olmayan kullanıcı). Sürüm etiketi itildiğinde yayın hattı `agent` imajını da yayınlar.

```bash
docker run -d --name integraops-agent --env-file agent.env -p 127.0.0.1:9464:9464 ghcr.io/selimyildirim1/integraops/agent:v1.3.0
```

## Doğrulama

```bash
python3 scripts/verify_agent.py
```

Ajanı derler, yerel PostgreSQL'de geçici outbox tablosu açar ve şunları sınar: aynı kimlikle teslim, yinelenen olay, reddedilen gövdenin `dead` olması, kimlik uyuşmazlığının gönderilmeden durdurulması, yaşam sinyalinin API'de görünmesi, API kapalıyken bekletme ve dönüşte teslim, hız sınırı, Prometheus ölçümleri, dizin kaynağı. Senaryo yerel API'yi kısa süre durdurur.
