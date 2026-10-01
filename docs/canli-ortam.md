# Canlı ortam işletim rehberi

Bu rehber IntegraOps'un tek sunucuda Docker Compose ile kurulmasını, sürüm yayınlamayı, geri dönüşü, yedeklemeyi ve sunucu kaybından sonra geri yüklemeyi anlatır. Bütün adımlar `scripts/live/` altındaki betiklerle yapılır. Aynı betikler `scripts/rehearse_live.py` ile yerelde uçtan uca sınanır.

## Yapı

| Bileşen | Görev |
|---|---|
| `compose.live.yml` | Yalnızca yayınlanmış imajları çalıştırır. Bir sürüm, bir etiketin api, worker ve panel imajlarıdır. |
| `.github/workflows/release.yml` | `vX.Y.Z` etiketi itilince üç imajı derler ve GHCR'a gönderir. |
| `scripts/live/deploy.sh` | Yayın öncesi yedek alır, migration çalıştırır, yeni sürümü başlatır ve hazır olmasını bekler. |
| `scripts/live/rollback.sh` | Şema değişmediyse önceki sürüme döner. |
| `scripts/live/backup.sh` | Veritabanı ve anahtar yedeği alır, doğrular, sunucu dışına kopyalar, ölçüm yazar. |
| `scripts/live/restore.sh` | Yedeği boş bir sunucuya geri yükler. |
| `deploy/systemd/` | Günlük yedek için systemd zamanlayıcısı. |

Sunucudaki kalıcı durum `LIVE_STATE_DIR` dizinindedir. Varsayılan `/var/lib/integraops`:

```text
/var/lib/integraops/
  backups/<ZAMAN>/          integraops.dump, protection-keys.tar.gz, manifest.json
  metrics/                  integraops_backup.prom (Prometheus okur)
  rclone/rclone.conf        sunucu dışı depo erişimi
  alertmanager.yml          .env.live'dan üretilir
  releases.log              yayın, geri dönüş ve geri yükleme geçmişi
```

## Sürüm ve kaynak kodu eşleşmesi

Her imaj `org.opencontainers.image.version` ve `org.opencontainers.image.revision` etiketlerini taşır. Çalışan sürüm şu adreslerden okunur:

```bash
curl https://API_HOST/health/version
# {"version":"v1.0.0","revision":"<40 karakterlik commit>"}
```

Aynı bilgi Prometheus'taki her IntegraOps ölçümünde `service_version` etiketi olarak bulunur. Yayın hattı imajları hem sürüm etiketiyle hem `sha-<commit>` etiketiyle yayınlar.

## İlk kurulum

Sunucuda Docker Engine, Docker Compose v2, Python 3 ve git gerekir.

1. DNS kayıtlarını oluşturun. `PANEL_HOST` ve `API_HOST` bu sunucunun IP adresini göstermelidir. Caddy sertifikayı ilk açılışta alır, bunun için 80 ve 443 portları açık olmalıdır.
2. Depoyu `/opt/integraops` altına alın ve yayınlanacak etiketi seçin:

   ```bash
   git clone https://github.com/SelimYildirim1/IntegraOps.git /opt/integraops
   cd /opt/integraops && git checkout v1.0.0
   ```

3. Ayar dosyasını oluşturun ve bütün gizli değerleri yeniden üretin:

   ```bash
   cp .env.live.example .env.live && chmod 600 .env.live
   python3 -c "import secrets;print(secrets.token_hex(32))"
   ```

   Gerçek SMTP bilgisi ve `OPS_ALERT_EMAIL` doldurulur. Müşteri alarm kanalları ile IntegraOps'un kendi sağlık alarmları farklı adreslere gider.
4. Sunucu dışı yedek deposunu tanımlayın. Depo bu sunucudan bağımsız bir sağlayıcıda olmalıdır. Örnek S3 uyumlu ayar:

   ```ini
   # /var/lib/integraops/rclone/rclone.conf  (chmod 600)
   [offsite]
   type = s3
   provider = Other
   endpoint = https://s3.example-provider.com
   access_key_id = ...
   secret_access_key = ...
   ```

   `.env.live` içinde `BACKUP_REMOTE=offsite:integraops-backups/prod` yazılır. Kovada nesne kilidi veya sürümleme açılması önerilir. Böylece sunucuyu ele geçiren biri yedekleri silemez.
5. İmaj deposuna erişim verin. GHCR paketleri özelse okuma yetkili bir jetonla giriş yapılır:

   ```bash
   echo "$GHCR_TOKEN" | docker login ghcr.io -u KULLANICI --password-stdin
   ```

6. Kurun:

   ```bash
   bash scripts/live/deploy.sh v1.0.0
   ```

7. İlk sahip hesabını kurun. `BOOTSTRAP_KEY` yalnızca bu adımda kullanılır:

   ```bash
   curl -X POST https://API_HOST/api/v1/bootstrap/projects -H "X-Bootstrap-Key: ..." \
     -H "Content-Type: application/json" -d '{"organizationName":"Şirket","projectName":"Pilot"}'
   curl -X POST https://API_HOST/api/v1/bootstrap/owner -H "X-Bootstrap-Key: ..." -H "X-API-Key: <yanıttaki apiKey>" \
     -H "Content-Type: application/json" -d '{"email":"...","name":"...","password":"..."}'
   ```

8. Günlük yedeği açın:

   ```bash
   sudo cp deploy/systemd/integraops-backup.* /etc/systemd/system/
   sudo systemctl daemon-reload && sudo systemctl enable --now integraops-backup.timer
   sudo systemctl start integraops-backup.service && journalctl -u integraops-backup -n 20
   ```

## Paylaşımlı sunucu

Sunucuda başka bir ürün kendi nginx'i ile 80 ve 443 portlarını kullanıyorsa Caddy başlatılmaz. IntegraOps ayrı bir Compose projesi, ayrı ağ ve ayrı veritabanı ile çalışır; diğer ürünle yalnızca alan adı üzerinden HTTPS ile konuşur.

1. `.env.live` içine şunlar eklenir. Portlar sunucuda boş olmalı ve yalnızca `127.0.0.1` üzerinde dinlenir:

   ```bash
   LIVE_COMPOSE_EXTRA=compose.shared-host.yml
   PANEL_PORT=4000
   API_PORT=4001
   GRAFANA_PORT=4002
   ```

   `compose.shared-host.yml` ayrıca her servise CPU ve bellek sınırı koyar. `API_REPLICAS` 1 kalmalıdır.
2. nginx sitesi `deploy/nginx/integraops.conf.template` dosyasından üretilir, `nginx -t` ile sınanır ve `systemctl reload nginx` ile yüklenir. Diğer ürünün site dosyalarına dokunulmaz.
3. Sertifika `certbot --nginx -d PANEL_HOST -d API_HOST` ile alınır.
4. Güvenlik duvarında yeni port açılmaz.

## Sürüm yayınlama

1. Ana dalda CI yeşil olduktan sonra etiket oluşturun:

   ```bash
   git tag -a v1.1.0 -m "v1.1.0" && git push origin v1.1.0
   ```

2. "IntegraOps release images" iş akışının bitmesini bekleyin.
3. Sunucuda:

   ```bash
   cd /opt/integraops && git fetch --tags && git checkout v1.1.0
   bash scripts/live/deploy.sh v1.1.0
   ```

Betik sırasıyla Alertmanager ayarını üretir, imajları çeker, yayın öncesi yedek alır, migration çalıştırır, servisleri yeniler ve `/health/version` yeni sürümü gösterene kadar bekler. Sonuç `releases.log` dosyasına şema sürümüyle birlikte yazılır.

## Önceki sürüme dönüş

```bash
bash scripts/live/rollback.sh          # bir önceki sürüm
bash scripts/live/rollback.sh v1.0.0   # belirli sürüm
```

Betik, dönülecek sürümün çalıştığı şema sürümünü `releases.log` dosyasından okur. Şema o zamandan beri değişmediyse imajları değiştirir ve hazır olmasını bekler. Şema değiştiyse durur, çünkü eski kod yeni şemayla güvenle çalışmayabilir.

### Şema değişen sürümden dönüş

Migration'lar yalnızca ileri yönlüdür. Yeni sürüm şemayı değiştirdiyse ve geri dönmek şartsa:

1. Yayın öncesi yedeğin adını `backups/` altında bulun. Bu yedek, yayından hemen önce alınmıştır.
2. Yayından sonra kabul edilen olaylar bu yedekte yoktur. Kaynak sistemler bu olayları kendi kuyruklarından tekrar gönderir, çünkü IntegraOps'a ulaşan her olay kaynakta kalıcıdır. Yine de hangi aralığın yeniden gönderileceği kaynak ekiplerle kararlaştırılır.
3. Mevcut diskleri silmeden önce ayrıca bir yedek alın: `bash scripts/live/backup.sh`.
4. Servisleri ve diskleri kaldırıp yayın öncesi yedeği geri yükleyin:

   ```bash
   docker compose -f compose.live.yml --env-file .env.live -p integraops-live down -v
   RESTORE_IMAGE_TAG=v1.0.0 bash scripts/live/restore.sh /var/lib/integraops/backups/<ZAMAN>
   ```

Bu yol veri kaybı riski taşıdığı için yalnızca sorumlu kişinin kararıyla uygulanır.

## Yedekleme

`backup.sh` her çalışmada şunları yapar:

1. `pg_dump` ile veritabanını ve API anahtar dizinini arşivler.
2. `pg_restore --list` ve arşiv listesiyle dosyaların okunabildiğini doğrular.
3. `manifest.json` içine sürüm, şema ve SHA-256 özetlerini yazar.
4. `BACKUP_REMOTE` deposuna kopyalar ve `rclone check` ile uzaktaki kopyayı doğrular.
5. Yerelde `BACKUP_KEEP_DAYS`, uzakta `BACKUP_REMOTE_KEEP_DAYS` günden eski yedekleri siler.
6. Başarılıysa `integraops_backup_last_success_timestamp_seconds` ölçümünü yazar.

`DEPLOYMENT_ENVIRONMENT=production` iken uzak depo tanımlı değilse yedek başarısız sayılır. Son başarılı yedek 26 saati geçerse `IntegraOpsBackupStale` alarmı açılır.

**Anahtar arşivi ile veritabanı yedeği birlikte saklanır.** Anahtarlar olmadan bildirim kanallarının hedefleri ve webhook sırları çözülemez.

## Sunucu kaybından sonra geri yükleme

Yeni sunucuda ilk kurulumun 1–5. adımları uygulanır. `.env.live` eski sunucudakiyle aynı gizli değerleri içermelidir. Bu dosyanın bir kopyası parola yöneticisinde saklanmalıdır. Ardından:

```bash
# Uzak depodaki yedekleri listeleyin
docker run --rm -v /var/lib/integraops/rclone:/config/rclone rclone/rclone:1.71.0 lsd offsite:integraops-backups/prod
# En yenisini geri yükleyin
bash scripts/live/restore.sh remote:20260922T023000Z
```

Betik şu adımları uygular:

1. Yedeği indirir ve SHA-256 özetlerini doğrular.
2. Yedekteki sürümün imajlarını çeker.
3. Hedef veritabanı boş değilse durur.
4. Veritabanını ve anahtarları geri yükler.
5. İşlenmemiş olayları boş RabbitMQ için yeniden kuyruğa hazırlar.
6. Servisleri başlatır ve sürümün hazır olmasını bekler.

Sonra DNS kayıtları yeni sunucuya yönlendirilir. Kaynak sistemler bu sürede olayları kendi kuyruklarında bekletir ve bağlantı dönünce gönderir.

Geri yüklemeden sonra kontrol edilecekler:

- Panelde giriş, son işlemler ve alarm geçmişi.
- Ayarlar ekranında bildirim kanallarının hedefleri okunuyor.
- Sistem ekranında worker yaşam sinyali güncel ve bekleyen sayılar azalıyor.
- Grafana'da "IntegraOps sistem sağlığı" panosu veri alıyor.

## Yerel prova

Sunucu olmadan bütün döngü şu komutla sınanır:

```bash
python3 scripts/rehearse_live.py
```

Prova iki sürüm etiketi derler. Canlı Compose dosyasını ayrı bir proje adıyla Caddy HTTPS arkasında kurar. Yayın, geri dönüş, sunucu dışı yedek ve bütün diskleri silip geri yükleme adımlarını çalıştırır. Sunucu dışı depo olarak yerel bir S3 sunucusu kullanır. Sonuç `.local/live-rehearsal/<KİMLİK>/report.json` dosyasına yazılır.

Prova gerçek sunucuyu, DNS'i, genel sertifika sağlayıcısını, gerçek SMTP'yi ve gerçek sunucu dışı depoyu kanıtlamaz. Bunlar canlı kurulumda ayrıca doğrulanır.
