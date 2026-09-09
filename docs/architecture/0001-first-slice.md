**Karar 0001 — İlk olay kabul ve işleme akışı**

Tarih: 7 Eylül 2026. Durum: ilk uygulama tercihi. IntegraOps çalışma adı; marka ve alan adı ilk çalışan ürün sonrasına bırakıldı.

**İlk hedef**, sıralı bir akışta aynı işleme ait durum bildirimlerini güvenilir biçimde kabul etmek ve sorgulanabilir hale getirmektir. Kapsam, önceki sohbetin ilk backend teslimine karşılık gelir. Gecikme tespiti ikinci teslimdir.

| Karar | Gerekçe ve sınırı |
| --- | --- |
| .NET API ve ayrı .NET worker | Olay kabulü ve arka plan işlemeyi ayrı çalıştırabilmek |
| PostgreSQL ve açık SQL/Npgsql | İşlem sınırlarını, benzersizlikleri ve indeksleri görünür tutmak; ORM seçimini bu küçük dilimden bağımsız bırakmak |
| PostgreSQL outbox | Kabul edilen olayla gönderim kaydını tek transaction'da tutmak |
| RabbitMQ | Olay işlemesini API yanıtından ayırmak; yayınlama ve tüketici onaylarını öğrenmek |
| Beş mantıksal .NET projesi | Alan kurallarını HTTP ve veritabanı ayrıntılarından ayırmak; iki çalıştırılabilir servisle başlamak |
| Immutable flow | İlk teslimde akış değiştirmeyi desteklemeyerek devam eden işlemin kurallarını sabit tutmak |
| Proje anahtarıyla kapsam | Firma/proje kimliğini dışarıdan gelen olay gövdesine güvenerek seçmemek |

**Veri kabulünün sırası:** API projeyi anahtardan bulur, alanları ve akış adımını doğrular, işlem kaydı ile olay ve outbox kaydını PostgreSQL'de aynı transaction içinde oluşturur. Commit tamamlanmadan 202 dönmez. Bundan sonra yayımlayıcı outbox kaydını alır ve RabbitMQ onayını bekler. İşleyici kaydı uygulayıp PostgreSQL commit'ini bitirdikten sonra mesajı onaylar.

Outbox'a ilk günden ihtiyaç duyulmasının nedeni API'nin hem veritabanına hem de kuyruğa ayrı ayrı yazmasının arada bir kesinti penceresi oluşturmasıdır. Burada tek veritabanı işlemi kullanılır. Yayımlayıcı, satırı FOR UPDATE SKIP LOCKED ile alır. Gönderim başına en fazla on saniyelik süre uygulanır. İlk sürümde bir kerede tek outbox kaydı gönderilir; bu bir yüksek kapasite optimizasyonu değildir.

RabbitMQ yayını başarılı olduktan sonra outbox commit'i tamamlanmadan süreç kapanırsa aynı olay yeniden yayımlanabilir. Bu beklenen bir durumdur. İşleyici, proje ve eventId ile tekilleştirilen olayın processed_at değerini kontrol eder. İşlem kaydı kilidi, aynı işleme ait farklı olayların eşzamanlı projeksiyonunu sıraya koyar. Bu yaklaşım en az bir kez teslimi tolere eder; bütün ağ boyunca bir kez teslim garantisi iddia edilmez. İstemci onay davranışları [resmî RabbitMQ rehberine](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) göre seçildi.

**Kimlik kapsamı:** Olayın benzersizliği project_id + event_id; iş kaydının benzersizliği project_id + flow_id + business_id üzerindedir. Bir müşterinin ORDER-123 kaydı başka müşterinin aynı kimlikli kaydına bağlanmaz. Bağlantılar veritabanında birleşik yabancı anahtarlarla da korunur. Anahtarların yalnızca SHA-256 özeti saklanır; üretilen uzun rastgele anahtar ilk kurulum yanıtında bir kez gösterilir.

**Zaman kuralları:** occurredAt gerçek iş olayının üreticideki zamanı, receivedAt bizim kabul zamanımızdır. Saat dilimi zorunlu tutulur ve UTC'ye dönüştürülür. Olayların gelişi farklı sırada olsa da adımın son durumu occurredAt üzerinden seçilir. Tam eşit zamanlarda eventId kararlı bir eşitlik bozucudur; aynı iş anındaki anlamlı sıralamayı üretici belirlemek istiyorsa ileride sıra numarası eklenmelidir. PostgreSQL zaman damgalarını mikrosaniye hassasiyetinde tuttuğu için bundan daha ince sıralama garantisi verilmez.

**Gözlemin sınırı:** not_observed, bildirimin henüz işlenmediğini ifade eder. Uzak sistemin işi yapmadığını kanıtlamaz. Başlangıcı hiç gözlenmemiş bir işlemin varlığı da bu sürümde bilinemez. Gerçek kaynak-hedef kayıt mutabakatı farklı bir teslim olacaktır.

**Yerel kurulum ve şema:** Migration ayrı bir komut/Compose servisi ile çalışır. schema_migrations kaydı ve PostgreSQL advisory lock aynı migration'ın eşzamanlı veya tekrarlı uygulanmasını önler. Uygulama açılışında her API örneği şema değiştirmez. API'nin hazır olma kontrolü veritabanını kapsar; kuyruk kesildiğinde olay kabulü sürdüğü için RabbitMQ bu kontrole dahil edilmemiştir. Worker ve kuyruk sağlığını ayrıca gözlemlemek gerekir.

**Henüz çözülmeyen işletim konuları:** işlem/olay arşivleme, çok yüksek olay sayılı tek işlem, ayrıntılı hata sınıflandırması, kalıcı hatanın tekrar deneme sınırı, kuyruk kapasite politikası, kapsamlı dış alarm ve worker ölçümleri. İlk teslim için varsayılan veritabanı ve kuyruk ayarları tek makine geliştirme ortamıdır. Redis, Kafka, Kubernetes, Go ve AI eklenmemiştir.

**Sıradaki öğrenme ve geliştirme işi:** Akışın son gözlenen sıralı adımından sonraki hedefi bulmak, süre sınırını hesaplamak, gecikmiş işlemleri indeksli bir sorgu/işleyiciyle tespit etmek ve geç bildirim geldiğinde mevcut durumu düzeltirken gecikme geçmişini korumak. Başlangıç ve iptal senaryolarının anlamı bu teslimden önce netleştirilecektir.
