# Ölçek: kapasite ölçümü ve büyüme adımları (Aşama 14)

Ölçek işleri ölçülen ihtiyaca göre yapılır. Bu belge kapasitenin nasıl ölçüleceğini, 22 Eylül 2026 temel değerlerini, hangi eşikte hangi adımın alınacağını ve şimdiden hazır olan araçları anlatır.

## Ölçüm aracı

```bash
python3 scripts/load_test.py --transactions 2000 --concurrency 32 --json sonuc.json
```

Betik yalıtılmış bir test projesi açar, iki adımlı bir akış tanımlar, her işlem için iki olay gönderir (`TEST-` önekli, test verisi sayılır) ve şunları ölçer:

| Ölçüm | Anlamı |
|---|---|
| Kabul hızı (olay/sn) ve gecikme p50/p95/p99 | API + PostgreSQL yazma yolu (`POST /events`) |
| Uçtan uca süre ve hız | Outbox yayını → RabbitMQ → worker işleme → işlem tamamlandı; panelde görünme süresi |
| Bekleyen outbox ve işlenmemiş olay | Kuyruk birikimi; test sonunda 0 olmalı |
| Tablo büyümesi | Olay başına disk maliyeti; saklama ve bölümleme planı için |

Ölçüm sonucu 10 dakikada tamamlanmayan işlem varsa betik hata koduyla çıkar. CI her çalışmada 100 işlemlik bir duman testi koşar; kapasite ölçümü için betik canlıya benzer bir sunucuda, üretim yükü dışında koşulmalıdır. Sahibin organizasyonu deneme planındaysa günlük olay sınırı (5.000) ölçümü keser; ölçüm için plan yükseltilir.

## Temel ölçüm (22 Eylül 2026, geliştirme makinesi)

Ortam: Docker Desktop (VM 8 vCPU paylaşımlı, 2 GB bellek), PostgreSQL 17, RabbitMQ 4, API, worker ve panel aynı VM'de. Veritabanı 58 MB, 17 bin işlem. Sayılar bu ortama aittir; canlı sunucuda yeniden ölçülmelidir.

| Senaryo | Kabul | Gecikme p50 / p95 (ms) | Uçtan uca |
|---|---|---|---|
| 500 işlem (1.000 olay), 16 eşzamanlı, 1 worker, tekli yayın | 490 olay/sn | 29 / 60 | 162 olay/sn |
| 2.000 işlem (4.000 olay), 32 eşzamanlı, 1 worker, tekli yayın | 461 olay/sn | 54 / 172 | 142 olay/sn |
| 2.000 işlem, 32 eşzamanlı, 2 worker, tekli yayın | 410 olay/sn | 66 / 167 | 190 olay/sn |
| 2.000 işlem, 32 eşzamanlı, 1 worker, **toplu yayın (50)** | 484–498 olay/sn | 53 / 150 | 83–199 olay/sn |
| 2.000 işlem, 32 eşzamanlı, 1 worker, toplu yayın, tüketici eşzamanlılığı 4 | 277 olay/sn | 89 / 240 | 121 olay/sn |
| 2.000 işlem, 32 eşzamanlı, 2 worker, toplu yayın | 397 olay/sn | 67 / 183 | 90 olay/sn |

Olay başına disk: yaklaşık 1,2 KB (`transactions` + `transaction_events` + `transaction_step_stats` + `transaction_delays`) artı yayına kadar 0,3 KB outbox. Günde 500 bin olay ≈ 600 MB/gün; saklama kuralları (`docs/v1-1.md`) tamamlanmış işlemleri siler, yayınlanmış outbox satırları 7 gün sonra silinir.

**Yorum:** Bu makinede darboğaz paylaşılan PostgreSQL ve VM CPU'sudur. Uçtan uca hız aynı ayarda 83 ile 199 olay/sn arasında oynadı; worker sayısını ya da tüketici eşzamanlılığını artırmak veritabanı çekişmesini artırdığı için yardımcı olmadı. Toplu outbox yayını en iyi çalışmalarda tekli yayına göre yaklaşık %40 daha hızlıydı. Günlük 500 bin olaylık işletme planı bu ortamda bile ortalama 6 olay/sn demektir; kapasite, kesinti sonrası biriken trafiği eritme hızıyla (yukarıdaki uçtan uca değerler) planlanır.

## Hazır ölçek araçları

| Araç | Nerede | Ne zaman |
|---|---|---|
| Toplu outbox yayını | `Outbox__BatchSize` (varsayılan 50, en çok 500) | Her zaman açık; RabbitMQ gecikmesi yüksekse artırılır |
| Tüketici eşzamanlılığı | `EventConsumer__Concurrency` (varsayılan 1), `EventConsumer__Prefetch` (32) | Worker CPU boşta ve veritabanı rahat iken; aynı işlemin olayları satır kilidiyle sıralanır |
| Worker kopyaları | `WORKER_REPLICAS` (canlı compose), yerelde `docker compose up -d --scale worker=2 worker` | Outbox, gecikme kontrolü, bildirim ve e-posta işleri `SKIP LOCKED` ile paylaşılır; özet, saklama ve kurumsal işler `JobLock` ile tek kopyada koşar |
| API kopyaları | `API_REPLICAS` (canlı compose; Caddy servis adını çözerek dağıtır) | Kabul gecikmesi p95 > 500 ms iken API CPU doluysa. Giriş hız sınırı kopya başınadır; dışa aktarım dosyaları paylaşılan `exports_data` biriminde |
| İndeksler ve temizlik | Migration 012 (`audit_log`, yayınlanmış outbox), `RetentionSweeper.SweepOutboxAsync` | Her zaman açık |

## Eşikler ve sıradaki adımlar

Aşağıdaki sinyaller Prometheus/Grafana'dan (`docs/izleme.md`) ve bu betikten izlenir. Her satır bir adımı tetikler; adımlar sırayla, ölçüm tekrarlanarak alınır.

| Sinyal | Eşik | Adım |
|---|---|---|
| `integraops_outbox_pending` sürekli artıyor, worker CPU dolu | 15 dakikadan uzun süre birikme | `WORKER_REPLICAS` +1; sonra `EventConsumer__Concurrency` 2–4 (yalnızca veritabanı rahatken) |
| Kabul gecikmesi p95 | > 500 ms, API CPU > %70 | `API_REPLICAS` +1 |
| PostgreSQL CPU / bekleyen kilitler | CPU > %70 ya da `pg_stat_activity` bekleyenleri sürekli > 5 | Yönetilen PostgreSQL'e ya da ayrı sunucuya taşıma (bağlantı dizesi değişir, başka değişiklik gerekmez); `shared_buffers`, `work_mem` ayarı; okuma ağırlıklı yükte okuma kopyası |
| `transaction_events` boyutu | > 50 GB ya da rapor sorguları > 2 sn | Önce saklama süresini kısaltma; sonra `transaction_events` ve `transaction_step_stats` için `received_at` bazlı aylık bölümleme (pg_partman); raporlar için ayrı depo en son |
| RabbitMQ kuyruk derinliği | > 100 bin ve tüketiciler doygun | Kuyruğu proje/kiracı bazında bölme (`Messaging.Queue` çoğullanır, tüketici bir kuyruk kümesine bağlanır) |
| Tek sunucu arızası kabul edilemez | Sözleşmede erişilebilirlik hedefi (örn. %99,9) | Yönetilen PostgreSQL (çoklu bölge, otomatik yedek), RabbitMQ 3 düğümlü küme ve quorum kuyruğu, API/worker iki sunucuda, Caddy yerine bulut yük dengeleyici |
| Servis sayısı ve dağıtım karmaşıklığı | > 3 sunucu ya da elle dağıtım hataları | Kubernetes/yönetilen konteyner platformu; imajlar hazır (`release.yml`), yapılandırma ortam değişkenleriyle |
| Aynı panel sorgularının tekrarlanması | API veritabanı okuma oranı > yazma ×10 | Redis önbelleği (özet ve rapor uçları için, kısa TTL) |

## Canlı sunucu için ölçüm prosedürü

1. Gerçek yükün dışında bir saatte `scripts/load_test.py --transactions 2000 --concurrency 32 --json` koşulur; sonuç bu belgeye tarihle eklenir.
2. Ölçüm sırasında Grafana'daki API gecikmesi, outbox bekleyen sayısı ve PostgreSQL CPU grafiği kaydedilir.
3. Beklenen günlük olay hacminin 10 katı bir kesinti birikimi (örneğin 8 saatlik kesinti) uçtan uca hızla bölünerek "eritme süresi" hesaplanır; 1 saatten uzunsa yukarıdaki ilk adım uygulanır.
4. Her ölçek adımından sonra ölçüm tekrarlanır; iyileşme yoksa adım geri alınır.

## Dil ve mimari notu

Dil çeşitlendirmek kapasite çözümü değildir. Mevcut yapı (API yazar, outbox yayınlar, worker işler) yatay büyümeye uygundur; darboğaz ölçümle bulunur ve yalnızca o katman büyütülür. Geçmiş veri yeterince biriktiğinde anomali tespiti ve olay özetleme ayrı bir Python servisi olarak eklenebilir; bunlar ürünün tamamlanması için şart değildir.
