# IntegraOps — Ürün boşlukları ve pazarlama fırsatları

**Tarih:** 24 Eylül 2026 · **Durum:** Dalga 1'in ürün içi maddeleri (A1, A5, A6, B6) uygulandı, bkz. `docs/dalga-1.md`; B5 Gemini ile ayrı çalışmada; tanıtım sitesi bekliyor.
**Kapsam:** Depodaki kod (backend 14 migration, ~110 API ucu, 25 panel sayfası, Go ajanı, Python mutabakat, 2 SDK), 20 belge ve 20 doğrulama betiği incelendi. Bu belge "ne var, ne yok, şirketleri ne cezbeder, sırada ne olmalı" sorularını yanıtlar.

---

## 1. Kısa cevap

**Evet, proje daha ileri taşınabilir; ancak darboğaz artık teknik değil, ticari.**

Ürün, aynı kategorideki (iş süreci gözlemlenebilirliği / business process monitoring) ürünlerin çoğunda görülmeyen bir teknik olgunluğa sahip: transactional outbox ile kayıpsız kabul, akış sürümleme, çalışma takvimli süre hesabı, paralel ve koşullu adımlar, kaynak-hedef mutabakatı, organizasyon/plan/SSO/MFA katmanı, izleme ve kurtarma provası. Bunların hepsi otomatik doğrulama betikleriyle kanıtlanmış.

Buna karşılık üç şey yok:

1. **Gerçek bir müşteri ve gerçek veri.** Yol haritasındaki Aşama 2, 3 ve 5 hâlâ dış girdi bekliyor. Pazarlama malzemesinin en güçlü parçası (vaka çalışması, gerçek süre tasarrufu) bu yüzden üretilemiyor.
2. **Şirketlerin "ilk 15 dakikada" değer görmesini sağlayan unsurlar.** Hazır bağlayıcı, sektör şablonu, örnek veriyle dolu demo ortamı, Slack/Teams bildirimi, İngilizce panel ve herkese açık API belgesi yok.
3. **Kendi kendine satış yolu.** Ödeme sağlayıcısı ve plan yükseltme akışı olmadığı için deneme hesabı ücretli müşteriye insan müdahalesi olmadan dönüşemiyor.

Önerilen strateji: **altyapıya yeni katman eklemeyi durdurup** bir sonraki üç dalgayı "değer görünürlüğü" ve "dönüşüm" üzerine kurmak (bölüm 7).

---

## 2. Mevcut durum envanteri

| Alan | Var | Kanıt |
|---|---|---|
| Kayıpsız olay kabulü | Outbox + RabbitMQ + worker, at-least-once, idempotent | `docs/sistem-mimarisi.md`, `verify_v1.py --faults` |
| Süreç modeli | Doğrusal, paralel aşama, isteğe bağlı/koşullu adım, `skipped`, `cancelled`, gruplar | `009_complex_flows.sql`, `docs/v2-akislar.md` |
| Süre kuralları | Adım süresi, grup bazlı süre, çalışma takvimi, tatil, saat dilimi | `docs/v1-2.md` |
| Akış yaşam döngüsü | Taslak, sürüm, ileri tarihli yayın, fark görünümü | `005_flow_versions.sql` |
| Alarm | Süre aşımı ve hata; üstlenme, atama, not, otomatik kapanış | `003_pilot.sql`, `docs/v1-1.md` |
| Bildirim | E-posta ve HMAC imzalı webhook; kalıcı teslimat, tekrar deneme | `NotificationTransport.cs` |
| Raporlar | Yüzdelikler, süre uyumu, alarm yanıt süresi, tekrarlayan sorun, veri kalitesi, eğilim, günlük/haftalık özet | `docs/raporlar.md` |
| Mutabakat | Kaynak SQL/JSON ile karşılaştırma, hiç bildirilmemiş işlem tespiti | `reconciler/` |
| Bağlanma yolları | Doğrudan API, Go ajanı (Postgres outbox, dizin), Python/Node SDK, sunucu tarafı eşleme şablonu | `docs/baglantilar.md` |
| Kurumsal | Organizasyon, roller, gruplar, plan ve günlük sınır, TOTP, OpenID Connect, güvenlik olayları, dışa aktarım, silme | `011_organizations.sql`, `docs/kurumsal.md` |
| İşletim | OpenTelemetry, Prometheus 17 kural, Grafana, yedek/geri yükleme provası, imaj yayını, geri dönüş | `docs/izleme.md`, `docs/canli-ortam.md` |
| Yapay zekâ | Model bağımsız katman, kanıta dayalı alarm inceleme, geri bildirim, plan eklentisi; **gerçek model adaptörü yok** | `AiAssistant.cs`, `docs/yapay-zeka.md` |
| Kapasite | Yük testi aracı, toplu yayın, worker/API kopyaları, eşik tablosu | `docs/olcek.md` |

**Ölçek notu:** Yerel makinede ~480 olay/sn kabul, 140–200 olay/sn uçtan uca işleme. Günlük 500 bin olay hedefi için fazlasıyla yeterli; pazarlamada "kesinti sonrası birikeni saatler değil dakikalar içinde eritir" olarak kullanılabilir.

---

## 3. Pazar konumu

IntegraOps'un rakibi genel APM araçları (Datadog, Dynatrace, New Relic) değil. Onlar servis ve sunucu izler; "SO-000123 numaralı siparişin faturası neden 4 saattir kesilmedi?" sorusunu yanıtlamaz. Doğrudan rakipler:

| Kategori | Örnekler | IntegraOps'un farkı |
|---|---|---|
| Süreç madenciliği / analitik | Celonis, UiPath Process Mining | Geriye dönük analiz yaparlar; IntegraOps canlı alarm üretir. Fiyatları KOBİ için erişilmez |
| İş akışı motorları | Camunda (Optimize), Temporal | Süreci **çalıştırırlar**; IntegraOps yalnızca gözlemler, mevcut sistemlere dokunmaz. Kurulum maliyeti çok daha düşük |
| Entegrasyon platformları (iPaaS) | Workato, Make, n8n, Tray | Veri taşırlar; süre hedefi, mutabakat, işlem bazlı alarm yoktur |
| Log/APM tabanlı iş izleme | Splunk Business Flow, Dynatrace Business Events | Log veya iz üzerinden çıkarım yapar; IntegraOps açık sözleşmeyle olay alır, kaynakta outbox şartıyla **kayıpsızlık** vaat eder |
| Türkiye'de e-ticaret/ERP entegratörleri | Entegra, Koala, Akınsoft entegrasyon modülleri | Veri aktarımına odaklanır; "eksik olan işlemi bulma" (mutabakat) ve süre ihlali raporu yoktur |

**Konumlandırma cümlesi önerisi:** "Sistemleriniz arasında kaybolan siparişi, geciken faturayı ve tekrar eden hatayı dakikalar içinde gösteren, hiçbir sisteminize yazmayan gözlem katmanı."

**Farklılaştırıcı üç iddia (hepsi kodla kanıtlı):**

1. **Kayıpsızlık:** Kaynakta outbox + ajan + idempotent kabul. Ağ kesilse de olay kaybolmaz (`verify_v1.py --faults`, `verify_agent.py`).
2. **Hiç bildirilmeyeni de buluruz:** Mutabakat servisi kaynak veritabanındaki kaydı IntegraOps'ta arar. Rakiplerin çoğu yalnızca kendine ulaşan veriyi bilir.
3. **Kanıta dayalı yapay zekâ:** Model tespit yapmaz, açıklar; her cümle kayda bağlanır. "Fatura kesilmedi" ile "fatura bildirimi ulaşmadı" ayrımı korunur.

---

## 4. Eksik özellikler

Öncelik sınıfları:

- **A — Satışı engelleyen:** Bunlar olmadan deneme hesabı müşteriye dönüşmez ya da demo ilk toplantıda takılır.
- **B — Rekabet avantajı:** Rakiplerde zayıf, IntegraOps'un mimarisiyle kolay yapılabilir.
- **C — Kurumsal kapı açıcı:** Büyük müşterinin satın alma ve güvenlik ekibinin soracağı sorular.
- **D — Büyüme ve verimlilik:** Kullanım artınca gerekli.

### 4.1 Sınıf A — Satışı engelleyen

| # | Eksik | Bugünkü durum | Neden cezbeder | Efor |
|---|---|---|---|---|
| A1 | **Slack, Microsoft Teams, SMS ve Telegram bildirim kanalları** | `notification_channels.kind` yalnızca `email` ve `webhook` (`003_pilot.sql:57`) | Operasyon ekipleri e-posta okumaz; ilk demo sorusu "Slack'e düşer mi?" olur. Webhook altyapısı hazır olduğu için Slack/Teams biçimleyici eklemek küçük iş | Küçük |
| A2 | **Alarm yönlendirme ve eskalasyon** | Etkin bütün kanallar bütün alarmları alır; akış, adım, grup veya önem bazlı yönlendirme, sessiz saatler, bakım penceresi, erteleme, eskalasyon zinciri yok | "Kargo gecikmesi depoya, fatura hatası muhasebeye gitsin; 30 dakikada görülmezse yöneticiye çıksın" her orta ölçekli şirketin ilk isteğidir. Alarm yorgunluğunu önlemeden pilot ekibi ürünü kapatır | Orta |
| A3 | **Olaya iş verisi ekleme (`attributes`)** | Olay gövdesi yalnızca kimlik, adım, durum, zaman, hata ve grup taşır (`Contracts.cs`) | "Geciken siparişlerin toplam tutarı", "hangi müşteri", "hangi depo" soruları yanıtlanamıyor. Tutar, müşteri segmenti, bölge gibi 5–10 anahtar/değer alanı arama, filtre ve rapora girince ürün finans ve satış yöneticisinin de diline geçer | Orta |
| A4 | **İngilizce panel ve e-postalar** | `layout.tsx` `lang="tr"`; bütün metinler sabit Türkçe | Yabancı sermayeli Türk şirketleri ve ihracatçılar İngilizce ister; yurt dışı pazar tamamen kapalı | Orta (metin ayrıştırma) |
| A5 | **Herkese açık API belgesi (OpenAPI/Swagger) ve geliştirici portalı** | Swagger yok; sözleşme yalnızca `docs/entegrasyon-sozlesmesi.md` | Entegrasyonu yazacak geliştirici ilk gün ne bulacağını bilmeli. OpenAPI dosyası aynı zamanda Postman koleksiyonu ve SDK üretimi sağlar | Küçük |
| A6 | **Örnek veriyle dolu demo ortamı ve sektör şablonları** | Akış boş açılır; başlangıç listesi 6 adım, ama ilk değer görme kaynak entegrasyonuna bağlı | "Önce göreyim" diyen potansiyel müşteri için tek tıkla 30 günlük sahte sipariş/fatura/kargo verisi ve hazır akış şablonları (e-ticaret siparişi, ERP sipariş → fatura, lojistik teslimat, ödeme mutabakatı, sigorta hasar dosyası) gerekir | Küçük–Orta |
| A7 | **Ödeme sağlayıcısı ve kendi kendine plan yükseltme** | Planlar `/admin` sayfasından elle atanır; yol haritası "ödeme sağlayıcısı ticari model seçilince" diyor | Deneme bitince kullanıcı satışçı beklemek istemez. iyzico (yurt içi) + Stripe (yurt dışı), aylık/yıllık fatura, kullanım aşımında otomatik uyarı | Orta |

### 4.2 Sınıf B — Rekabet avantajı

| # | Eksik | Bugünkü durum | Neden cezbeder | Efor |
|---|---|---|---|---|
| B1 | **Hazır bağlayıcılar (Türkiye pazarı öncelikli)** | Yalnızca eşleme şablonu ve SDK; bağlayıcı yok (`docs/baglantilar.md` son paragraf) | Öncelik sırası: Logo Tiger/Netsis, Mikro, SAP B1 ve Nebim (ERP); Trendyol, Hepsiburada, Shopify, İkas, Ticimax (pazar yeri/e-ticaret); iyzico, PayTR, Param (ödeme); Yurtiçi, Aras, MNG, Sürat (kargo takip webhook'u). Her bağlayıcı bir landing sayfası ve arama trafiği demektir | Bağlayıcı başına küçük–orta |
| B2 | **Ajan için MySQL, MSSQL ve Oracle outbox kaynağı** | Go ajanı yalnızca `postgres` ve `directory` (`agent/internal/config/config.go:143`) | Türkiye'deki ERP'lerin büyük çoğunluğu MSSQL üzerinde çalışır. Bu tek başına ajanı çoğu müşteri için kullanılmaz kılıyor | Küçük–Orta |
| B3 | **Öğrenilen süre eşikleri ve anomali tespiti** | `maxDelaySeconds` elle girilir; sözleşme "son 30 günün p95'i" diye yönlendirir ama sistem bunu hesaplamaz | "Bu adım için önerilen süre 2 sa 10 dk (p95)" düğmesi kurulum süresini kısaltır. Sonra "bugün ortancanın 3 katı" tipi dinamik uyarı, sabit eşiklerin kaçırdığı yavaşlamayı yakalar. Yapay zekâ olmadan, istatistikle yapılır | Orta |
| B4 | **Sorun grupları (alarm birleştirme)** | Alarmlar tek tek; `docs/yapay-zeka.md` 1.2 planlanmış ama yazılmamış | 300 sipariş aynı fatura adımında takıldığında 300 alarm yerine "1 olası ortak sorun, 300 etkilenen işlem". Bu, alarm yorgunluğunun kalıcı çözümüdür ve yapay zekâ gerektirmez (adım + zaman penceresi + hata metni benzerliği) | Orta |
| B5 | **Gerçek model adaptörü ve günlük anlatımlı özet** | `Ai__Provider` yalnızca `none` ve `fake` (`AiAssistant.cs:72`) | Altyapı hazır; adaptör olmadan "yapay zekâ destekli" iddiası pazarlamada kullanılamaz. Model seçimi tek karar, adaptör küçük iş. Günlük özet e-postasına anlatımlı paragraf eklemek görünürlüğü en yüksek yapay zekâ özelliğidir | Küçük (adaptör), orta (özet) |
| B6 | **Adım başına çalışma talimatı (runbook) ve dış sistem bağlantısı** | Alarm notu var; adıma bağlı kalıcı talimat yok | Akış tanımında "bu adım hata verirse şu ekranı aç, şu komutu çalıştır" yazılır; alarm açıldığında talimat ve kaynak sistemin kayıt bağlantısı (`https://erp/orders/{transactionId}`) alarmın içinde gelir. Yeni operatörün öğrenme süresini sıfırlar | Küçük |
| B7 | **Paylaşılabilir işlem durumu bağlantısı (müşteri portalı)** | İşlem detayı yalnızca oturumlu kullanıcıya açık | Şirketin kendi müşterisine veya bayisine "siparişiniz şu aşamada" sayfası verebilmesi ürünü destek ekibinin de aracı yapar. İmzalı, süreli, salt okunur bağlantı yeterli | Küçük–Orta |
| B8 | **Ekip ile paylaşılan görünümler ve panolar** | Kayıtlı görünümler kullanıcıya özel (`docs/v1-1.md`); ana sayfa sabit | Operasyon odası ekranı (TV modu), vardiya bazlı ortak filtreler, sabitlenmiş göstergeler | Küçük |
| B9 | **Toplu alarm işlemleri** | Üstlenme, atama, not tek tek | 50 alarmı seçip "bakım penceresi, kapat" ya da "hepsini X'e ata" | Küçük |

### 4.3 Sınıf C — Kurumsal kapı açıcı

| # | Eksik | Bugünkü durum | Neden gerekir | Efor |
|---|---|---|---|---|
| C1 | **Güvenlik olaylarının SIEM'e akışı ve denetim kaydı dışa aktarımı** | `security_events` ve `audit_log` yalnızca panelde | Kurumsal güvenlik ekibi Splunk/Sentinel'e webhook veya syslog ister | Küçük |
| C2 | **API anahtarı için IP kısıtı ve akış kapsamı** | Anahtar yetkisi `ingest`/`read`; kapsam proje geneli | "Bu anahtar yalnızca şu IP'den ve yalnızca şu akışa yazsın" güvenlik değerlendirmesinde artı puan | Küçük |
| C3 | **SCIM ile kullanıcı sağlama** | SSO var; kullanıcı ilk girişte oluşur, ayrılan otomatik kapanmaz | 200+ kullanıcılı şirketlerde Entra ID/Okta üzerinden otomatik açma/kapama şart | Orta |
| C4 | **Uyum paketi: KVKK/GDPR veri işleme sözleşmesi, güvenlik dokümanı, alt işleyici listesi, veri konumu** | Silme ve dışa aktarma süreçleri var; belge yok | Satın alma bunları PDF olarak ister. Kod hazır olduğu için belge yazmak yeterli; ileride ISO 27001 yolu | Küçük (belge) |
| C5 | **Şirket içi (on-prem) kurulum paketi ve lisans anahtarı** | Compose ile kurulabilir, lisans/etkinleştirme yok | Banka, kamu, sağlık verisini dışarı çıkarmaz. Mevcut Compose + Caddy yapısı zaten buna çok yakın; "kendi sunucunuzda" seçeneği kurumsal planın gerekçesi olur | Küçük–Orta |
| C6 | **Ortam ayrımı (test / canlı)** | Yalnızca `TEST-` öneki | Proje içinde `staging` ve `production` ortamı; ayrı anahtar, ayrı sayaç, ayrı bildirim | Orta |
| C7 | **Herkese açık durum sayfası ve değişiklik günlüğü** | İç izleme var, müşteriye dönük yok | "Sizin sisteminiz ayakta mı?" sorusuna bağlantıyla cevap; güven göstergesi | Küçük |
| C8 | **Kubernetes Helm chart ve Terraform modülü** | Compose ve imajlar hazır; `docs/olcek.md` Kubernetes'i eşik sonrası sayıyor | Kurumsal platform ekipleri Compose'u üretim saymaz; chart varlığı satın almayı kolaylaştırır | Orta |

### 4.4 Sınıf D — Büyüme ve verimlilik

| # | Eksik | Not |
|---|---|---|
| D1 | Kafka / Azure Event Hubs / AWS SQS kaynak ve hedef desteği | Büyük müşteriler olayları zaten bir akış platformunda tutar; ajanın Kafka tüketicisi olması entegrasyonu sıfır koda indirir |
| D2 | Ticket sistemine bağlantı (Jira, ServiceNow, Zendesk) | Alarmdan tek tıkla iş kaydı, kapanınca alarma yansıma |
| D3 | Rapor dışa aktarımı PDF, zamanlanmış rapor, dönem karşılaştırması | Yönetici sunumları için |
| D4 | BI bağlantısı: salt okunur veri API'si veya okuma kopyası + Metabase/Power BI şablonu | Müşterinin kendi raporunu yazması |
| D5 | Mobil bildirim (PWA push) | Nöbetçi operatör için |
| D6 | Karanlık tema ve erişilebilirlik denetimi | `globals.css` içinde `prefers-color-scheme` yok |
| D7 | Bayi / partner programı ve beyaz etiket | Entegratör şirketlerin kendi adıyla satması |
| D8 | Kullanım bazlı faturalama ve fatura geçmişi | `usage_daily` sayar, fatura üretmez |

---

## 5. Pazarlamaya doğrudan hizmet eden ürün içi unsurlar

Bu maddeler özellik değil, satış hunisinin parçasıdır; kod tabanında hiçbiri yok.

| Unsur | Ne | Neden |
|---|---|---|
| **Tanıtım sitesi** | Konumlandırma, 3 farklılaştırıcı, sektör sayfaları, bağlayıcı sayfaları, fiyat tablosu, demo talep formu | Şu an ürünün dışa dönük tek yüzü GitHub README'si |
| **Canlı demo hesabı** | Herkesin girebildiği, saatlik sıfırlanan, sahte veriyle dolu proje | Toplantı öncesi kendi kendine keşif; "signup" öncesi güven |
| **Etkileşimli ürün turu** | İlk girişte 6 adımlı başlangıç listesinin yanına işaretli rehber | Başlangıç listesi var (`GET /onboarding`), ama rehber yok |
| **Getiri hesaplayıcı (ROI)** | "Günde X sipariş, %Y gecikme, gecikme başına Z dakika arama" → aylık kayıp | Satın alma gerekçesi; CFO dili |
| **Vaka çalışması** | Pilot müşteriyle 1 sayfalık sonuç: bulunan eksik işlem sayısı, ortalama fark etme süresi öncesi/sonrası | En ikna edici malzeme; **pilot olmadan yazılamaz** |
| **Karşılaştırma sayfaları** | "IntegraOps vs Celonis", "vs Camunda Optimize", "vs kendi yazmak" | Arama trafiği ve itiraz karşılama |
| **Güven sayfası** | Mimari özeti, şifreleme, yedek/kurtarma provası sonuçları, veri silme süreci, alt işleyiciler | Kurtarma provası ve 68 kurumsal kontrol zaten var; kanıt olarak kullanılmıyor |
| **Ücretsiz araçlar** | Herkese açık "outbox şablonu üreteci", "SLA süresi hesaplayıcı" | Geliştirici topluluğuna giriş |
| **Sürüm notları ve herkese açık yol haritası** | `docs/yol-haritasi.md` içeriğinin müşteri diline çevrilmiş sürümü | Canlı proje algısı |

---

## 6. Sektör bazlı paketleme

Aynı çekirdek, farklı anlatım. Her paket: hazır akış şablonu + örnek veri + bağlayıcı önceliği + tek cümle vaat.

| Sektör | Şablon akış | İlk bağlayıcılar | Vaat |
|---|---|---|---|
| **E-ticaret ve pazar yeri satıcısı** | Sipariş → ödeme → stok → fatura → kargo → teslim | Trendyol, Hepsiburada, Shopify, İkas, iyzico, kargo webhook'ları | "Pazar yerine geç faturayı ceza yemeden önce görün" |
| **Toptan / dağıtım (ERP ağırlıklı)** | Sipariş → onay → sevk emri → irsaliye → e-fatura → tahsilat | Logo, Netsis, Mikro, SAP B1, e-fatura entegratörleri (Foriba, Uyumsoft) | "ERP ile e-fatura arasında kaybolan belgeyi bulun" |
| **Lojistik** | Yük kabul → araç atama → yükleme → transit → teslim → POD | TMS/WMS dışa aktarımları, araç takip API'leri | "Teslim süresi ihlalini müşteri aramadan bilin" |
| **Ödeme ve fintek** | İşlem → yetkilendirme → mutabakat → hakediş → ödeme | Banka mutabakat dosyaları, ödeme kuruluşu API'leri | "Gün sonu mutabakatını gün içinde yapın" |
| **Sigorta** | İhbar → ekspertiz → onay → ödeme | Core sigorta sistemleri, eksper portalları | "Hasar dosyasının hangi masada beklediğini görün" |
| **Üretim** | Sipariş → planlama → üretim emri → kalite → sevk | MES/ERP tabloları (MSSQL ağırlıklı, B2 şart) | "Sipariş sözünü tutamayacağınızı önceden öğrenin" |

Bu tablodaki şablonlar `connections/catalog` mantığıyla (bugün 3 eşleme şablonu var) **akış kataloğu** olarak ürüne eklenebilir; A6 maddesiyle birleşir.

---

## 7. Önerilen sıra

Üç dalga, her biri yaklaşık bir sprint kümesi. Her dalganın sonunda pazarlamaya verilecek somut çıktı yazılı.

### Dalga 1 — "Göster ve ikna et" (pilot bulmadan önce)

| Sıra | İş | Pazarlama çıktısı |
|---|---|---|
| 1 | A6 Demo verisi üreteci + 5 sektör akış şablonu | Canlı demo hesabı; 15 dakikalık demo senaryosu |
| 2 | A1 Slack ve Teams kanalı (webhook biçimleyici) | "Slack'e düşer" cevabı; ekran görüntüsü |
| 3 | A5 OpenAPI dosyası ve `/docs` sayfası | Geliştirici sayfası; Postman koleksiyonu |
| 4 | B6 Adım talimatı ve kaynak sistem bağlantısı | "Alarm sizi ekrana götürür" gösterimi |
| 5 | B5 Gerçek model adaptörü + günlük anlatımlı özet | "Kanıta dayalı yapay zekâ" iddiası artık gösterilebilir |
| 6 | Tanıtım sitesi ve güven sayfası (bölüm 5) | Demo talep hunisi açılır |

**Bu dalgayla paralel:** pilot müşteri arayışı. Bölüm 1'deki ticari darboğaz ancak burada çözülür.

### Dalga 2 — "Pilotu tut, alarm yorgunluğunu bitir" (ilk pilot sırasında)

| Sıra | İş | Pazarlama çıktısı |
|---|---|---|
| 1 | A2 Alarm yönlendirme, sessiz saatler, bakım penceresi, eskalasyon | "Doğru kişiye, doğru zamanda" |
| 2 | B4 Sorun grupları | "300 alarm değil 1 sorun" ekran görüntüsü |
| 3 | A3 Olay `attributes` alanı; arama, filtre ve rapora yansıması | "Geciken siparişlerin toplam tutarı: ₺…" göstergesi |
| 4 | B3 Süre önerisi (p95) ve basit anomali uyarısı | "Eşiği biz öneriyoruz" |
| 5 | B2 MSSQL/MySQL outbox kaynağı | ERP müşterileri için ajan gerçekten kullanılabilir |
| 6 | B1 İlk 2 bağlayıcı (pilotun sistemi + en yaygın pazar yeri) | Bağlayıcı sayfaları |
| 7 | Pilot sonuçlarıyla vaka çalışması | En değerli satış malzemesi |

### Dalga 3 — "Kendi kendine satış ve kurumsal kapı"

| Sıra | İş | Pazarlama çıktısı |
|---|---|---|
| 1 | A7 Ödeme (iyzico + Stripe), plan yükseltme, fatura geçmişi | Deneme → ücretli dönüşüm insan müdahalesiz |
| 2 | A4 İngilizce | Yurt dışı ve çok uluslu müşteri |
| 3 | C4 Uyum belgeleri, C1 SIEM akışı, C2 anahtar kısıtı, C7 durum sayfası | Satın alma güvenlik formu tek seferde geçer |
| 4 | C5 Şirket içi kurulum paketi | Kurumsal planın gerekçesi |
| 5 | B7 Paylaşılabilir işlem bağlantısı, B8 ortak görünümler, B9 toplu işlem | Günlük kullanım derinleşir |
| 6 | C3 SCIM, C8 Helm chart, C6 ortam ayrımı | Büyük müşteri teknik itirazları kalkar |

Sınıf D maddeleri ölçülen talebe göre bu dalgaların arasına girer.

---

## 8. Ne yapılmamalı

- **Yeni altyapı katmanı eklemek** (Redis, Kubernetes, kuyruk bölümleme): `docs/olcek.md` bunları ölçüm eşiğine bağlamış, doğru. Ölçüm olmadan yapılırsa yalnızca bakım yükü artar.
- **Yapay zekâya tespit yaptırmak:** `docs/yapay-zeka.md` bölüm 0'daki "tespiti kurallar yapar, model açıklar" ilkesi hem doğruluk hem pazarlama açısından ürünün en güçlü yanı. Rakiplerin "AI kök neden buldu" iddialarına karşı "kanıta bağlı" duruşu korunmalı.
- **Bağlayıcıyı talep görmeden yazmak:** Bölüm 6'daki liste öncelik sırası verir; ilk bağlayıcı pilotun sistemi olmalı.
- **Pazarlama metnini pilot olmadan büyütmek:** Vaat edilebilir olanlar bölüm 3'te; "yüzde X tasarruf" gibi rakamlar ilk gerçek veriye kadar yazılmamalı.

---

## 9. Sonuç

Kod tabanı, kategori için nadir bir şekilde "kayıpsızlık, sürümleme, mutabakat, kurumsal katman, kurtarma" beşlisini aynı anda taşıyor ve her biri otomatik testle kanıtlı. Eksik olan, bu gücü bir şirketin ilk toplantıda görmesini sağlayan ve deneme hesabını ödemeye çeviren katman: bildirim kanalları, yönlendirme, iş verisi, demo, bağlayıcı, İngilizce, API belgesi ve ödeme.

Sıradaki en yüksek getirili iş yeni bir teknik aşama değil, **Dalga 1 ile eş zamanlı bir pilot müşteri**dir. Pilot hem Aşama 2/3/5'i kapatır hem de bu belgenin B ve C sınıfındaki maddelerinin gerçek sırasını belirler.
