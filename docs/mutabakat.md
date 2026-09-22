# Mutabakat servisi (Python)

`integraops-reconciler`, kaynak sistemdeki işlemleri IntegraOps kayıtlarıyla dönemsel olarak karşılaştıran bağımsız bir Python servisidir. Olay akışı yalnızca kendisine ulaşan bildirimleri bilir; kaynakta oluşup hiç gönderilmemiş bir işlem alarm üretemez. Mutabakat bu boşluğu kapatır.

## Ne bulur

| Uyuşmazlık | Anlamı |
|---|---|
| `missing_transaction` | Kaynakta var, IntegraOps'ta kayıt yok |
| `missing_step` | Kaynakta tamamlanan adım IntegraOps'a bildirilmemiş |
| `status_mismatch` | Kaynak tamamlandı/hatalı diyor, IntegraOps farklı durumda (yalnızca adım sorunu yoksa) |

## Nasıl çalışır

1. Karşılaştırma aralığı belirlenir. Bitiş `şimdi − GRACE`, başlangıç bir önceki koşunun bitişi `− OVERLAP` (ilk koşuda `bitiş − WINDOW`).
2. Kaynak sorgusu çalışır ve işlem başına adımlar toplanır.
3. IntegraOps'a `POST /api/v1/reconciliation/lookup` ile 500'lük partiler halinde sorulur.
4. `GRACE` içinde gerçekleşen kayıt ve adımlar atlanır; henüz yolda olabilirler.
5. Sonuç `POST /api/v1/reconciliation/runs` ile kaydedilir. API her uyuşmazlığı `(akış, işlem, tür, adım)` anahtarıyla tekilleştirir: ilk görülme **yeni**, sonraki koşular **tekrar**, aralıktaki eski kaydın artık bildirilmemesi **çözüldü** olur.
6. Yalnızca yeni uyuşmazlıklar projenin e-posta kanallarına bir kez özetlenir. Tekrarlar bildirim üretmez.

Koşu geçmişi ve son başarılı koşu panelde "Mutabakat" sekmesinde görünür.

## Kaynak sorgusu

`QUERY_FILE` içindeki SQL `$1` (aralık başı) ve `$2` (aralık sonu) parametrelerini alır ve şu sütunları döndürür:

| Sütun | Zorunlu | Anlamı |
|---|---|---|
| `transaction_id` | evet | IntegraOps'taki işlem kimliğiyle aynı |
| `occurred_at` | evet | İşlemin kaynakta başladığı zaman |
| `step_code` | hayır | Satır bir adımı temsil ediyorsa adım kodu |
| `step_status` | hayır | `completed` (varsayılan) ya da `failed` |
| `step_at` | hayır | Adımın zamanı; yoksa `occurred_at` |
| `status` | hayır | Kaynağın işlem durumu: `completed`, `failed`, `inProgress` |

Örnek `reconciler/orders.sql.example` içindedir. Sorgu yalnızca okuma yetkili bir veritabanı kullanıcısıyla çalıştırılmalıdır. `SOURCE=json` seçeneği aynı sütunlara sahip bir JSON dizisini okur; test ve dosya dışa aktarımları içindir.

## Kurulum

```bash
cd reconciler && python3 -m venv .venv && .venv/bin/pip install .
cp reconciler.conf.example reconciler.conf && chmod 600 reconciler.conf
.venv/bin/integraops-reconciler --config reconciler.conf --check
.venv/bin/integraops-reconciler --config reconciler.conf          # tek koşu (cron / systemd timer)
.venv/bin/integraops-reconciler --config reconciler.conf --loop   # INTERVAL aralığıyla sürekli
```

Konteyner: `deploy/docker/Dockerfile.reconciler`. Sürüm etiketi itildiğinde yayın hattı `reconciler` imajını da yayınlar.

```bash
docker run -d --name integraops-reconciler -v /etc/integraops-reconciler:/etc/integraops-reconciler:ro \
  ghcr.io/selimyildirim1/integraops/reconciler:v1.3.0
```

Kullanılan anahtar projenin "Bildirim gönderir" anahtarıdır; koşu kaydı için gereklidir. Yalnızca okuma anahtarı toplu sorgu yapabilir, koşu kaydedemez.

## Ayarlar

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| `WINDOW` | 24h | İlk koşuda ve en fazla geriye bakılan aralık (5m–31d) |
| `OVERLAP` | 1h | Önceki koşunun bitişinden ne kadar geriye yeniden bakılacağı |
| `GRACE` | 30m | Bu süreden yeni kayıtlar uyuşmazlık sayılmaz (en fazla 7d) |
| `INTERVAL` | 15m | `--loop` modunda koşu aralığı |

`GRACE`, kaynak sistemin olayı ürettikten sonra IntegraOps'a ulaştırma süresinin üstünde seçilmelidir. Ajan kesintisi uzun sürerse uyuşmazlıklar geçici olarak açılır ve olaylar ulaşınca kendiliğinden çözülür.

## Doğrulama

```bash
cd reconciler && .venv/bin/python -m unittest discover -s tests   # 7 birim testi
python3 scripts/verify_reconciler.py                              # yerel yığın; 18 kontrol
```

Yerel doğrulama bir kaynak tablosu oluşturur; hiç bildirilmemiş işlem ve eksik adım bulunur, taze kayıt beklenir, ikinci koşu uyuşmazlıkları çoğaltmaz, geç gelen adım uyuşmazlığı çözer ve e-posta bir kez gönderilir.
