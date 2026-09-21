# IntegraOps V1 — Kalan modülleri ekleme rehberi

Bu teslim, mevcut işlem takibi projesinin üzerine kullanıcı/proje yetkileri, alarm merkezi, e-posta ve webhook bildirimleri, güvenilir kaynak entegrasyonu ve işletim araçlarını ekler. Kodlar son paylaştığımız .NET 10 + PostgreSQL + RabbitMQ + Next.js yapısını temel alır. Bilgisayarındaki dosyaları doğrudan değiştirmedim; rehberdeki dosyaları projene sen ekleyeceksin.

## 1. Hangi rehber ne içeriyor?

| Sıra | Dosya | İçerik |
|---|---|---|
| 00 | Bu rehber | Kurulum sırası, mantık, testler, sınırlar |
| 01 | IntegraOps_V1_01_Backend_Tam_Kodlar.md | Backend dosyalarının tam içerikleri; yeni migration, yetki, alarm ve gönderim kodları |
| 02 | IntegraOps_V1_02_Panel_Tam_Kodlar.md | Panel sayfalarının, ortak bileşenlerin ve bağlantı katmanının tam içerikleri |
| 03 | IntegraOps_V1_03_Entegrasyon_ve_Ortam_Tam_Kodlar.md | .NET kaynak örneği, webhook alıcısı, Docker dosyaları, kurulum/test/yedekleme komutları, CI ve canlı ortam şablonu |

Kod rehberlerinde **YENİ** dosyaları oluştur; **DEĞİŞTİR** dosyaların içeriğini verilen tam içerikle değiştir. **REFERANS** dosyalar önceki sürümle aynıdır ve bütün yapıyı birlikte okuyabilmen için eklenmiştir; sende mevcutsa yeniden değiştirmen gerekmez. Kendi eklediğin özel değişiklikler varsa DEĞİŞTİR dosyalarını karşılaştırarak birleştir.

Başlıklardaki yollar `integraops/` proje köküne göredir. Örneğin `frontend/src/app/alarms/page.tsx`, frontend klasörünün altındaki dosyadır. Kod bloklarının dışındaki açıklamaları dosyaya kopyalama. `...` ile bırakılmış eksik metot veya yer tutucu iş mantığı yoktur.

## 2. Eklenen modüller

| Modül | Ne işe yarar? | Nerede görünür? |
|---|---|---|
| Kullanıcı oturumu | E-posta ve parola ile giriş, çıkış, parola değiştirme | Giriş / Hesabım |
| Proje ve ekip | Proje seçme, oluşturma, kullanıcı ekleme, rol değiştirme | Projelerim / Ayarlar |
| API anahtarları | Gönderim ve okuma için ayrı anahtar, anında iptal | Ayarlar |
| Alarm merkezi | Süre aşımı ve bildirilen hata için ayrı alarm kayıtları | Alarmlar |
| Alarm geçmişi | Açılma, görülme ve kapanma zamanları | Alarm detayı |
| Bildirimler | E-posta ve imzalı webhook; kalıcı kuyruk ve tekrar denemeler | Ayarlar / Alarm detayı |
| Sistem görünümü | Bekleyen işler, duran gönderimler, worker yaşam sinyali, yönetim geçmişi | Sistem |
| Kaynak entegrasyonu | İş kaydıyla bildirimi aynı veritabanı işleminde saklama | `samples/OrderSource` |
| İşletim araçları | Yerel test, kesinti denemesi, yedek ve ayrı veritabanına geri yükleme | `scripts/` |

Mevcut işlem listesi, filtreler, sayfalama, işlem detayı, akış oluşturma, süre alanları, akış detayı/kopyalama ve panel testleri korunur.

## 3. İşleyişi basitçe

**Kaynak sistem işini yapar.** Örneğin ERP kaydı oluşturur. Aynı veritabanı işlemi içinde “bu kayıt oluşturuldu” bildirimini kendi gönderim tablosuna ekler. Birisi kaydedilip diğeri kaybolmaz.

**Ayrı gönderici bildirimi iletir.** `POST /api/v1/events` isteğinde `X-API-Key` kullanır. `transactionId` aynı siparişin bütün adımlarında aynıdır. Her yeni olayın `eventId` değeri ayrıdır. Aynı olay tekrar gönderiliyorsa kimliği, zamanı ve içeriği korunur.

**IntegraOps kaydı işler ve beklenen adımı hesaplar.** ERP tamamlandıysa fatura bildirimi beklenir. Süre aşılırsa gecikme oluşur. Süre aşımı, faturanın başarısız olduğunu iddia etmez. `failed` ancak kaynak sistem hata bildirdiğinde oluşur.

**Alarm koşulu oluştuğunda alarm açılır.** Tekrarlanan worker kontrolleri aynı açık alarmı çoğaltmaz. “Gördüm” işareti sadece bir kullanıcının alarmı gördüğünü kaydeder. Eksik adımın tamamlandığı bildirilince alarm kapanır. Sonradan yeni bir sorun oluşursa yeni bir alarm kaydı açılır.

**Bildirim gönderimi ayrı kuyruktadır.** E-posta veya webhook sorunu, gelen olayın kaydedilmesini engellemez. Kalıcı gönderim kaydı sayesinde worker yeniden başlayınca işlem devam eder.

`Program.cs`, servis ayarlarının ve HTTP uçlarının bağlandığı başlangıç dosyasıdır. Alarm hesabı `AlarmRules`, alarm kaydı `PostgresAlarmStore`, gönderim işi `NotificationDispatcher`, dış bağlantı `NotificationTransport` içindedir. Böylece her dosyanın ayrı görevi vardır.

## 4. Süre formu nerede?

**Akışlar → Yeni akış → Adımlar** bölümünde adım bazında dakika/saat girilir. İlk adımın önceki adıma bağlı süresi yoktur. Diğer adımların API karşılığı `maxDelaySeconds` alanıdır; mevcut sınır 1–86400 saniyedir.

**Akışlar → Akış adına tıkla** ekranında kaydedilmiş adımlar ve süreler görünür. Süreyi değiştirmek için “Kopyadan yeni akış oluştur” kullanılır. Yeni işlemleri gönderen sistem yeni akış kodunu kullanmalıdır. Devam eden eski işlemlerin kuralları böylece kendiliğinden değişmez.

Demo akışındaki **30 saniye** ve otomatik testteki **2 saniye** sadece deneme değerleridir. Bütün faturalar için tek bir süre dayatılmaz. Bu sürümde geçmişten otomatik süre öğrenme veya yapay zekâ analizi yoktur. Henüz hiçbir başlangıç bildirimi gelmemiş bir işlemin varlığını sistem kendiliğinden bilemez.

## 5. Dosyaları eklemeden önce

Mevcut dosyalarının Git kaydını veya ayrı bir klasör kopyasını al. Mevcut veritabanına yeni tablolar eklenir; tablo silen bir migration yoktur. Yine de eski verilerle çalıştığın için başlangıç yedeği al:

```bash
mkdir -p backups/pre-v1
chmod 700 backups/pre-v1
docker compose exec -T postgres pg_dump -U integraops -d integraops -Fc > backups/pre-v1/integraops.dump
chmod 600 backups/pre-v1/integraops.dump
```

Bu komutlar mevcut proje kökünde çalıştırılır. `.env` ve `frontend/.env.local` dosyalarını paylaşma veya örnek dosyalarla değiştirme. Mevcut PostgreSQL parolasını rastgele değiştirmek, saklanan verilerle bağlantıyı keser.

## 6. Kurulum sırası

### A. Kod dosyalarını ekle

01, 02 ve 03 numaralı rehberlerin dosya listelerini sırayla uygula. Rehberlerin tümünü bitirdikten sonra aşağıdaki komutlara geç.

Yeni veritabanı değişikliği **003_pilot.sql** dosyasıdır. Önceki `001_initial.sql` ve `002_transaction_delays.sql` içeriklerini mevcut veritabanında farklı bir şemaya dönüştürmeye çalışma. Migration geçmişi bunları tekrar çalıştırmaz.

### B. Yerel ayarları tamamla ve backend’i kur

Proje kökünde:

```bash
python3 scripts/prepare_v1.py
docker compose stop api worker
docker compose up --build -d
docker compose ps
python3 scripts/setup_v1.py
```

`prepare_v1.py`, mevcut parolaları koruyarak yerel webhook ve örnek kaynak veritabanı için eksik ayarları ekler. Bu projeyi zaten kurduğun için `init_local.py` dosyasını tekrar çalıştırman gerekmez.

`setup_v1.py` e-posta, ad ve parola sorar. İlk kullanıcı, mevcut kurulum anahtarı ve proje API anahtarıyla proje sahibi yapılır. Proje anahtarı önce `frontend/.env.local`, sonra varsa `.local/demo-project.json` içinden alınır; bulunamazsa terminalde sorulur. Parolayı veya anahtarı sohbete yazman gerekmez.

Kurulum; `pilot-source` akışını, iki yerel bildirim kanalını ve örnek kaynak uygulamasının gönderim anahtarını hazırlar. Anahtarı ekrana yazmadan `.env` içine kaydeder. Tekrar çalıştırıldığında aynı proje için mevcut uygun kaynak anahtarını kullanır.

Birden fazla sahip olduğun proje varsa kurulumun hangi projeye yapılacağını seçersin. Seçim `PILOT_PROJECT_ID` olarak saklanır. İlk kullanıcı kurulduktan sonra eski bootstrap/proje oluşturma yolu kapanır; sonraki projeler panelden açılır.

### C. Paneli derle ve başlat

```bash
cd frontend
npm ci
npm run build
npm run start
```

Mevcut `package-lock.json` dosyanı koru. Rehberdeki `package.json` ile uyuşmuyorsa önce `npm install` çalıştırıp kilit dosyasını güncelle, ardından tekrar derle. Yeni panel bağlantısı artık kullanıcı oturumunu kullanır; eski `INTEGRAOPS_API_KEY` değeri tarayıcıya aktarılmaz ve panel API çağrılarında kullanılmaz.

Tarayıcıda `http://localhost:3000` adresini aç, oluşturduğun hesapla giriş yap ve projeni seç. Yerel kullanımda `PANEL_COOKIE_SECURE=false`; HTTPS kullanılan canlı ortamda `true` olmalıdır.

### D. Kaynak sistem örneğini başlat

Yeni terminalde proje kökünde:

```bash
docker compose --profile demo up --build -d source
```

| Yerel adres | İşlev |
|---|---|
| http://localhost:3000 | Panel |
| http://localhost:8080/health/ready | API ve migration hazırlığı |
| http://localhost:8025 | Yerel e-posta kutusu |
| http://localhost:8082/receipts | İmzası doğrulanmış webhook kayıtları |
| http://localhost:8081/outbox | Örnek kaynak uygulamasının gönderim durumu |

Mailpit gerçek kişilere e-posta göndermez; gönderimleri yerelde toplar. Webhook alıcısı da bu bilgisayardaki test servisidir. Kaynak örneğinin HTTP uçları yalnızca yerel demo içindir; canlı ortam şablonunda yayınlanmazlar.

## 7. Ekranda elle bir senaryo dene

Postman ile `POST http://localhost:8081/orders` isteği gönder:

```json
{"transactionId":"DEMO-MANUAL-001"}
```

Panelde kurulum yaptığın projeyi ve “Kaynak sistem denemesi” akışını seç. ERP adımı işlenince fatura beklenir. Yaklaşık 30 saniye ve worker kontrol aralığı sonrasında Alarmlar ekranında süre aşımını görürsün. “Gördüm” düğmesine basınca alarm açık kalır.

Sonra `POST http://localhost:8081/orders/DEMO-MANUAL-001/invoice` isteği gönder. Gövde olarak `{}` yeterlidir. İşlem tamamlanır, alarm kapanır, e-posta kutusunda ve webhook kayıtlarında açılış/kapanış bildirimleri görünür.

Hata senaryosu için başka bir sipariş oluşturup `POST /orders/YENI-KIMLIK/invoice-failed` çağır. Aynı sipariş için daha sonra `/invoice` çağırınca hata koşulu kalkar. Her yeni denemede yeni bir sipariş kimliği kullan.

## 8. Toplu test

API, worker, panel, Mailpit, webhook ve kaynak örneği çalışırken proje kökünde:

```bash
dotnet test IntegraOps.slnx
python3 scripts/verify_v1.py --load 100
```

Bu betik yeni bir **V1 toplu test** projesi açar. Test kullanıcıları ve anahtarları bu projede oluşturulur; mevcut proje üyelerini veya anahtarlarını iptal etmez. Yerel test kanalları kullanılır. Sonuçları sonradan panelde inceleyebilmen için kayıtlar saklanır.

Kontroller: roller, son sahibi koruma, proje izolasyonu, anahtar yetkisi/iptali, aynı olayın tekrar gönderimi, gecikmenin hatadan ayrılması, alarm açılması/görülmesi/kapanması, imzalı webhook ve tekrar deneme, hata düzeltme, ters olay sırası, sayfalama, toplu işlem ve örnek kaynak sistemidir.

Kesintileri de denemek için:

```bash
python3 scripts/verify_v1.py --load 100 --faults
```

`--faults`, bu yerel Compose projesindeki API ve RabbitMQ servislerini sırayla kısa süre durdurup yeniden başlatır. API kapalıyken kaynak siparişinin kaydedildiğini, API döndüğünde bildirimin ulaştığını ve RabbitMQ kapalıyken API’nin olayı veritabanında sakladığını kontrol eder. Bu seçeneği başka çalışma yapmadığın yerel denemede kullan. İşletim sistemi veya terminal zorla kapatılırsa servisleri `docker compose start api rabbitmq` ile açabilirsin.

Beklenen çıktı satırları `GEÇTİ · ...` şeklindedir. Test bir noktada durursa hata adını ve aşağıdaki logları incele:

```bash
docker compose logs --tail=100 api worker migrate
docker compose --profile demo logs --tail=100 source
```

## 9. Yetki tablosu

| İşlem | Sahip | Operatör | İzleyici |
|---|---|---|---|
| Akış, işlem ve alarm okuma | Evet | Evet | Evet |
| Akış oluşturma ve olay gönderme | Evet | Evet | Hayır |
| Alarmı gördüm işareti ve duran bildirimi yeniden deneme | Evet | Evet | Hayır |
| Kullanıcı, anahtar ve bildirim kanalı yönetimi | Evet | Hayır | Hayır |
| Sistem/yönetim geçmişi | Evet | Hayır | Hayır |

Her kullanıcı kendi hesabının parolasını değiştirebilir ve kendisine ait yeni bir proje açabilir. Bir projeye erişim ayrıca o projenin üyeliğiyle kontrol edilir. Proje kimliğini istekte değiştirmek başka bir projeye erişim sağlamaz.

Yeni `ingest` anahtarı yalnızca olay gönderir; `read` anahtarı takip verilerini okur. Önceki `legacy` anahtarı eski Postman denemelerinin bozulmaması için korunur, fakat kullanıcı/anahtar/kanal yönetimine erişemez. Kaynak sistemleri yeni anahtara geçirdikten sonra eski anahtarı Ayarlar’dan iptal edebilirsin.

Parolalar ASP.NET Core `PasswordHasher` ile hashlenir. Oturum ve API anahtarlarının ham değerleri veritabanında tutulmaz. Kullanıcı oturumu sekiz saattir; parola değişikliği tüm kullanıcı oturumlarını kapatır. Giriş denemeleri sınırlandırılır. [PasswordHasher belgesi](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1?view=aspnetcore-10.0)

## 10. Bildirim kuralları

- Etkin kanallar yalnızca alarm açılma ve kapanma bildirimlerini alır. “Gördüm” ek bir dış bildirim üretmez.
- Kanal eklendiğinde geçmiş alarmlar tekrar gönderilmez. Migration mevcut işlemlerin o anki koşullarını tarar; geçmişte yaşanıp bitmiş alarmları geriye dönük uydurmaz.
- Kanal hedefi mevcut kuyruk kayıtlarının yönünün değişmemesi için sonradan düzenlenmez. Hedef değişecekse eski kanalı kapatıp başka adla yeni kanal oluştur.
- Ağ hataları ve geçici HTTP hatalarında artan aralıklarla en çok sekiz otomatik deneme yapılır. Kalıcı HTTP retlerinde denemeler erken durur. Alarm detayındaki “Tekrar dene”, duran gönderime yeni deneme hakkı verir; bildirim kimliği korunur.
- Teslimat en az bir kez gerçekleşebilir. Hedef yanıtı kaybolursa e-posta tekrar gelebilir. Webhook örneği `X-IntegraOps-Delivery` kimliğini saklayarak aynı bildirimin iş etkisini çoğaltmaz.
- Webhook imzası `HMAC-SHA256(secret, timestamp + "." + deliveryId + "." + body)` biçimindedir. Alıcı ham gövde üzerinden doğrular ve beş dakikalık zaman sınırı uygular.
- Webhook adresleri sunucudaki izin verilen HTTPS origin listesine uymalıdır. Yönlendirmeler izlenmez; DNS sonucu kontrol edilir. Yerel HTTP/private network istisnası yalnızca açıkça yapılandırılmış test adresidir.

Kanal hedefleri ve ortak sırlar Data Protection ile saklanır. API ve worker aynı anahtar dizinini paylaşır. Bu nedenle **veritabanı yedeğiyle beraber `protection_keys` içeriğini de korumalısın**. Anahtar dosyaları bu şablonda ayrıca şifrelenmez; canlı sunucuda disk/backup erişimi ve şifrelemesi ayrıca sağlanmalıdır. [Data Protection yapılandırması](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)

## 11. Yedekleme ve geri yükleme kontrolü

V1 kurulduktan sonra:

```bash
bash scripts/backup_local.sh
```

Oluşan gerçek klasör adını kullanarak:

```bash
bash scripts/check_restore.sh backups/OLUSAN-KLASOR-ADI
```

Kontrol betiği çalışan veritabanını değiştirmez. Ayrı ve benzersiz bir veritabanına yedeği yükler, kayıt sayılarını gösterir ve anahtar arşivinin okunabildiğini kontrol eder. İnceleyebilmen için kontrol veritabanını bırakır. Tam uygulama geri dönüşü ve yedek anahtarlarıyla korunan alanların açılması, canlıya geçiş provasında ayrıca doğrulanmalıdır. RabbitMQ kuyruğunun anlık kopyası bu yedeğe dahil değildir.

Veritabanı yedeğinden dönüldüğünde `processed_at IS NULL` olaylar için yayımlanmış outbox satırlarının da yeniden gönderilebilmesi gerekir; 03 numaralı rehberdeki `scripts/recover_pending_events.sql` dosyası bu amaçla verilmiştir. Worker durmuşken uygula. Daha önce işlenmiş olaylar değişmez; yeniden teslim edilen olayları mevcut idempotent tüketici ayıklar.

Yalnızca gerçekten yedekten döndüğünde, geri yüklenen çalışan veritabanı için:

```bash
docker compose stop worker
docker compose exec -T postgres psql -U integraops -d integraops -v ON_ERROR_STOP=1 < scripts/recover_pending_events.sql
docker compose start worker
```

## 12. Bu teslimde ne doğrulandı?

| Kontrol | Sonuç |
|---|---|
| API ve worker derlemesi | Geçti, hata/uyarı yok |
| .NET kaynak uygulaması derlemesi | Geçti |
| Panelin üretim derlemesi / TypeScript | Geçti |
| Birim testleri | 51 test geçti |
| API/panel/entegrasyon çalışma kontrolleri | 110 kontrol geçti |
| Migration’ın iki kez uygulanması | İkinci çalıştırma değişiklik oluşturmuyor |
| Yerel e-posta, imzalı webhook ve başarısız gönderimin tekrarı | Çalıştırılarak doğrulandı |
| Kaynak uygulama hedef API kapalıyken kayıt ve sonradan gönderim | Çalıştırılarak doğrulandı |
| Docker/RabbitMQ ile `verify_v1.py`, gerçek pg_dump/pg_restore | Komutları hazır; bu çalışma ortamında Docker olmadığı için burada çalıştırılmadı |
| Canlı DNS, TLS, gerçek SMTP/ERP ve canlı yük testi | Yapılmadı |

SQL/HTTP kontrollerinde geçici PGlite veritabanı ve gerçek API/panel süreçleri kullanıldı. Olay işleme test yardımcısı üzerinden çalıştırıldı; RabbitMQ ağ yolunun aynı kontrol içinde doğrulandığı iddia edilmiyor. PGlite’ın bağlantı modeli gerçek PostgreSQL sunucusundan farklıdır. Bu yüzden kendi Docker ortamındaki toplu test ayrıca gereklidir. [PGlite Socket belgesi](https://pglite.dev/docs/pglite-socket)

## 13. Canlı ortama geçiş için hazır bırakılanlar

03 numaralı rehberde `compose.live.yml`, frontend Dockerfile, Caddy HTTPS yapılandırması ve `.env.live.example` vardır. Canlı şablon ayrı Compose proje adını ve ayrı veri birimlerini kullanır. Veritabanı ve RabbitMQ portlarını dışarı açmaz; yerel Mailpit, webhook alıcısı ve demo sipariş uygulamasını yayınlamaz.

Yerel testler geçtikten sonra gerçek sunucuda alan adlarını, yeni sırları, SMTP hesabını ve gerekiyorsa webhook izin listesini hazırlayacağız. `.env.live.example` içindeki örnek değerler çalışır canlı kimlik bilgileri değildir. Şimdilik canlı komutlarını yerel `.env` ile çalıştırma.

İleride sunucuda kullanılacak komut biçimi:

```bash
docker compose --env-file .env.live -f compose.live.yml up --build -d
```

Boş bir canlı veritabanında önce korumalı bootstrap/proje ve ilk sahip kurulumu yapılır; yerelden veri taşınacaksa veritabanıyla Data Protection anahtarları birlikte geri yüklenir. Alan adı/sertifika, gerçek SMTP, kullanıcı girişi, kaynak ERP bildirimi, yedekten dönüş ve kesinti denemeleri o ortamda tamamlanır. Bu dosyaların hazırlanmış olması canlı kurulumun test edildiği anlamına gelmez.

## 14. V1’in kapsam sınırı

Bu teslim, **olay bazlı işlem takibi ve alarm yönetimi V1** kapsamını tamamlayan kodları içerir. Gerçek ERP’ye erişim bilgisi veya o ERP’nin kodu verilmediği için firmaya özel bağlantı uygulanmış değildir; bunun yerine çalışan .NET kaynak örneği ve aynı işleme bağlı gönderim mantığı vardır.

Tam log arama motoru, sunucu metrik ajanı, gelişmiş SLA/SLO raporu, yapay zekâ ile kök neden analizi, faturalandırma/abonelik, kurumsal SSO/MFA ve Kubernetes dağıtımı bu sürümde yoktur. Bunları “tamamlandı” saymıyoruz. V1 kabulü için önce buradaki yerel testleri, ardından gerçek ortam provası ve müşteri senaryosunu geçmeliyiz.
