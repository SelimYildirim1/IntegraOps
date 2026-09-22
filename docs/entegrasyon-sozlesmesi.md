# Kaynak sistem entegrasyon sözleşmesi

Bu belge, IntegraOps'a olay gönderecek gerçek kaynak sistemin uyması gereken kuralları tanımlar. İlk entegrasyon **ERP sipariş kaydı → fatura oluşturulması** sürecidir. Ödeme ve kargo adımları ilk kabulden sonra aynı kurallarla eklenir.

Referans uygulama `samples/OrderSource` dizinindedir. Bu uygulama aşağıdaki kuralların hepsini .NET ve PostgreSQL ile gösterir.

## 1. Akış tanımı

| Sıra | Adım kodu | İş anlamı | Önceki adımdan sonra izin verilen süre |
|---|---|---|---|
| 1 | `order-created` | Sipariş ERP'de kaydedildi | Başlangıç adımı |
| 2 | `invoice-created` | Siparişin faturası oluşturuldu | Pilot öncesi ölçülecek; başlangıç önerisi 4 saat |

Akış kodu `erp-order-invoice` olur. Süre, gerçek sistemde siparişten faturaya geçen sürelerin dağılımına göre belirlenir. Önerilen yöntem, son 30 günün 95. yüzdelik değerinin biraz üzerini seçmektir. Süre saniye cinsinden 1 ile 86400 arasında olmalıdır.

Akış panelde veya API ile bir kez oluşturulur:

```bash
curl -X POST https://API_HOST/api/v1/flows \
  -H "Authorization: Bearer OTURUM" -H "X-Project-Id: PROJE" -H "Content-Type: application/json" \
  -d '{"code":"erp-order-invoice","name":"ERP sipariş → fatura","steps":[
       {"code":"order-created","name":"Sipariş kaydı","maxDelaySeconds":null},
       {"code":"invoice-created","name":"Fatura","maxDelaySeconds":14400}]}'
```

## 2. Kimlikler

**İşlem kimliği (`transactionId`)** iki sistemde aynı iş kaydını gösterir. ERP'deki sipariş numarası kullanılır. Fatura olayı da faturanın değil siparişin numarasını taşır. Kurallar:

- 1–128 karakter. Harf, rakam, nokta, alt çizgi, iki nokta veya tire. İlk karakter harf ya da rakam.
- Sipariş numarası birden fazla şirket veya depoda tekrar edebiliyorsa önek eklenir, örneğin `TR01:SO-000123`.
- Kişisel veri, müşteri adı veya e-posta işlem kimliğine konmaz.

**Olay kimliği (`eventId`)** her iş olayı için bir kez üretilen UUID'dir. Kaynak sistem bu kimliği olayı kaydettiği anda üretir ve saklar. Tekrar gönderimde **aynı** kimlik kullanılır. IntegraOps aynı kimlik ve aynı içerikle gelen olayı yinelenen olay olarak tanır ve bir kez işler. Aynı kimlik farklı içerikle gelirse `409` döner.

## 3. Olay biçimi

```http
POST /api/v1/events
X-API-Key: io_v1_...        (yalnızca "Bildirim gönderir" yetkili anahtar)
Content-Type: application/json

{
  "eventId": "0192f3c4-8b1a-7c2e-9f10-3a4b5c6d7e8f",
  "flow": "erp-order-invoice",
  "transactionId": "SO-000123",
  "step": "invoice-created",
  "status": "completed",
  "occurredAt": "2026-09-22T10:15:00+03:00",
  "error": null
}
```

| Alan | Kural |
|---|---|
| `status` | `completed` iş adımı gerçekleşti. `failed` iş adımı denendi ve hata verdi. |
| `occurredAt` | İş olayının kaynakta gerçekleştiği an. Saat dilimi zorunlu. Gönderim anı değildir. Sunucu saatinden 5 dakikadan fazla ileride olamaz. |
| `error` | Yalnızca `failed` için. En fazla 2000 karakter. Kişisel veri, parola veya yığın izi içermez. |

## 4. Başarı, hata ve düzelme

| Durum | Gönderilecek olay | IntegraOps davranışı |
|---|---|---|
| Fatura oluştu | `invoice-created` + `completed` | Adım tamamlanır, işlem tamamlanır |
| Fatura servisi hata verdi | `invoice-created` + `failed` + açıklama | Hata alarmı açılır, kanallara bildirilir |
| Hata giderildi, fatura oluştu | Yeni `eventId` ile `invoice-created` + `completed` | Hata alarmı kendiliğinden kapanır, kapanış bildirilir |
| Fatura süre içinde gelmedi | Olay yok | Süre aşımı alarmı açılır. Fatura geç gelirse alarm kapanır |

Her deneme sonucu ayrı bir iş olayıdır ve kendi `eventId` değerini alır. Aynı olayın ağ nedeniyle tekrar gönderilmesi ise aynı `eventId` ile yapılır.

Olaylar ters sırada ulaşabilir. IntegraOps sıralamayı `occurredAt` ile yapar.

## 5. Kaynak tarafında kalıcı gönderim kuyruğu

**Temel şart:** İş kaydı ve gönderilecek olay, kaynak veritabanında **aynı veritabanı işlemi içinde** yazılır. İşlem geri alınırsa olay da kaybolur, işlem onaylanırsa olay da kalıcı olur. IntegraOps'a HTTP çağrısı bu veritabanı işleminin içinde yapılmaz.

Örnek tablo, `samples/OrderSource/Program.cs` içindeki `source_outbox` tablosudur:

```sql
CREATE TABLE source_outbox (
  id uuid PRIMARY KEY,              -- eventId
  body text NOT NULL,               -- gönderilecek JSON
  state varchar(16) NOT NULL DEFAULT 'pending',  -- pending | sending | sent | dead
  attempts integer NOT NULL DEFAULT 0,
  available_at timestamptz NOT NULL DEFAULT now(),
  lease_token uuid, lease_until timestamptz, last_error text,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Ayrı bir gönderici süreç kuyruğu okur ve sırayla gönderir:

1. Sıradaki kaydı `FOR UPDATE SKIP LOCKED` ile kilitleyip `sending` yapar ve süreli kira alır.
2. Olayı gönderir. Yanıttaki `eventId` gönderilenle aynı olmalıdır.
3. Sonuca göre kaydı günceller.

| Yanıt | Anlamı | Kaynak ne yapar |
|---|---|---|
| `202` | Kabul edildi | `sent` |
| `200` | Daha önce kabul edilmiş aynı olay | `sent` |
| `400` | Alan kuralı ihlali | `dead`; kod veya veri düzeltilir |
| `401`, `403` | Anahtar geçersiz veya yetkisiz | `dead`; anahtar kontrol edilir, sonra yeniden sıraya alınır |
| `404` | Akış bu projede yok | `dead`; akış oluşturulur, sonra yeniden sıraya alınır |
| `409` | Aynı `eventId` farklı içerikle | `dead`; kaynak hatası, incelenir |
| `408`, `425`, `429`, `5xx`, zaman aşımı, bağlantı hatası | Geçici | `pending`; üstel bekleme ile tekrar, en fazla 60 saniye aralık |

Bağlantı kesildiğinde kayıtlar kuyrukta bekler. Bağlantı gelince aynı kimliklerle gönderilir. `dead` kayıtlar kaynak sistemin kendi izleme ekranında görünmeli ve düzeltmeden sonra elle yeniden sıraya alınabilmelidir.

## 6. Güvenlik

- Kaynak sistem yalnızca "Bildirim gönderir" yetkili anahtar kullanır. Bu anahtar okuma yapamaz.
- Anahtar kaynak sistemin gizli ayarlarında tutulur, koda veya günlüğe yazılmaz.
- Anahtar yenilemede önce yeni anahtar oluşturulur, kaynak yeni anahtara geçirilir, sonra eski anahtar iptal edilir.
- Bağlantı canlı ortamda yalnızca HTTPS ile yapılır.

## 7. Kabul kontrol listesi

Gerçek kaynak sistem bağlandığında aşağıdaki adımlar pilot projede yapılır:

1. Gerçek bir sipariş oluşturulur. İşlem panelde sipariş numarasıyla görünür.
2. Faturası kesilir. İşlem tamamlanır ve iki olayın zamanları kaynaktaki zamanlarla uyuşur.
3. Kaynak ile IntegraOps arasındaki bağlantı kesilir, yeni sipariş girilir. Sipariş kaynağa kaydedilir, olay kaynağın kuyruğunda bekler.
4. Bağlantı açılır. Bekleyen olay aynı `eventId` ile gönderilir ve işlem panelde görünür.
5. Fatura hatası üretilir. Hata alarmı açılır ve alarm kanalındaki kişiye ulaşır.
6. Hata giderilip fatura kesilir. Alarm kapanır ve kapanış bildirimi ulaşır.
7. Süre aşımı denemesi için geçici kısa süreli bir test akışı kullanılır. Gecikme alarmı doğru kişiye ulaşır.

Bu adımların sonuçları `docs/yol-haritasi.md` içindeki Aşama 2 tablosuna işlenir.

## 8. Kaynak sistemin sağlaması gereken bilgiler

Entegrasyon kodunu yazmadan önce şu bilgiler gerekir:

- Kaynak uygulamanın adı, dili, çatısı ve veritabanı.
- Sipariş kaydının ve faturanın oluşturulduğu kod noktaları veya veritabanı işlemleri.
- Sipariş numarasının biçimi ve tekilliği.
- Son 30 günde siparişten faturaya geçen süre dağılımı.
- Alarmların gideceği kişi veya ekip adresleri.
- Kaynak sistemin IntegraOps API'sine ağ erişimi: doğrudan internet, VPN veya vekil sunucu.
