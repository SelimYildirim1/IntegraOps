# IntegraOps — V1

IntegraOps, farklı sistemlerden gelen iş olaylarını aynı işlem altında birleştirir; beklenen adımları, gecikmeleri ve kaynak sistemin bildirdiği hataları izler. Kullanıcılar panelden işlem ve alarm geçmişini inceler. IntegraOps çalışma adıdır.

**Mevcut aşama:** V1'in olay takibi, panel, kullanıcı/proje yetkileri, alarm, bildirim, kurtarma provası, sistem sağlığı izleme ve canlı işletim betikleri uygulanmıştır. Aşamaların güncel durumu `docs/yol-haritasi.md` belgesindedir. Gerçek kaynak entegrasyonu, gerçek sunucuda canlı kabul ve kontrollü pilot bekliyor.

## Mevcut modüller

- Güvenilir olay kabulü: PostgreSQL transaction + outbox, RabbitMQ ve worker.
- Olay kimliğiyle tekilleştirme, ters teslim sırasını olay zamanına göre değerlendirme.
- İşlem listesi/detayı, durum özeti, akış/durum/gecikme filtresi ve sayfalama.
- Adım bazlı süre, periyodik gecikme kontrolü, geç tamamlanma.
- Akış oluşturma, detay, kopyalama ve panelden gerçek test bildirimi.
- Giriş/çıkış/parola değiştirme, proje seçimi, sahip/operatör/izleyici rolleri.
- Gönderim ve okuma API anahtarları; iptal ve eski anahtarla uyumluluk.
- Gecikme/hata alarmları, görüldü kaydı, otomatik kapanış ve geçmiş.
- Kalıcı e-posta/webhook teslimatı, imza, yeniden deneme ve kanal yönetimi.
- Sistem görünümü, bekleyen işler, worker yaşam sinyali ve yönetim geçmişi.
- Kaynakta outbox kullanan .NET örneği ve yerel işletim betikleri.
- Uygulama kurtarma provası: yedekten ayrı ortama dönüş ve 21 otomatik kontrol.
- Sistem sağlığı: OpenTelemetry ölçümleri, Prometheus alarm kuralları, Grafana panosu.
- Canlı işletim: imaj tabanlı yayın, geri dönüş, sunucu dışı yedek ve geri yükleme betikleri.
- V1.1: işlem arama, tarih filtreleri, kayıtlı görünümler, test verisi ayrımı, alarm sorumlusu ve notları, e-posta daveti, parola sıfırlama, veri saklama, CSV dışa aktarım.
- V1.2: akış taslağı ve sürümleri, ileri tarihli etkinleşme, sürüm farkı, çalışma takvimleri, grup bazlı süreler, süre ihlali raporu.

## Gerekenler

- Docker ve Docker Compose v2.
- Python 3.10 veya üzeri.
- Panel için Node.js 24 ve npm.
- Backend'i yerel derlemek/test etmek için .NET 10 SDK. Docker derlemesi SDK'yı imaj içinde kullanır.

Paket sürümleri proje dosyalarında sabittir. Veritabanı erişiminde Npgsql ve açık SQL kullanılır.

## V1 zaten kuruluysa

Mevcut ortam ayarlarını ve veri birimlerini koruyun. Bu adımlar ilk kullanıcıyı yeniden oluşturmaz:

```bash
python3 scripts/prepare_v1.py
docker compose up --build -d
docker compose --profile demo up --build -d source
```

Panel için ayrı terminalde:

```bash
cd frontend
npm ci
npm run typecheck
npm run build
npm run start
```

Panel önceden 3000 portunda çalışıyorsa yeni sürümü başlatmadan önce eski panel sürecini durdurun. `prepare_v1.py` mevcut ayarları koruyarak eksik V1 yerel ayarlarını tamamlar. Kaynak örneği henüz yapılandırılmadıysa proje kökünde `python3 scripts/setup_v1.py` çalıştırın ve ardından kaynak servisini yeniden başlatın.

## İlk kez, boş ortamda kurulum

Aşağıdaki sıra ilk sahibi bulunmayan yeni veritabanı içindir:

```bash
python3 scripts/init_local.py
python3 scripts/prepare_v1.py
docker compose up --build -d
python3 scripts/verify_api.py
python3 scripts/setup_v1.py
docker compose --profile demo up --build -d source
```

`verify_api.py`, iki başlangıç test projesi açar ve ilk sahip kurulumunun kullanacağı demo proje erişimini yerel olarak saklar. `setup_v1.py` ilk sahibin bilgilerini ister; `pilot-source` akışını, yerel bildirim kanallarını ve kaynak uygulamasının gönderim anahtarını hazırlar. Parolalar gizli terminal girdisiyle alınır.

Paneli yukarıdaki panel komutlarıyla açın. İlk sahip kurulduğunda bootstrap ile yeni proje açma yolu kapanır. Sonraki projeler panelden oluşturulur.

## Yerel adresler

Varsayılan ayarlarla:

| Adres | Görev |
|---|---|
| http://localhost:3000 | Panel |
| http://127.0.0.1:8080/health/live | API süreci |
| http://127.0.0.1:8080/health/ready | Veritabanı ve migration 003 hazırlığı |
| http://localhost:8025 | Mailpit yerel e-posta kutusu |
| http://localhost:8082/receipts | Doğrulanmış yerel webhook kayıtları |
| http://localhost:8081/outbox | Demo kaynak gönderim durumu |
| http://127.0.0.1:15679 | RabbitMQ yönetim arayüzü |

Mailpit, webhook alıcısı ve OrderSource yerel demo araçlarıdır. Canlı Compose bunları yayınlamaz. API readiness veritabanı/şemayı kontrol eder; RabbitMQ veya bütün worker görevlerinin sağlıklı olduğunu kanıtlamaz.

## Doğrulama

### Servis gerektirmeyen kontroller

```bash
dotnet build IntegraOps.slnx -c Release
dotnet test IntegraOps.slnx -c Release --no-build
python3 -m unittest discover -s scripts/tests -v
```

Python testleri yerel sahte bir HTTP sunucusuyla doğrulama istemcisinin oturum, anahtar kapsamı, temizlik ve yönlendirme davranışını sınar. Gerçek backend/kuyruk testinin yerine geçmez.

### Kurulu V1 ortamında

API, PostgreSQL ve worker çalışırken:

```bash
python3 scripts/verify_api.py --session
python3 scripts/verify_delays.py --session
```

Bu komutlar mevcut hesabınızla giriş yapar. E-posta/parola terminalde sorulur; otomatik testte `V1_OWNER_EMAIL` ve `V1_OWNER_PASSWORD` ortam değişkenleri kullanılabilir. Her koşu yeni test projesi oluşturur. Akış oluşturma kullanıcı oturumuyla, olay gönderme `ingest`, veri okuma `read` anahtarıyla yapılır.

Test kayıtları panelde kalır. Bu iki betiğin oluşturduğu geçici anahtarlar ve oturum normal bitişte, kontrol hatasında veya Ctrl+C'de kapatılmaya çalışılır. Zorla süreç sonlandırma/ağ kesintisinde temizleme tamamlanmayabilir; test projesinin Ayarlar ekranından kalan anahtarları iptal edin. Oturumlu koşu ilk kurulumun kayıtlı demo erişimini değiştirmez.

`verify_delays.py` yaklaşık 30 saniyelik gerçek süre aşımını bekler; toplam süre ortamın hızına bağlıdır.

Mailpit, webhook ve kaynak örneği de çalışırken kapsamlı V1 kontrolü:

```bash
python3 scripts/verify_v1.py --load 100
python3 scripts/verify_v1_1.py
python3 scripts/verify_v1_2.py
```

Kesinti provası için ayrı, uygun yerel test zamanında:

```bash
python3 scripts/verify_v1.py --load 100 --faults
```

`--faults`, aynı Compose projesindeki API ve RabbitMQ'yu geçici olarak durdurur. Sonlandırma nedeniyle kapalı kalırlarsa `docker compose start api rabbitmq` ile açın. Bu test çalışan yerel servisleri etkiler. Yeni oturumlu istemcinin temizlik davranışı `verify_v1.py` için değiştirilmemiştir; toplu V1 betiği kendi test kayıtlarını ve anahtarlarını inceleme amacıyla bırakır.

### İlk kullanıcıdan önce

`verify_api.py` ve `verify_delays.py` bayraksız çalıştırıldığında eski bootstrap yöntemini kullanır. V1 kurulmuşsa `--session` kullanmanız gerektiğini açıklar. Bayraksız `verify_api.py` yalnızca ilk kurulum sırası içindir; bu koşu kayıtlı demo proje erişimini günceller.

## Elle alarm senaryosu

Kaynak örneği çalışırken yeni bir kimlikle `POST http://localhost:8081/orders` çağrısı yapın:

```json
{"transactionId":"DEMO-MANUAL-001"}
```

`pilot-source` akışında ERP tamamlanır ve fatura beklenir. Yaklaşık 30 saniye ve tarama aralığı sonrasında gecikme alarmı açılır. “Gördüm” alarmı kapatmaz.

Ardından `POST http://localhost:8081/orders/DEMO-MANUAL-001/invoice` adresine `{}` gönderin. İşlem tamamlanmalı, alarm kapanmalı, açılış/kapanış bildirimleri Mailpit ve webhook alıcısında görünmelidir.

## Korunan iş kuralları

- API'den 202 almak, olay ve outbox kaydının kalıcı kaydedildiği anlamına gelir. İşlenmiş olması ayrıca izlenir.
- Aynı projede aynı `eventId` ve aynı içerik tekrar gönderilebilir; farklı içerik 409 üretir.
- `transactionId` aynı işin adımlarında sabittir. Her yeni olay ayrı `eventId` kullanır.
- `occurredAt` saat dilimi içermelidir; adımın durumu teslim sırasına değil olay zamanına göre seçilir.
- `not_observed`, bildirimin görülmediğini belirtir. Dış sistemin işi yapmadığını kanıtlamaz.
- Gecikme `failed` anlamına gelmez; kaynak açıkça hata bildirmelidir.
- Akışlar 2–30 sıralı adımdır. İlk adımda süre yoktur; diğerleri 1 saniye–30 gündür.
- Yayınlanan akış sürümü değişmez. Değişiklik yeni sürüm taslağıyla yapılır ve yayınlanır; devam eden işlemler başladıkları sürümde kalır.
- İşlem detayındaki süre farkı iş olaylarının zamanları arasındadır. Negatif fark zaman tutarsızlığı olarak gösterilir; işlem durumunu panel değiştirmez.
- Sistem ekranındaki işlenmemiş olay sayısı, henüz kuyruğa yayımlanmamış olayları da içerir. İki sayaç toplanmaz.
- Panelden oluşturulan TEST- işlemleri gerçek kayıtlardır ve sayılara dahildir.

## Sistem sağlığı izleme

```bash
docker compose -f docker-compose.yml -f compose.monitoring.yml up --build -d
python3 scripts/verify_monitoring.py
```

Grafana http://127.0.0.1:3001 adresinde açılır; kullanıcı `admin`, parola `.env` içindeki `GRAFANA_ADMIN_PASSWORD`. Ölçümler, alarm kuralları ve kesinti senaryosu `docs/izleme.md` belgesinde anlatılır.

## Yedek ve geri yükleme

```bash
bash scripts/backup_local.sh
bash scripts/check_restore.sh backups/OLUSAN-KLASOR-ADI
```

İkinci komuttaki yolu ilk komutun oluşturduğu gerçek klasörle değiştirin. Geri yükleme kontrolü ayrı veritabanı oluşturur; çalışan veritabanını değiştirmez. Oluşturduğu kontrol veritabanını inceleme için bırakır.

Veritabanıyla birlikte Data Protection anahtarlarını koruyun. Tam uygulama kurtarma provası ayrı bir Compose projesinde yedekten dönüşü, girişi, yeniden kuyruklamayı ve bildirimleri sınar:

```bash
python3 scripts/rehearse_restore.py run
```

Ayrıntı `IntegraOps_V1_Paket_02_Kurtarma_Kodlu_Rehber.md` ve kabul kaydı `docs/v1-paket-02-kabul.md` içindedir.

## Canlı ortam

`compose.live.yml` yalnızca yayınlanmış imajları çalıştırır. `vX.Y.Z` etiketi itilince `.github/workflows/release.yml` api, worker ve panel imajlarını GHCR'a gönderir. Sunucuda yayın, geri dönüş, yedek ve geri yükleme `scripts/live/` betikleriyle yapılır. Kurulum ve işletim adımları `docs/canli-ortam.md` belgesindedir.

Bütün döngü sunucu olmadan yerelde sınanır:

```bash
python3 scripts/rehearse_live.py
```

## Sıradaki işler

Gerçek kaynak sistemin bağlanması için sözleşme `docs/entegrasyon-sozlesmesi.md` belgesindedir. Gerçek sunucuda canlı kabul ve kontrollü pilot `docs/yol-haritasi.md` içindeki sırayla yapılır. Sonraki kapsam: arama/tarih filtreleri, kullanıcı daveti/hesap kurtarma, test verisi ayrımı ve veri saklama. Python/Go için bağımsız servis kararı pilot ihtiyacına göre verilecektir.

## Belgeler ve geçmiş doğrulama

- `docs/verification.md`: İlk teslim ve 17 Eylül V1 doğrulama kayıtları. Tarihsel sonuçlardır.
- `docs/yol-haritasi.md`: Aşamalı geliştirme planı ve her aşamanın güncel durumu.
- `docs/v1-paket-01.md`: İlk düzeltme paketinin değişiklikleri ve doğrulama sınırları.
- `docs/v1-paket-02-kabul.md`: Kurtarma provası kabul kaydı.
- `docs/v1-1.md`: V1.1 günlük kullanım özellikleri ve doğrulaması.
- `docs/v1-2.md`: V1.2 akış sürümleme ve süre kuralları.
- `docs/entegrasyon-sozlesmesi.md`: Gerçek kaynak sistem için olay, kimlik ve kuyruk kuralları.
- `docs/izleme.md` ve `docs/canli-ortam.md`: Sistem sağlığı izleme ve canlı işletim rehberleri.
- `IntegraOps_V1_00_Kurulum_ve_Test.md`: V1 modüllerinin ayrıntılı kurulum açıklaması.
- `docs/architecture/0001-first-slice.md` ve `docs/sistem-mimarisi.md`: İlk backend aşamasının kararları; güncel özellik envanteri değildir.
- Tam kod rehberleri tarihsel kaynaklardır. Panel rehberindeki eski dosyaları güncel panelin üzerine topluca yazmayın.

17 Eylül kaydı: 51 birim testi, 28 bütünleşik kontrol, panel derlemesi, elle alarm/bildirim ve veritabanı geri yükleme başarılı. Aynı kayıt kesinti ve canlı ortam provasının yapılmadığını belirtir. Yeni değişiklikler için bu sonuçlar tekrar koşulmuş sayılmaz.
