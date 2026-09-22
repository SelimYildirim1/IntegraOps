# IntegraOps — Yol haritası ve ilerleme kaydı

Bu belge projenin V1 kapanışından kurumsal ürüne kadar olan geliştirme planını ve her aşamanın güncel durumunu tutar. Aşamalar sırayla uygulanır. Bir aşama ancak kendi bitiş ölçütü karşılandığında tamamlanmış sayılır.

**Son güncelleme:** 22 Eylül 2026 (tekrar kullanılabilir entegrasyonlar tamamlandı)

## Durum işaretleri

| İşaret | Anlamı |
|---|---|
| ✅ | Tamamlandı ve doğrulandı |
| 🔄 | Üzerinde çalışılıyor |
| 🟡 | Kod hazır, dış girdi veya kullanıcı kabulü bekliyor |
| ⏳ | Sırası gelmedi |
| ⛔ | Engellendi; nedeni yanında yazılı |

## Şu an neredeyiz

**Aktif aşama:** Kurumsal kullanım ve ticari ürün (Aşama 13). Aşama 2, 3 ve 5 dış girdi bekliyor; V1.1, V1.2, V2 ve entegrasyon paketi tamamlandı.

Aşama 1 tamamlandı. Aşama 3 ve 4'ün yerelde sınanabilen bütün kısımları tamamlandı; kalan görevler gerçek sunucu, alan adı ve SMTP bilgisine bağlı. Bu girdiler gelene kadar Aşama 2, 3 ve 5 için kullanıcı kararı gerekiyor:

1. **Kaynak sistem:** Hangi gerçek ERP veya uygulama bağlanacak? Teknolojisi ve sipariş/fatura kod noktaları. Sözleşme: `docs/entegrasyon-sozlesmesi.md`.
2. **Sunucu:** Canlı sunucu, alan adları, SMTP hesabı ve sunucu dışı yedek deposu. Kurulum: `docs/canli-ortam.md`.
3. **Pilot ekibi:** Alarmların gideceği kişiler ve pilot dönemi.

**En yakın çalışma sırası:** Kurtarma kabulü → ilk gerçek entegrasyon → canlı ortam ve temel izleme → kontrollü pilot → **V1 kapanışı**. Ardından günlük kullanım özellikleri (V1.1), akış sürümleme (V1.2), Go aktarım ajanı ve Python mutabakat servisi gelir.

## Başlangıç durumu

| Konu | Durum | Not |
|---|---|---|
| Çekirdek sistem (olay kabulü, outbox, RabbitMQ, worker, panel, kimlik, alarm, bildirim) | ✅ | 17 Eylül 2026 yerel kabul kaydı |
| İlk düzeltme paketi (Paket 01) | ✅ | Yardımcı testler, typecheck/build, oturumlu API ve gecikme testleri geçti |
| API ve RabbitMQ kesinti provası | ✅ | `verify_v1.py --load 100 --faults` içindeki 33 kontrol geçti |
| Uygulama kurtarma paketi (Paket 02) | ✅ | 21 otomatik kontrol ve tarayıcı kabulü geçti |
| Sistem sağlığı izleme (Aşama 4) | ✅ | 15 izleme kontrolü ve kesinti senaryosu geçti |
| Canlı işletim betikleri ve yerel prova (Aşama 3) | 🟡 | 26 kontrol yerelde geçti; gerçek sunucu kabulü bekliyor |
| Gerçek kaynak entegrasyonu ve canlı pilot | 🟡 | Sözleşme hazır; kaynak sistem seçimi bekleniyor |

---

## 1. V1 kapanışı — Uygulama kurtarmasının tamamlanması ✅

**Amaç:** Veritabanı ve şifreleme anahtarları ayrı bir ortama döndüğünde API, worker, bildirim ve panelin birlikte çalıştığını kanıtlamak.

**Teknoloji:** Mevcut .NET, PostgreSQL, RabbitMQ ve Docker Compose. Python bu aşamada test ve işletim betiği olarak kullanılır.

**Paket içeriği:** `compose.restore.yml`, `scripts/rehearse_restore.py`, `scripts/restore_requeue.sql`, `scripts/tests/test_rehearse_restore.py`. Ayrıntılı kullanım `IntegraOps_V1_Paket_02_Kurtarma_Kodlu_Rehber.md` içindedir.

| Görev | Durum | Kanıt |
|---|---|---|
| Yardımcı testler (23 test) | ✅ | `python3 -m unittest discover -s scripts/tests` |
| Veritabanının ayrı ortama geri yüklenmesi | ✅ | Prova `e04c837883fa`, otomatik kontrol |
| Şifreleme anahtarlarının geri yüklenmesi ve SHA-256 doğrulaması | ✅ | Prova `e04c837883fa` |
| Mevcut kullanıcıyla API üzerinden giriş | ✅ | Prova `e04c837883fa` |
| Eski işlem, akış ve alarm geçmişinin okunması | ✅ | Prova `e04c837883fa` |
| İşlenmemiş olayların yeni RabbitMQ'ya yeniden kuyruklanması | ✅ | Prova `e04c837883fa` |
| Yeni hata/gecikme alarmlarının açılıp kapanması, webhook ve e-posta teslimi | ✅ | Prova `e04c837883fa` |
| Panelin geri yüklenen API ile çalışması (tarayıcı kabulü) | ✅ | `docs/v1-paket-02-kabul.md` |
| Prova sonrası asıl sistemin çalışmaya devam ettiğinin kontrolü | ✅ | `docs/v1-paket-02-kabul.md` |
| Prova ortamının temizlenmesi | ✅ | Konteyner ve diskler kaldırıldı; yedek ve rapor korundu |

**Bitiş ölçütü:** Kurtarma paketindeki otomatik kontroller ve tarayıcı kabulü geçer.

**Kapsam dışı:** Başka sunucuya dönüş ve canlı yedekleme düzeni Aşama 3'te ele alınır.

---

## 2. V1 kapanışı — İlk gerçek kaynak sisteminin bağlanması 🟡

Şu anda çalışan bir örnek kaynak uygulama var (`samples/OrderSource`). Sırada gerçek sistemden olay almak bulunuyor.

İlk entegrasyon sınırları belirli tek bir süreçle yapılır: **ERP sipariş kaydı → fatura oluşturulması**. İlk kabulden sonra ödeme ve kargo adımları eklenebilir.

| Görev | Durum |
|---|---|
| Bağlanacak gerçek kaynak sistemin ve teknolojisinin seçilmesi | ⛔ Kullanıcı kararı bekleniyor |
| Sistemler arasında ortak işlem kimliği sözleşmesi | ✅ Sözleşme bölüm 2 |
| Olay kimliği, adım kodu ve iş adımı eşlemesinin belirlenmesi | ✅ Sözleşme bölüm 1, 3 |
| Kaynakta kalıcı gönderim kuyruğu (outbox) | ✅ Referans uygulama `samples/OrderSource`; gerçek kaynakta uygulanacak |
| Bağlantı kesilince bekletme, bağlantı gelince gönderme | ✅ Referans uygulamada sınandı (`verify_v1.py --faults`) |
| Tekrar gönderimde aynı olay kimliğinin korunması | ✅ API tarafı ve referans uygulama |
| Başarı, açık hata ve hatanın düzelmesi bildirimleri | ✅ Sözleşme bölüm 4; alarm açılış/kapanış sınandı |
| Gerçek iş sürelerine göre gecikme eşikleri | ⏳ Kaynak sistemin süre dağılımı gerekli |
| Entegrasyon sözleşmesinin belgelenmesi | ✅ `docs/entegrasyon-sozlesmesi.md` |
| Gerçek kaynakta entegrasyon kodu ve kabul listesi | ⏳ Kaynak seçilince |

**Teknoloji:** IntegraOps tarafında mevcut .NET API. Kaynaktaki entegrasyon, bağlanacak uygulamanın kendi teknolojisiyle yazılır.

**Temel şart:** Kaynak uygulamada iş kaydı ile gönderilecek olay **aynı veritabanı işlemi içinde** kaydedilir. Sonradan eklenen bir gönderim ajanı bu ilk kayıt güvencesinin yerini tutamaz.

**Bitiş ölçütü:** Gerçek bir sipariş veya fatura doğru kimlikle panelde izlenir. Gecikme ve hata doğru kişiye bildirilir.

---

## 3. V1 kapanışı — Canlı ortam ve işletim altyapısı 🟡

Mevcut canlı Compose (`compose.live.yml`) ve Caddy (`deploy/Caddyfile`) şablonları gerçek dağıtımla tamamlanır.

| Görev | Durum |
|---|---|
| Sunucu, alan adı ve HTTPS kurulumu | 🟡 Caddy HTTPS yerel provada geçti; gerçek sunucu ve alan adı bekleniyor |
| Gerçek SMTP ve webhook hedeflerinin test hedeflerinden ayrılması | 🟡 `.env.live` ayrımı hazır; gerçek SMTP hesabı bekleniyor |
| Canlıya özel erişim bilgileri ve anahtarların yönetimi | ✅ `.env.live.example`, `render_alertmanager.py`, rehber |
| Zamanlanmış yedekleme | ✅ `scripts/live/backup.sh`, systemd zamanlayıcısı |
| Yedeklerin uygulama sunucusundan ayrı yerde tutulması | ✅ rclone ile S3 uyumlu depo; provada doğrulandı |
| Canlı yedekten başka ortamda geri dönüş provası | ✅ `rehearse_live.py`: diskler silindi, uzak yedekten 43 sn'de dönüldü |
| Sürüm yayınlama ve önceki sürüme dönüş prosedürü | ✅ `deploy.sh`, `rollback.sh`, `docs/canli-ortam.md` |
| Kaynak kodu, imajlar ve yayınlanan sürümün eşleştirilmesi | ✅ `release.yml`, imaj etiketleri, `/health/version` |
| Gerçek sunucuda kurulum ve kabul | ⛔ Sunucu, alan adı ve SMTP bilgisi bekleniyor |

**Teknoloji:**

- **Docker Compose:** İlk canlı servis dağıtımı.
- **Caddy:** HTTPS ve gelen trafiğin yönlendirilmesi.
- **GitHub Actions:** Mevcut test hattının imaj oluşturma ve yayın hazırlığıyla genişletilmesi.
- **İmaj deposu:** Yayınlanan API, worker ve panel sürümlerinin saklanması.
- **Ayrı yedek deposu:** Sunucu kaybında da erişilebilen yedekler.

**Bitiş ölçütü:** Aynı sürüm yeniden kurulabilir, güncellenebilir ve sorun halinde geri alınabilir.

---

## 4. V1 kapanışı — Sistemin kendi sağlığının izlenmesi ✅

İş süreçlerindeki sorunları izleyen ürün, kendi arızalarını da görebilmelidir.

| Ölçüm | Durum |
|---|---|
| Olay kabulünden işlenmesine kadar geçen süre | ✅ histogram, p50/p95/p99 panoda |
| Outbox içinde bekleyen en eski olayın yaşı | ✅ |
| Kuyruk büyüklüğü ve tüketim hızı | ✅ RabbitMQ Prometheus eklentisi |
| Gecikme tarayıcısının son başarılı çalışması | ✅ |
| Bildirim teslim başarısı ve tekrar deneme sayıları | ✅ |
| API hata oranı ve yanıt süreleri | ✅ |
| PostgreSQL bağlantıları, disk kullanımı ve veri büyümesi | ✅ |
| Son başarılı yedek zamanı | ✅ yedek betiği ölçüm yazar |
| Bu ölçümlere bağlı otomatik uyarılar | ✅ 17 kural, Alertmanager e-posta/webhook |
| "Worker açık ama olaylar ilerlemiyor" senaryosu | ✅ `verify_monitoring.py --faults`: alarm açıldı, e-posta gitti, kendiliğinden kapandı |

Ayrıntı `docs/izleme.md` belgesinde. Eşikler pilot verisiyle ayarlanacak.

**Yeni teknolojiler:**

- **OpenTelemetry:** Uygulama ölçümleri ve istek izlerinin ortak biçimde üretilmesi ve aktarılması.
- **Prometheus:** Sayısal ölçümlerin zaman içinde tutulması ve kuralların değerlendirilmesi.
- **Grafana:** Ölçümlerin operasyon panolarında gösterilmesi.

**Amaç:** "Worker açık görünüyor ama olaylar ilerlemiyor" durumu otomatik fark edilir ve ekip haberdar olur. İlk pilot için temel ölçümler yeterlidir. Ayrıntılı panolar kullanım verisiyle genişler.

---

## 5. V1'in tamamlanması — Kontrollü gerçek kullanım ⏳

Bu aşamada yeni özellik eklemek kadar gerçek kullanım sonuçlarını değerlendirmek önemlidir.

| Görev | Durum |
|---|---|
| Bir müşteri veya operasyon ekibiyle sınırlı pilot | ⏳ |
| Gerçek işlem kimlikleriyle uçtan uca doğrulama | ⏳ |
| Gecikme eşiklerinin gerçek iş sürelerine göre ayarlanması | ⏳ |
| Gereksiz ve eksik alarmların incelenmesi | ⏳ |
| Bildirimlerin doğru kişiye ulaştığının kontrolü | ⏳ |
| Kullanıcıların sorunlu işlemi bulup anlayabilmesinin değerlendirilmesi | ⏳ |
| Beklenen trafik ve kesinti sonrası biriken trafikle kapasite denemesi | ⏳ |

**Teknoloji:** Mevcut sistem ve Aşama 4'te eklenen izleme araçları.

**V1 burada tamamlanmış sayılır:** Gerçek süreç izleniyor, sorunlar bildiriliyor, sistem işletilebiliyor ve yedekten geri dönüş yolu doğrulanmış durumda.

Go, Python analitiği ve ileri özellikler V1 kapanışından sonra ele alınır.

---

## 6. V1.1 — Günlük kullanım ve operasyon özellikleri ✅

V1'in ardından ilk ürün geliştirme paketi kullanıcıların günlük işini hızlandırır.

| Özellik | Açıklama | Durum |
|---|---|---|
| İşlem arama | Sipariş veya işlem numarasıyla doğrudan bulma | ✅ `search` parametresi, büyük/küçük harf duyarsız |
| Tarih filtreleri | Belirli gün, saat ve dönemlerdeki kayıtları listeleme | ✅ `from`/`to`; işlemde oluşturulma, alarmda açılış zamanı |
| Kayıtlı filtreler | Sık kullanılan görünümleri saklama | ✅ Kullanıcıya özel, proje başına 50 görünüm |
| Test verilerinin ayrılması | Deneme işlemlerinin gerçek istatistikleri etkilememesi | ✅ `TEST-` önekli işlemler varsayılan olarak hariç |
| Alarm sorumlusu | Alarmın kim tarafından takip edildiğinin görünmesi | ✅ Üstlen, ata, kaldır; sahip veya operatör |
| Alarm notları | Yapılan inceleme ve müdahalenin kaydedilmesi | ✅ Kalıcı, yazar ve zamanla |
| E-posta daveti | Kullanıcının kendi hesabını tamamlayarak projeye katılması | ✅ 7 gün geçerli tek kullanımlık bağlantı |
| Parola sıfırlama | Hesap erişiminin güvenli şekilde geri alınması | ✅ 1 saat geçerli bağlantı, adres varlığı sızmaz |
| Veri saklama kuralları | Olay, bildirim ve denetim kayıtlarının saklama süreleri | ✅ Proje başına gün, worker günlük süpürür |
| Dışa aktarım | Yetkiye uygun işlem ve alarm raporları | ✅ CSV, sahip ve operatör, denetim kaydıyla |

**Teknoloji:** .NET API, PostgreSQL ve Next.js panel.

**Bitiş ölçütü:** Operasyon ekibi işlemleri bulabilir, alarmları paylaşabilir ve inceleme geçmişini sistemde tutabilir.

**Doğrulama:** `scripts/verify_v1_1.py` 48 kontrol, 59 birim testi, tarayıcı kontrolü. Ayrıntı `docs/v1-1.md`.

---

## 7. V1.2 — Akış sürümleme ve gelişmiş süre kuralları ✅

Mevcut akışların sabit olması geçmiş işlemlerin anlamını koruyor. Bu yaklaşım açık bir sürüm yönetimine dönüştürülür.

| Özellik | Durum |
|---|---|
| Akış taslağı oluşturma ve yayınlama | ✅ Tek taslak, kaydet, yayınla, sil |
| Akış sürümlerinin izlenmesi | ✅ Sürüm tablosu: taslak, etkin, planlandı, eski; işlem sayısı |
| Yeni işlemlerin yeni sürüme bağlanması | ✅ İlk bildirimde etkin sürüm |
| Devam eden işlemlerin başladıkları sürümde kalması | ✅ `flow_version_id`; başka sürümün adımı 400 |
| Sürümler arasındaki farkların görüntülenmesi | ✅ Diff endpoint ve panel sayfası |
| Yeni sürümün etkinleşceği zamanın belirlenmesi | ✅ `effectiveFrom`, en fazla 1 yıl ileri |
| Mesai saatleri, tatiller ve saat dilimine göre süre hesaplama | ✅ Proje takvimleri, sürüme bağlanır |
| İş türüne veya müşteri grubuna göre farklı süre hedefleri | ✅ Bildirimdeki `group` alanı, adım başına grup süreleri |
| Tamamlanmış işlemlerde geçmiş süre ihlallerinin raporlanması | ✅ Raporlar sekmesi, `transaction_step_stats` |

**Teknoloji:** İş kuralları .NET tarafında, sürüm ve takvim verileri PostgreSQL'de, yönetim ekranları Next.js'te.

**Bitiş ölçütü:** Akış değişikliği devam eden işlemlerin anlamını bozmaz. Süreler iş takvimine göre doğru hesaplanır.

**Doğrulama:** `scripts/verify_v1_2.py` 34 kontrol, 67 birim testi, tarayıcı kontrolü. Ayrıntı `docs/v1-2.md`.

---

## 8. V1.2 sonrası — Go ile kaynak tarafı aktarım ajanı ✅

Go'nun ilk görevi müşteri sisteminin yanında çalışan olay aktarım ajanıdır. İlk gerçek entegrasyon ve pilottan sonra, farklı kaynaklarda tekrar kullanılabilir bir gönderici ihtiyacı doğrulanınca eklenir.

| Görev | Durum |
|---|---|
| Desteklenen kaynaklardan kalıcı olay kayıtlarını alma | ✅ PostgreSQL outbox ve dizin kaynağı |
| IntegraOps API'sine HTTPS üzerinden gönderme | ✅ Uzak adres için https zorunlu |
| Ağ kesintisinde gönderimleri bekletme | ✅ Kayıtlar kaynakta kalır; deneme sayacı artmaz |
| Kontrollü tekrar deneme | ✅ Üstel bekleme, 50 deneme sınırı, kalıcı hata ayrımı |
| Gönderim hızını sınırlama | ✅ Token bucket, `RATE_PER_SEC` |
| Bağlantı durumunu ve bekleyen kayıt sayısını bildirme | ✅ `/status`, `/metrics` |
| Yapılandırma ve sürüm bilgisini merkeze raporlama | ✅ Yaşam sinyali; panel Sistem ekranı |

**Teknoloji:** Go ile ayrı çalıştırılabilir ajan. Kaynak kuyruğundan bağımsız yerel tampon gerekirse SQLite değerlendirilir.

**Neden Go:** Ayrı dağıtılabilen ve eşzamanlı gönderimleri yöneten bir bileşen için anlamlı bir görev sınırı oluşturur. .NET, işlem durumu ve alarm kurallarının merkezi olmaya devam eder.

**Bitiş ölçütü:** Ajan yeniden başlatma ve ağ kesintilerinden sonra gönderimlere doğru olay kimlikleriyle devam eder.

**Doğrulama:** `scripts/verify_agent.py` 21 kontrol (API kesintisi dahil), Go birim testleri. Ayrıntı `docs/aktarim-ajani.md`.

---

## 9. V2 — Python ile kaynak-hedef mutabakatı ✅

Python'ın ilk bağımsız ürün bileşeni mutabakat servisidir. Bugün Python test ve kurulum araçlarında kullanılıyor. Bu aşamada düzenli çalışan bir ürün görevi üstlenir.

**Çözülen problem:** Kaynak sistemde oluşan bir işlem IntegraOps'a hiç bildirilmediyse, olay akışı o işlemin varlığını öğrenemez. Bunun için kaynak kayıtlarıyla karşılaştırma gerekir.

| Görev | Durum |
|---|---|
| Kaynaktaki işlemlerle IntegraOps kayıtlarının dönemsel karşılaştırılması | ✅ `reconciler/`, SQL ya da JSON kaynağı, `--loop` |
| Hiç bildirilmemiş işlemlerin bulunması | ✅ `missing_transaction` |
| Eksik adım ve durum uyuşmazlıklarının belirlenmesi | ✅ `missing_step`, `status_mismatch` |
| Karşılaştırılan zaman aralığının ve son başarılı çalışmanın kaydı | ✅ `reconciliation_runs`, panel Mutabakat sekmesi |
| Geç gelen veriler için bekleme payı | ✅ `GRACE`; kayıt ve adım düzeyinde |
| Aynı uyuşmazlığın tekrar tekrar alarm üretmemesi | ✅ Tekilleştirme; yalnızca yeni uyuşmazlık e-posta ile bildirilir |

**Teknoloji:** Ayrı Python worker. Kaynağın API'si veya yetkili okuma bağlantısı. Sonuçlar için tanımlanmış IntegraOps servis sözleşmesi. HTTP yönetim uçları gerekirse FastAPI değerlendirilir. İlk sürüm yalnızca zamanlanmış worker olarak çalışabilir.

**Bitiş ölçütü:** Kontrollü olarak bildirimi eksik bırakılan işlem bulunur. Geç gelen normal veri gereksiz uyuşmazlık oluşturmaz.

**Doğrulama:** `scripts/verify_reconciler.py` 18 kontrol, 7 Python birim testi. Ayrıntı `docs/mutabakat.md`.

---

## 10. V2 — Süre analizi, raporlama ve operasyon göstergeleri ✅

| Gösterge | Durum |
|---|---|
| Adım bazında gerçekleşme süreleri | ✅ Ortanca, p90, p95, en uzun |
| Süre hedeflerine uyum oranları | ✅ Genel, adım ve grup başına |
| En sık geciken veya hata veren adımlar | ✅ Süre aşımı ve hata gruplaması |
| Sistem, akış ve dönem karşılaştırmaları | ✅ Akış filtresi, grup dağılımı, günlük/haftalık eğilim |
| Alarmın açılmasından görülmesine ve çözülmesine kadar geçen süre | ✅ Ortalama ve ortanca |
| Tekrarlayan sorunların listelenmesi | ✅ 2+ alarm açılan işlemler |
| Günlük ve haftalık raporlar | ✅ Zamanlanmış özet e-postası, anlık gönderim, Python komutu |
| Veri kalitesi göstergeleri: eksik olay, tutarsız zaman, geç bildirim oranları | ✅ Mutabakat, `out_of_order`, geç ve ileri tarihli olaylar |

**Teknoloji:**

- **PostgreSQL:** Temel ölçümler ve özet tabloları.
- **Python:** Daha kapsamlı dönemsel analizler ve mutabakatla birleştirilen hesaplamalar.
- **.NET:** Rapor erişimi ve yetkilendirme.
- **Next.js:** İş kullanıcılarına yönelik grafik ve rapor ekranları.

**Bitiş ölçütü:** Kullanıcı hangi sürecin ne sıklıkta ve ne kadar geciktiğini güvenilir biçimde görür.

**Doğrulama:** `scripts/verify_reports.py` 24 kontrol. Ayrıntı `docs/raporlar.md`.

---

## 11. V2 — Daha karmaşık süreçlerin takibi ✅

Doğrusal akışların ötesine geçmek ayrı bir iş kuralı geliştirmesidir.

| Özellik | Durum |
|---|---|
| Paralel ilerleyen adımlar | ✅ Aynı aşama numarası; her dal ayrı süre ve alarm |
| Koşula bağlı adımlar | ✅ Gruba bağlı koşullu adım |
| İsteğe bağlı adımlar | ✅ Beklenmez, ölçülmez |
| Atlanan adımın açıkça belirtilmesi | ✅ `skipped` bildirimi |
| İşlemin iptal edilmesi | ✅ `cancelled` bildirimi, İptal edildi durumu |
| Birden fazla dalın tamamlanmasını bekleyen noktalar | ✅ Sonraki aşama bütün beklenen dalları bekler |
| Bu durumların işlem sonucuna ve alarmlara etkisi | ✅ Durum kuralları, alarm kapanış nedenleri, rapor ölçümleri |

**Teknoloji:** Kurallar .NET çekirdeğinde, model PostgreSQL'de, gösterim Next.js'te.

**Amaç:** Gerçek iş süreçlerini doğru yorumlamak. IntegraOps bu aşamada da ERP, ödeme veya kargo işlemlerini yürütmez, durumlarını izler. Koşula bağlı olarak atlanan bir adım, bildirimi gelmediği için yanlış alarm üretmez.

**Doğrulama:** `scripts/verify_v2_flows.py` 24 kontrol, 8 birim testi. Ayrıntı `docs/v2-akislar.md`.

---

## 12. V2–V3 — Entegrasyonların tekrar kullanılabilir hale gelmesi ✅

İlk birkaç gerçek entegrasyondan öğrenilenler ürünleştirilir.

| Görev | Durum |
|---|---|
| Tekrar kullanılabilir gönderici kütüphaneleri | ✅ `sdk/python`, `sdk/typescript`; Go ajanı |
| Kaynak bağlantısı kurulum ekranları | ✅ Bağlantılar sekmesi, bağlantıya bağlı anahtarlar |
| Bağlantı testi ve son başarılı veri alışverişi bilgisi | ✅ Ping, son olay, son hata, red sayısı, şablon kuru çalıştırma |
| Hazır olay eşleme şablonları | ✅ 3 katalog şablonu, sunucu tarafı eşlemeli alım |
| Kaynağa özel kimlik doğrulama ve anahtar yenileme | ✅ Bağlantıya bağlı anahtar, geçiş süreli yenileme |
| Bağlantı sürümlerinin ve uyumluluğunun izlenmesi | ✅ User-Agent ve ajan sürümü, uyumluluk işareti |
| İhtiyaç duyulan ERP ve e-ticaret sistemleri için hazır bağlayıcılar | ⛔ Gerçek müşteri ihtiyacı belirleyecek; şablon ve kütüphane zemini hazır |

**Teknoloji:** Go ajanı, .NET entegrasyon bileşenleri ve gerektiğinde TypeScript/Python istemci kütüphaneleri. Hangi ERP bağlayıcısının önce geleceğini gerçek müşteri ihtiyacı belirler.

**Bitiş ölçütü:** İkinci müşteri aynı kaynak sistemi bağlamak için ilk entegrasyonun tamamını yeniden yazmaz.

**Doğrulama:** `scripts/verify_connections.py` 32 kontrol, SDK ve eşleme birim testleri. Ayrıntı `docs/baglantilar.md`.

---

## 13. Kurumsal kullanım ve ticari ürün ⏳

| Görev | Durum |
|---|---|
| Şirket/organizasyon yönetiminin genişletilmesi | ⏳ |
| Organizasyon seviyesinde yetkiler ve proje grupları | ⏳ |
| Kurumsal giriş (SSO) ve çok faktörlü doğrulama | ⏳ |
| Daha kapsamlı denetim kayıtları | ⏳ |
| Müşteri bazında kullanım sınırları | ⏳ |
| Hesap açılışı, deneme süresi ve kurulum yönlendirmeleri | ⏳ |
| Abonelik ve kullanım ölçümü | ⏳ |
| Müşteri verilerinin saklanması, dışa aktarılması ve silinmesi süreçleri | ⏳ |
| Destek ve olay müdahale prosedürleri | ⏳ |

**Teknoloji:** Mevcut .NET ve PostgreSQL üzerine standart kimlik sağlayıcı entegrasyonu. Ticari model seçildikten sonra ödeme/abonelik sağlayıcısı.

**Bitiş ölçütü:** Birden fazla şirketin hesabı, erişimi, verisi ve kullanım hakları yönetilebilir.

---

## 14. Ölçek büyüdüğünde — Kapasite ve erişilebilirlik ⏳

Bu işler ölçülen ihtiyaçlara göre sıraya alınır.

| İhtiyaç | Değerlendirilecek geliştirme |
|---|---|
| Olay işleme kapasitesinin yetmemesi | Worker sayısının artırılması, kuyruk bölümleme, toplu işleme |
| Veritabanı büyümesi | İndeksler, arşivleme, uygun tablolarda bölümleme |
| Tek sunucu arızasında hizmetin devam etmesi | Veritabanı ve broker için yüksek erişilebilirlik |
| Tekrarlanan okuma yükünün artması | Gerektiğinde Redis önbelleği |
| Servis ve sunucu sayısının yönetiminin zorlaşması | Yönetilen konteyner altyapısı veya Kubernetes |
| Analitik sorguların işlem sistemini zorlaması | Ayrı raporlama deposu |

Dil çeşitlendirmek tek başına kapasite çözümü sayılmaz. Darboğazın nerede olduğu ölçülerek ilerlenir.

Yeterli geçmiş veri oluşursa Python üzerinden anomali tespiti, olası neden önerileri ve olay özetleme eklenebilir. Bunlar ileri aşama özellikleridir, temel ürünün tamamlanması için şart değildir.

---

## Teknolojilerin görev dağılımı

| Teknoloji | Projedeki görevi | Devreye giriş |
|---|---|---|
| .NET / C# | API, yetkilendirme, işlem durumu, akış ve alarm kuralları | Mevcut; çekirdek olarak kalacak |
| Next.js / TypeScript | Operasyon paneli ve yönetim ekranları | Mevcut; genişleyecek |
| PostgreSQL | Kalıcı olaylar, durumlar, kullanıcılar, outbox ve rapor verileri | Mevcut |
| RabbitMQ | Asenkron olay aktarımı ve worker kuyruğu | Mevcut |
| Docker Compose / Caddy | İlk canlı dağıtım ve HTTPS | V1 canlı kabulü |
| OpenTelemetry / Prometheus / Grafana | Teknik sağlık, ölçüm ve operasyon panoları | Canlı pilot hazırlığı |
| Go | Kaynak tarafında tekrar kullanılabilir aktarım ajanı | Mevcut: `agent/` dizini |
| Python | Mutabakat ve dönemsel analiz servisi | Mevcut: `reconciler/`; yardımcı betikler |
| SQLite | Gerekirse Go ajanının yerel kalıcı tamponu | Ajan tasarımında ihtiyaç olursa |
| Redis / Kubernetes | Önbellek veya büyük dağıtımın yönetimi | Ölçülen ihtiyaç oluşursa |

## İlerleme günlüğü

| Tarih | Aşama | Olay |
|---|---|---|
| 17 Eylül 2026 | Çekirdek | V1 yerel kabul kaydı |
| 21 Eylül 2026 | Paket 01 | Panel doğruluğu, oturumlu doğrulama; 33 kontrol ve kesinti provası geçti |
| 22 Eylül 2026 | Aşama 1 | Kurtarma provası `e04c837883fa`: 21 otomatik kontrol geçti |
| 22 Eylül 2026 | Aşama 1 | Tarayıcı kabulü geçti, prova ortamı temizlendi. **Aşama 1 tamamlandı** |
| 22 Eylül 2026 | Aşama 4 | OpenTelemetry, Prometheus, Alertmanager, Grafana eklendi; 15 kontrol ve kesinti senaryosu geçti. **Aşama 4 tamamlandı** |
| 22 Eylül 2026 | Aşama 3 | İmaj yayın hattı, canlı betikler ve yerel prova: 26 kontrol geçti. Gerçek sunucu bekleniyor |
| 22 Eylül 2026 | Aşama 2 | Entegrasyon sözleşmesi yazıldı. Kaynak sistem seçimi bekleniyor |
| 22 Eylül 2026 | V1.1 | Günlük kullanım özellikleri: migration 004, 48 kontrol geçti. **V1.1 tamamlandı** |
| 22 Eylül 2026 | V1.2 | Akış sürümleme, takvim, grup süreleri, ihlal raporu: migration 005, 34 kontrol geçti. **V1.2 tamamlandı** |
| 22 Eylül 2026 | Aşama 8 | Go aktarım ajanı, yaşam sinyali (migration 006), 21 kontrol geçti. **Aşama 8 tamamlandı** |
| 22 Eylül 2026 | Aşama 9 | Python mutabakat servisi, sözleşme uçları (migration 007), 18 kontrol geçti. **Aşama 9 tamamlandı** |
| 22 Eylül 2026 | Aşama 10 | Rapor göstergeleri, eğilim, özet e-postaları (migration 008), 24 kontrol geçti. **Aşama 10 tamamlandı** |
| 22 Eylül 2026 | Aşama 11 | Paralel/koşullu/isteğe bağlı adımlar, atlama ve iptal (migration 009), 24 kontrol geçti. **Aşama 11 tamamlandı** |
| 22 Eylül 2026 | Aşama 12 | Bağlantılar, eşleme şablonları, anahtar yenileme, Python/Node SDK (migration 010), 32 kontrol geçti. **Aşama 12 tamamlandı** |
