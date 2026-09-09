**IntegraOps — ilk backend adımı**

Şirketin farklı sistemlerinden gelen durum bildirimlerini aynı işleme bağlayan bir süreç takip ürünü. IntegraOps şimdilik çalışma adıdır; marka ve alan adı çalışması ilk çalışan ürün sonrasına bırakıldı.

Bu başlangıcın hedefi: bir sipariş olayını kabul etmek, kalıcı olarak kaydetmek, RabbitMQ üzerinden işlemek ve işlem geçmişini API'den sorgulamak. Müşteri uygulamasının işlerini yeniden çalıştırmaz. Süre aşımı tespiti ve Next.js paneli sonraki teslimlerdir.

**İlk çalıştırma için Docker Compose ve Python 3 gerekir.** Proje klasöründe şu komutları çalıştırın:

```bash
python3 scripts/init_local.py
docker compose up --build -d
python3 scripts/verify_api.py
```

İlk komut yerel parolaları ve kurulum anahtarını üretir. Var olan .env dosyasını değiştirmez. İkinci komut PostgreSQL, RabbitMQ, migration, API ve işleyiciyi başlatır. Son komut iki ayrı demo proje oluşturur, gerçek servisler üzerinden bildirim gönderir ve sonucu kontrol eder. İlk açılışta görüntülerin ve .NET paketlerinin indirilmesi gerekir.

| Yerel adres | Amaç |
| --- | --- |
| http://127.0.0.1:8080/health/live | API süreci çalışıyor mu? |
| http://127.0.0.1:8080/health/ready | API veritabanına ve şemaya erişebiliyor mu? |
| http://127.0.0.1:15679 | RabbitMQ yönetim arayüzü |
| 127.0.0.1:54329 | PostgreSQL bağlantısı |
| 127.0.0.1:56729 | RabbitMQ AMQP bağlantısı |

RabbitMQ ve PostgreSQL kullanıcı adı integraops; parolalar yerel .env dosyasında. Portlar aynı dosyadan değiştirilebilir. Örnek doğrulama sonucu oluşan proje anahtarı .local/demo-project.json dosyasına yazılır. .env ve .local dizini Git ve Docker bağlamından dışlanmıştır.

```bash
docker compose logs --tail=100 api worker migrate
docker compose down
```

İlk komut sorun incelemek, ikinci komut servisleri durdurmak içindir. down komutu veri disklerini silmez.

**Başlangıç kodunun parçaları:**

| Proje/dosya | Sorumluluk |
| --- | --- |
| backend/src/IntegraOps.Domain | Akış modeli, gözlenen olaylar ve zaman çizelgesi kuralları |
| backend/src/IntegraOps.Application | İstek sözleşmeleri, doğrulama ve veri erişim arayüzü |
| backend/src/IntegraOps.Infrastructure | Npgsql ile PostgreSQL erişimi, migration ve outbox aktarımı |
| backend/src/IntegraOps.Api | HTTP uçları ve projeye ait anahtar kontrolü |
| backend/src/IntegraOps.Worker | RabbitMQ yayınlama ve olay işleme |
| backend/tests/IntegraOps.Tests | Sıra dışı olay ve veri doğrulama senaryoları |
| scripts/verify_api.py | Gerçek API, veritabanı, kuyruk ve işleyiciyi birlikte doğrulama |
| docs/architecture/0001-first-slice.md | İlk teknik kararlar ve sınırlar |
| docs/sistem-mimarisi.md | Öğrenme notu: ürün amacı, katmanlar, outbox akışı |

.NET 10 hedeflenmiştir; SDK seçimi global.json içindedir. Paket sürümleri Directory.Packages.props dosyasında sabittir. Veri erişiminde başlangıç için Npgsql ve açık SQL kullanıldı; şema, indeksler ve veri bütünlüğü kuralları tek SQL migration'ında görülebilir. Npgsql'in veri kaynağı ve bağlantı havuzu yaklaşımı için [resmî temel kullanım belgesine](https://www.npgsql.org/doc/basic-usage.html) bakılabilir.

**API sözleşmesi:**

| İstek | Yetki | Sonuç |
| --- | --- | --- |
| POST /api/v1/bootstrap/projects | X-Bootstrap-Key | Yerel geliştirme için yeni firma/proje ve bir defa gösterilen proje anahtarı |
| POST /api/v1/flows | X-API-Key | Değiştirilemeyen sıralı akış tanımı |
| POST /api/v1/events | X-API-Key | Olayı kabul etme; ilk gönderimde 202, aynı içeriğin tekrarında 200 |
| GET /api/v1/flows/{flow}/transactions/{transactionId} | X-API-Key | Projeye ait işlem ve gözlenen adımlar |

Olay örneği:

```json
{
  "eventId": "6982e8d8-212f-423e-a37d-18665d0d7362",
  "flow": "order-flow",
  "transactionId": "ORDER-10092",
  "step": "payment-completed",
  "status": "completed",
  "occurredAt": "2026-09-07T13:02:13+03:00"
}
```

Aynı olayı tekrar gönderirken eventId ve içeriği koruyun. Yeni bir iş olayı veya yeni deneme sonucu için yeni eventId üretin. Aynı projede aynı eventId farklı içerikle kullanılırsa 409 döner. status yalnızca completed veya failed olabilir; failed olayına isteğe bağlı error açıklaması eklenebilir. occurredAt saat dilimi içermelidir. Sunucu kabul zamanını ayrıca kaydeder.

Akışlar 2–30 adımdan oluşur. İlk adımın maxDelaySeconds değeri null, diğerlerininki 1–86400 saniye olmalıdır. Süre kuralları bu teslimde saklanır; henüz gecikme tespiti yapılmaz. Akış güncelleme ucu yoktur, böylece mevcut işlemlerin kuralları sessizce değişmez.

**Kabul edilme ve işlenme ayrı durumlardır.** 202 yanıtı, olay ile outbox kaydının aynı veritabanı işlemi içinde kaydedildiğini belirtir. RabbitMQ'dan onay alındığında outbox kaydı yayımlanmış işaretlenir. İşleyici veritabanı güncellemesini bitirdikten sonra kuyruk mesajını onaylar. Yayınlama onaylarının davranışı [RabbitMQ'nun resmî örneğinde](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet) açıklanır.

Kuyruk veya işleyici beklerken acceptedEventCount ile processedEventCount farklı olabilir. İşlem görünümünde henüz bildirimi işlenmemiş adım not_observed olarak gösterilir. Bu, o iş adımının kesinlikle yapılmadığı anlamına gelmez. pending, inProgress, failed ve completed bu teslimin işlem durumlarıdır.

**.NET SDK ile çalışmak için** önce PostgreSQL ve RabbitMQ'yu açın:

```bash
docker compose up -d postgres rabbitmq
```

IDE'nin veya terminalin ortam değişkenlerine, .env içindeki değerleri kullanarak şunları tanımlayın:

```text
ConnectionStrings__Postgres=Host=127.0.0.1;Port=54329;Database=integraops;Username=integraops;Password=<POSTGRES_PASSWORD>
RabbitMQ__Uri=amqp://integraops:<RABBITMQ_PASSWORD>@127.0.0.1:56729/
Bootstrap__Key=<BOOTSTRAP_KEY>
ASPNETCORE_URLS=http://127.0.0.1:8080
```

Buradaki köşeli ayraçlı alanları kendi yerel değerlerinizle değiştirin; .NET, .env dosyasını kendiliğinden okumaz. Önce migration'ı çalıştırın, ardından API ve işleyiciyi ayrı terminallerde açın:

```bash
dotnet run --project backend/src/IntegraOps.Api -- --migrate
dotnet run --project backend/src/IntegraOps.Api
dotnet run --project backend/src/IntegraOps.Worker
```

**Doğrulama komutları:**

```bash
dotnet build IntegraOps.slnx -c Release
dotnet test IntegraOps.slnx -c Release --no-build
python3 scripts/verify_api.py
```

Python doğrulaması çalışan servisler gerektirir. Aynı olayın tekrarı, aynı kimlikle farklı içerik, farklı projelerde aynı işlem/olay kimlikleri, eşzamanlı gönderim, ters sırada teslim ve eski hatanın geç gelmesini kontrol eder. Her çalıştırma yeni demo firma/projeleri oluşturur. GitHub Actions iş akışı bu komutları yeni bir ortamda çalıştıracak şekilde hazırlanmıştır; herhangi bir uzak depoya gönderilmemiştir.

**İlk teslimin sınırı:** Bu bir backend başlangıcıdır. Panel, kullanıcı girişi ve üyelikler, anahtar kapsamları/yenileme arayüzü, süre aşımı taraması, dış alarm kanalları, veri saklama politikası, yük testi ve üretim işletimi henüz tamamlanmadı. Kurulum ucu yerel geliştirme içindir. Ürün müşteri uygulamasına bildirim üreten bir SDK da henüz sunmaz; müşteriyi bekletmeyen güvenilir gönderim tarafı ayrıca tasarlanacaktır.

Bir sonraki ürün adımı, ERP bildirimi geldikten sonra tanımlanan sürede fatura bildirimi bulunmadığında işlemi gecikmiş işaretlemek ve geç gelen bildirimle durumu düzeltmektir. Ardından Next.js işlem ekranı aynı API üzerine bağlanacaktır.
