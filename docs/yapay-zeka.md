# IntegraOps — Yapay zekâ desteği

Bu belge yapay zekâ özelliklerinin ürün önerisini, koddaki bağlantı noktalarını ve kurulan altyapıyı bir arada tutar.
**Deneme modeli: Google Gemini `gemini-3.5-flash-lite` (ücretsiz katman, yalnızca sentetik veriyle).** Sağlayıcı adaptörü ve deneme profili eklendi (bkz. "5. Gemini ücretsiz deneme profili" ve `IntegraOps_Gemini_Ucretsiz_Test_Rehberi.md`). Canlı müşteri verisiyle kullanım ayrı karardır.

---

## 0. Temel kural: IntegraOps yapay zekâ olmadan çalışır

**Takip, sorun tespiti, raporlama ve bildirim işlevleri yapay zekâ olmadan çalışır. Yapay zekâ paketi bunların üzerine
açıklama ve inceleme kolaylığı ekler.** Yapay zekâ almayan müşteri ürünün temel işlevlerini eksiksiz kullanır.

| İşlev | Yapay zekâ olmadan | Yapay zekâ paketiyle eklenen kolaylık |
|---|---|---|
| Süreç takibi | Siparişin hangi aşamada olduğu gösterilir | Sürecin durumu anlaşılır bir dille özetlenir |
| Eksik adım ve gecikme | Akış kuralları ve süre eşikleriyle belirlenir | Gecikmenin bağlamı açıklanır, inceleme önerileri sunulur |
| Başarısız işlem | Kaynaktan gelen hata ve işlem durumu gösterilir | Teknik hata daha anlaşılır şekilde açıklanır |
| Alarmlar ve bildirimler | Kurallara göre açılır, gönderilir ve çözülür | Alarm için ek açıklama hazırlanır |
| Mutabakat | Kaynak kayıtları karşılaştırılır, farklar bulunur | Farklar anlatımlı özete dönüştürülür |
| Raporlar | Süre, uyum ve gecikme oranları hesaplanır | Günlük veya haftalık anlatımlı özet hazırlanır |

**Doğruluk kuralı (yapay zekâ açıkken de kapalıyken de aynı):** Tespiti yapay zekâ yapmaz; gösterilen ifade sistemin
elindeki kanıta bağlıdır.

- Kaynak sistem fatura oluşturmanın başarısız olduğunu bildirdiyse (`failed` alarmı): **"Fatura oluşturulamadı."**
- Beklenen fatura olayı sürede gelmediyse (`overdue` alarmı): **"Fatura bildirimi alınmadı; bekleme süresi aşıldı."**
  Yalnızca bildirim eksik olduğu için faturanın kesinlikle kesilmediği söylenmez.

Temel panel kendi kurallarıyla şunu üretir:
> Sipariş #123 — Fatura adımı gecikti. Beklenen süre: 10 dakika. Geçen süre: 13 dakika. Son tamamlanan adım: Ödeme.

Yapay zekâ paketi olan kullanıcı "İncele" dediğinde bunun yanına yardım gelir:
> Ödeme tamamlanmış ancak beklenen fatura bildirimi ulaşmamış. Sorunun hangi tarafta olduğu mevcut verilerle kesinleşmiyor.

**Mimari sınırlar:**

- İşlem durumlarını, süre hesaplarını ve alarm kararlarını uygulamanın kuralları belirler (.NET çekirdeği, worker).
- Yapay zekâ, alarmın gönderilmesini veya işlemin tamamlanmasını bekleten bir bağımlılık değildir; yalnızca kullanıcı isteğiyle
  ya da zamanlanmış özet için çağrılır.
- Yapay zekâ servisi çalışmazsa ya da kota biterse yalnızca o inceleme başarısız olur; temel sistem çalışmaya devam eder.
- Yapay zekâ açıklaması resmî işlem durumunu değiştirmez; `ai_analyses` tablosunda ayrı yardımcı içerik olarak saklanır ve gösterilir.
- Yapay zekâ eklentisinin aboneliği biterse yalnızca yapay zekâ özellikleri kapanır; temel plandaki işlevler devam eder.
  ✅ Organizasyon bazında `ai_enabled` işareti (migration 014): plan varsayılanı deneme ve kurumsalda açık, başlangıç ve
  işletmede kapalı; sistem yöneticisi `/admin` sayfasından müşteri bazında açar ya da kapatır. Kapalıyken uç 403 döner,
  paneldeki düğme görünmez, alarm ve bildirimler aynen çalışır.

**Paketleme:** Temel IntegraOps ürünü + isteğe bağlı yapay zekâ asistanı. Çekirdek ürünün tamamlanma ve canlı kabul
koşulları yapay zekâdan bağımsız değerlendirilir.

---

## 1. Ürün önerisi

Yapay zekâ ilk olarak **alarmı açıklayan ve incelemeyi yönlendiren operasyon asistanı** olarak eklenir. Mevcut verilerle
başlanabilir ve müşteriye somut faydası gösterilebilir.

**Pazarlama odağı:** Sipariş, ödeme, ERP, fatura ve kargo süreçlerindeki sorunları izleyen; yapay zekâ desteğiyle incelemeyi
hızlandıran operasyon platformu.

**Temel ilke:** Yapay zekâ **gördüğü kanıtı ve henüz bilmediği şeyi ayırır.** Sayıları ve kayıtları backend hesaplar;
model yalnızca bunları açıklar. Her bulgu dayandığı kayda bağlanır.

### 1.1 Yapay zekâ ile alarm inceleme — ilk özellik ✅ altyapı hazır

- **Nerede:** İşlem ve alarm detay sayfası, "Yapay zekâ ile incele" işlemi.
- **Ne yapar:**
  - İşlemin olay geçmişini ve beklenen adımlarını okur.
  - Alarmın neden oluştuğunu anlaşılır biçimde açıklar.
  - Gecikme, kaynak hatası ve bildirim ulaşmaması arasındaki farkı belirtir.
  - Varsa benzer işlemlerdeki sorunlarla karşılaştırır.
  - Operatörün kontrol etmesi gereken noktaları sıralar.
  - Açıklamasını ilgili olaylara ve kayıtlara bağlar.
- **Örnek çıktı:** "Sipariş adımı tamamlanmış. Fatura tamamlanma bildirimi tanımlanan süre içinde ulaşmamış. Aynı zaman
  aralığında bu akıştaki başka işlemlerde de benzer gecikmeler görülüyor. Öncelikle fatura entegrasyonunun gönderim
  kuyruğunu ve hata kayıtlarını kontrol edin. Faturanın kaynak sistemde oluşup oluşmadığı mevcut verilerle doğrulanamıyor."
- **Müşteriye faydası:** Teknik ayrıntıları tek tek okumadan sorunu anlamak ve incelemeye doğru yerden başlamak.
- **Pazarlama:** "Kanıta dayalı yapay zekâ destekli alarm analizi."
- **Zamanlama:** V1 kabulünden hemen sonra. Uzun geçmiş veri birikimi gerekmez.

### 1.2 Benzer alarmları birleştirme ve ortak sorun önerisi

- **Nerede:** Alarm listesi ve yeni bir "Sorun grupları" görünümü.
- **Örnek:** Onlarca sipariş aynı fatura adımında takıldığında bunları tek bir olası ortak sorun olarak göstermek.
- **Ne yapar:**
  - Benzer hata mesajlarını sınıflandırır.
  - Aynı kaynak, akış veya zaman aralığındaki alarmları ilişkilendirir.
  - Ortak bir sorun olabileceğini belirtir.
  - Etkilenen işlemleri tek görünümde toplar.
  - Varsa önceki benzer olayın çözüm notlarını getirir.
- **Gereken ek veriler:** Kaynak sistem adı, yapılandırılmış hata kodları, bağlantı kimliği, ileride teknik log ve istek izleri.
  Bugünkü olay verileri başlangıç sağlar; kesin kök neden iddiası için bağlamın genişlemesi gerekir.
- **Sektör örneği:** Dynatrace ilişkili anomalileri tek sorun altında toplar. Doğruluğu kendi verimizle ölçmemiz gerekir.
- **Müşteriye faydası:** Tek tek alarm incelemek yerine ortak problemi araştırmak.
- **Zamanlama:** İlk alarm inceleme özelliğinden sonra.

### 1.3 Operasyona doğal dille soru sorma

- **Nerede:** Panelde "Operasyona sor" alanı.
- **Örnek sorular:**
  - "Bugün hangi işlemler fatura aşamasında gecikti?"
  - "Dün en çok hangi adım sorun çıkardı?"
  - "Bu siparişin başından beri ne olduğunu anlat."
  - "Son haftada tekrarlayan hatalar hangileri?"
  - "Açık alarmlardan hangileri hâlâ kimse tarafından görülmemiş?"
- **Nasıl çalışır:** Yapay zekâ soruyu mevcut filtrelere (işlem listesi, alarm listesi, raporlar) dönüştürür; sayıları ve
  kayıtları backend sorgular; model sonucu açıklar. Yanıtın yanında ilgili işlem listesi gösterilir. Model doğrudan SQL yazmaz.
- **Pazarlama:** "İş süreçlerinizi doğal dille sorgulayın."

### 1.4 Yapay zekâ destekli günlük operasyon özeti

- **Nerede:** Ana panel ve günlük/haftalık özet e-postaları.
- **Ne üretir:** Dönemin önemli sorunları, en çok etkilenen akışlar, tekrarlayan hatalar, çözülen ve açık kalan sorunlar,
  önceki dönemle dikkat çeken farklar, incelenmesi önerilen konular.
- Sayılar mevcut rapor hesaplarından (`PostgresReportQueries`) gelir; model bunları okunabilir bir değerlendirmeye çevirir.
- **Zamanlama:** Alarm inceleme ile aynı altyapıyı paylaşır; ilk yapay zekâ paketine dahil edilmesi önerilir.

### 1.5 Gecikme riski tahmini — daha sonra

- **Nerede:** Devam eden işlem listesi ve detayı. Süre dolmadan "bu işlemin gecikme riski yükseliyor" demek.
- **Veriler:** Akış ve adım bazında geçmiş süreler, gün ve saat etkisi, kaynak sistemin son davranışı, kuyruk yoğunluğu,
  yakın zamandaki hata ve gecikme örüntüleri.
- **Zamanlama:** Yeterli gerçek veri toplandıktan sonra. Önce basit istatistiksel yöntemlerle karşılaştırılır; "sorunları
  önceden tahmin ediyor" iddiası ölçülmüş sonuçlara dayandırılır. Python mutabakat/analiz servisinde yürür.

### 1.6 Entegrasyon kurulumunu kolaylaştıran yardımcı

- **Nerede:** Kaynak bağlantısı ve akış oluşturma ekranları.
- **Ne yapar:** Örnek olay verisindeki alanları açıklar, kaynak alanlarla IntegraOps alanları arasında eşleme önerir, iş süreci
  açıklamasından akış taslağı çıkarır, eksik işlem kimliği veya belirsiz zaman alanını gösterir, bağlantı hatasını açıklar.
- **Zamanlama:** Hazır bağlayıcılar geliştirilirken. Öneriler kullanıcı doğrulayarak yayınlanır.

### 1.7 Önerilen ilk yapay zekâ paketi

1. Alarmın anlaşılır özeti.
2. Kanıtlara bağlı inceleme önerileri.
3. Günlük operasyon özeti.
4. "Faydalı / hatalı / eksik" geri bildirimi.

Pilot sırasında inceleme süresi, önerilerin faydası ve dayanaksız açıklama oranı ölçülür. Böylece "yapay zekâ destekli"
ifadesinin arkasında gösterilebilir bir özellik ve gerçek kullanıcı sonucu bulunur.

---

## 2. Teknolojilerin görevi

| Teknoloji | Görev |
|---|---|
| **.NET** | Yetki kontrolü, kanıtın toplanması, sayıların hesaplanması, model çağrısı, çıktının denetimi ve kaydı |
| **Dil modeli hizmeti** | Alarm açıklaması, inceleme önerileri, soru yorumlama ve özetleme |
| **Next.js** | Sonucu, dayanak kayıtları ve kullanıcı geri bildirimini gösterme |
| **PostgreSQL** | Analiz geçmişi, modele verilen kanıt ve değerlendirme sonuçları |
| **Python (ileride)** | Gecikme tahmini, anomali analizi, kapsamlı dönemsel hesaplamalar |

İlk özellik için Python veya Go gerekmez; model .NET üzerinden çağrılır. İşlem durumu ve alarm kuralları çekirdekte kalır.

---

## 3. Koddaki bağlantı noktaları

| Özellik | Backend | Panel | Durum |
|---|---|---|---|
| Alarm inceleme | `PostgresAiStore.AnalyzeAlarm`, `AiEndpoints` | `components/ai-analysis.tsx`, `app/alarms/[id]/page.tsx` | ✅ Altyapı |
| Geri bildirim | `PostgresAiStore.Feedback`, `ai_feedback` tablosu | Alarm sayfasındaki Faydalı / Hatalı / Eksik düğmeleri | ✅ Altyapı |
| Plan eklentisi | `Plans.AiDefaults`, `organization_limits.ai_enabled`, `PlanInput.AiEnabled` | `/admin` plan formu, organizasyon sayfası | ✅ Altyapı |
| İşlem detayından inceleme | Aynı uç; işlem sayfası açık alarmı çağırır | `app/transactions/[flow]/[id]` | ⏳ |
| Sorun grupları | `PostgresPilotQueries.Alarms` + yeni gruplama sorgusu | `app/alarms` yeni görünüm | ⏳ |
| Operasyona sor | `ITransactionQueries.ListAsync`, `PostgresPilotQueries.Alarms`, `PostgresReportQueries` filtre olarak araç çağrısı | Panel üst alanı | ⏳ |
| Günlük özet | `DigestScheduler` (worker) + `PostgresReportQueries` → `ai_analyses` (`subject_kind='digest'`) | Ana panel, özet e-postası | ⏳ Tablo hazır |
| Gecikme riski | `reconciler/` (Python) + `transaction_step_stats` | İşlem listesi | ⏳ Veri birikimi gerekli |
| Entegrasyon yardımcısı | `PostgresConnectionStore`, `EventMapper` | `app/connections`, `app/flows/new` | ⏳ |

---

## 4. Kurulan altyapı

### Model bağımsız katman — `backend/src/IntegraOps.Infrastructure/AiAssistant.cs`

- `IAiModelClient`: Tek görev, bir istemi (sistem + kullanıcı metni) metne çevirmek. Her sağlayıcı bunu uygular.
- `DisabledAiClient`: Varsayılan. Bütün yapay zekâ uçları **503 "Yapay zekâ henüz yapılandırılmadı"** döner; panel bölümü bilgi notu gösterir.
- `FakeAiClient`: Yalnızca yerel doğrulama. Model çağırmaz, kanıt listesinden sabit bir yanıt üretir.
- `AlarmInsightPrompt`: Sistem istemi (`alarm-v1`) ve çıktı denetimi:
  - JSON dışındaki metin atılır, özet zorunludur.
  - Neden sınıfı `delay`, `source_error`, `notification`, `unknown` dışındaysa `unknown` olur.
  - Bulgulardaki kanıt kimlikleri modele verilen listede yoksa atılır; kanıtı kalmayan bulgu **"Kanıta bağlanmadı"** olarak işaretlenir.
  - Listeler en fazla 8 öğe, metinler kırpılır.
  - Kanıttaki hata mesajları ve notlar veri olarak işaretlenir; içlerindeki talimatların uygulanmaması istenir.

### Kanıt toplama — `PostgresAiStore.AlarmEvidence`

Model yalnızca kısa kimlikleri olan şu kayıtları görür:

| Kimlik | Tür | İçerik |
|---|---|---|
| `A1` | alarm | Tür, adım, hata (500 karakter), beklenen son zaman, açılış/kapanış, görüldü/atandı |
| `T1` | transaction | Akış, işlem numarası, durum, grup, sürüm, takvim, kabul/işlenen olay sayısı, gecikme durumu |
| `S1…` | step | Beklenen adımlar: aşama, tür, süre sınırı, gözlem, hata |
| `E1…` | event | Son 30 olay: adım, durum, oluşma/alınma zamanı, bildirim gecikmesi, hata |
| `D1…` | delivery | Bildirim gönderimleri: kanal türü, durum, deneme, son hata (hedef adresler gönderilmez) |
| `N1…` | note | Son 5 operatör notu |
| `B1` | similar_alarms | Aynı akış ve adımda ±6 saatteki alarm sayısı, türleri, açık kalanlar, 5 örnek |

Kullanıcı adları, e-postalar, anahtarlar ve bildirim hedefleri kanıta girmez.

### Kayıt — migration `013_ai.sql`

- `ai_analyses`: proje, konu (`alarm` / `digest`), isteyen kullanıcı, sağlayıcı, model, istem sürümü, durum, **modele verilen kanıt**,
  denetlenmiş sonuç, hata sınıfı, jeton sayıları, süre.
- `ai_feedback`: kullanıcı başına tek değerlendirme (`useful`, `wrong`, `incomplete`) ve isteğe bağlı açıklama.
- İşlem saklama süresi dolduğunda ilgili alarm analizleri de silinir (`RetentionSweeper`); organizasyon silmede bütün analizler temizlenir.
- Her inceleme `audit_log` içine `ai.alarm_analysis` olarak yazılır.

### API uçları

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET /api/v1/ai/status` | Proje üyeleri | `enabled` (kullanılabilir), `configured` (model var), `planEnabled` (planda dahil), sağlayıcı, model, günlük sınır ve kullanım |
| `POST /api/v1/alarms/{id}/ai-analyses` | Sahip, operatör (oturumla) | İncelemeyi başlatır, sonucu döner (201) |
| `GET /api/v1/alarms/{id}/ai-analyses` | Proje üyeleri | Son 10 inceleme, kanıt ve kullanıcının değerlendirmesi |
| `PUT /api/v1/ai/analyses/{id}/feedback` | Proje üyeleri (oturumla) | Faydalı / hatalı / eksik geri bildirimi |

API anahtarları yapay zekâ uçlarını kullanamaz. Başka projenin alarmı veya analizi 404 döner. Model yapılandırılmamışsa 503,
planda asistan yoksa 403, günlük sınır dolduysa 429 döner; hiçbiri temel işlevleri etkilemez.

### Sınırlar ve ayarlar

Uygulama sınırları bizim deneme korkuluklarımızdır; sağlayıcının kotası değildir. Compose dosyaları `.env` içindeki `AI_*`
değişkenlerini backend ortamındaki `Ai__*` anahtarlarına aktarır; .NET `.env` dosyasını kendiliğinden okumaz.

| Ortam değişkeni | Varsayılan | Açıklama |
|---|---|---|
| `AI_PROVIDER` → `Ai__Provider` | `none` | `none` kapalı, `fake` yerel doğrulama, `gemini` Google Gemini Developer API |
| `AI_ENABLED` → `Ai__Enabled` | `true` | `false` sağlayıcı yapılandırılmış olsa da asistanı kapalı tutar |
| `AI_MODEL` → `Ai__Model` | gemini için `gemini-3.5-flash-lite` | Model kimliği |
| `GEMINI_API_KEY` (veya `AI_API_KEY` → `Ai__ApiKey`) | — | Sağlayıcı anahtarı; yalnızca backend ortamında, panele/Git'e girmez |
| `AI_TEST_MODE` → `Ai__TestMode` | `false` | `true`: yalnızca `is_test` (TEST- ile başlayan) işlemler incelenir; production ortamında asistan açılmaz |
| `AI_FREE_TIER_ONLY` → `Ai__FreeTierOnly` | `true` | Yalnızca ücretsiz katmandaki modele izin verir (`Ai__FreeTierModels`, varsayılan `gemini-3.5-flash-lite`) |
| `AI_FREE_TIER_CONFIRMED` → `Ai__FreeTierConfirmed` | `false` | Yöneticinin AI Studio'daki Free Tier kontrolünü kaydeder; `false` iken sağlayıcıya çağrı yok. Billing garantisi değildir |
| `AI_DAILY_LIMIT` → `Ai__DailyLimitPerProject` (`Ai__MaxRequestsPer24Hours` da kabul edilir) | 200 | Proje başına son 24 saatte çağrı girişimi sınırı; başarısız çağrı da sayılır (aşımda 429) |
| `AI_MAX_OUTPUT_TOKENS` → `Ai__MaxOutputTokens` | 1200 | Yanıt uzunluğu sınırı |
| `AI_TIMEOUT_SECONDS` → `Ai__TimeoutSeconds` | 60 | Model çağrısı zaman aşımı (aşımda 502, başarısız kayıt) |
| `AI_MAX_CONCURRENT_REQUESTS` → `Ai__MaxConcurrentRequests` | 4 | Proje başına aynı anda süren çağrı |
| `AI_MIN_SECONDS_BETWEEN_REQUESTS` → `Ai__MinSecondsBetweenRequests` | 0 | Proje başına iki çağrı arası asgari süre (aşımda 429, bekleme süresi mesajda) |
| `AI_MAX_REQUEST_BYTES` → `Ai__MaxRequestBytes` | 60000 | Sağlayıcıya giden JSON gövdesi sınırı; aşımda çağrı yapılmaz |
| `AI_CACHE_TTL_SECONDS` → `Ai__CacheTtlSeconds` | 0 | Aynı, değişmemiş bağlam için son tamamlanmış yanıtın yeniden kullanım süresi |

Kota denetimi PostgreSQL'de yapılır: proje kilidi altında sınırlar kontrol edilir, `pending` satır yazılır, sağlayıcı çağrısı
işlem dışında yapılır ve satır `completed`/`failed` olarak kapatılır. Böylece sayaç yeniden başlatmada veya ikinci API
kopyasında sıfırlanmaz; aynı bağlama çift tıklama 409 ile tek çağrıya iner. Her analiz `context_hash` taşır: bağlam
(kanıtlar, sağlayıcı, model, istem sürümü) değiştiğinde eski açıklama panelde **eski** olarak işaretlenir.

### Doğrulama

- `dotnet test --filter AiTests`: çıktı denetimi (kanıt kimlikleri, neden sınıfı, sınırlar, bozuk yanıt) ve Gemini adaptörü
  (istek sözleşmesi, engellenen/yarım/boş yanıtın reddi, anahtarın başlıkta gitmesi, 429'da tekrar yok, yönlendirme izlenmez, boyut sınırı).
- `scripts/verify_ai.py`: yığın `AI_PROVIDER=fake` ile açıkken uçtan uca kontroller (alarm ve işlem incelemesi, plan eklentisi dahil).
- `scripts/verify_gemini_free.py`: .NET'ten bağımsız, tek dosyalık Gemini bağlantı sınaması; anahtarı diske yazmaz, tek istek yapar.

---

## 5. Gemini ücretsiz deneme profili

Ayrıntılı gerekçe ve Google hesabı kurulumu: `IntegraOps_Gemini_Ucretsiz_Test_Rehberi.md`. Kodda karşılığı:

| Bileşen | Yeri |
|---|---|
| Sağlayıcı arayüzü | `IAiModelClient` (`AiAssistant.cs`); `Reason` alanı asistanın neden kapalı olduğunu panele taşır |
| Gemini uyarlayıcısı | `GeminiAiClient`: `generateContent`, JSON kipi, tek aday, `thinkingLevel=MINIMAL`, anahtar `x-goog-api-key` başlığında, yönlendirme yok, yanıt 64 KB sınırı, düşünce parçaları atılır, `finishReason=STOP` şartı |
| Yapılandırma kapıları | `AiSetup.Gemini`: anahtar yoksa, Free Tier onayı yoksa, model ücretsiz listede değilse veya deneme profili production'daysa asistan **kapalı** kalır; API çalışmaya devam eder |
| Sentetik veri kuralı | `PostgresAiStore.Analyze`: `Ai__TestMode=true` iken `transactions.is_test=false` olan işlem/alarm için 403, sağlayıcıya çağrı yok |
| Kota ve önbellek | `PostgresAiStore.Analyze` rezervasyon bloğu; migration `016_ai_gemini.sql` (`pending` durumu, `context_hash`) |
| İşlem incelemesi | `GET/POST /api/v1/flows/{flow}/transactions/{transactionId}/ai-analyses`; panelde işlem detayında ayrı "Yapay zekâ ile inceleme" alanı |
| Bağlantı betiği | `scripts/verify_gemini_free.py` (rehber 6. madde) |

Açma sırası (yerel): `.env` içine `AI_PROVIDER=gemini`, `GEMINI_API_KEY=...`, rehberdeki sınırlar; AI Studio'da Free Tier kontrolünden
sonra `AI_FREE_TIER_CONFIRMED=true`; `docker compose up -d --build migrate api`. Panelde `TEST-` ile başlayan bir işlemi açıp
"Yapay zekâ ile incele" düğmesine basın. Kapalı durumda panel nedenini yazar.

Hata davranışı: 429 kota → mesaj gösterilir, otomatik tekrar ve ücretli geçiş yok; 401/403 → anahtar/proje kontrolü;
404 → model adı/erişim kontrolü, alternatif model seçilmez; ağ/zaman aşımı → yalnızca AI alanı hata gösterir.

Sonraki adımlar: gerçek alarmlarla istem ayarı (`prompt_version` artırılır), pilot ölçümleri (süre, faydalı oranı,
"Kanıta bağlanmadı" oranı, jeton kullanımı) ve canlı kullanım için veri işleme koşullarının ayrıca değerlendirilmesi.
