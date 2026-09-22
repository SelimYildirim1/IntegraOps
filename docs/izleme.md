# Sistem sağlığı izleme

IntegraOps iş süreçlerindeki sorunları izler. Bu belge, ürünün kendi arızalarının nasıl izlendiğini anlatır. Amaç, "worker açık görünüyor ama olaylar ilerlemiyor" gibi durumların otomatik fark edilmesi ve ekibe bildirilmesidir.

## Yapı

| Bileşen | Görev |
|---|---|
| OpenTelemetry (.NET SDK) | API ve worker ölçüm ve istek izi üretir, OTLP/HTTP ile gönderir. |
| Prometheus | OTLP ile gelen ölçümleri saklar. RabbitMQ, node-exporter ve Alertmanager'ı kazır. Alarm kurallarını değerlendirir. |
| Alertmanager | Alarmları gruplar ve e-posta ya da webhook ile ekibe iletir. |
| Grafana | "IntegraOps sistem sağlığı" panosunu gösterir. |
| node-exporter | Disk kullanımı ve yedek betiğinin yazdığı son yedek zamanı. |
| Jaeger | Yalnızca yerel ortamda istek izlerini gösterir. |

Uygulama ölçümü yalnızca `Telemetry__MetricsEndpoint` ayarlıysa gönderir. İz gönderimi `Telemetry__TracesEndpoint` ile açılır. Ayar yoksa uygulama eskisi gibi çalışır.

**Veritabanına dayalı ölçümleri API üretir.** Worker durduğunda bu ölçümler gelmeye devam eder ve bekleyen işin büyüdüğünü gösterir. Ölçümler projeler genelinde toplamdır. Proje, işlem veya kullanıcı kimliği ölçümlere yazılmaz.

## Ölçümler

| İhtiyaç | Prometheus ölçümü | Üreten |
|---|---|---|
| Olay kabulünden işlenmesine kadar geçen süre | `integraops_events_processing_duration_seconds` (histogram) | worker |
| Kabul ve işleme hızı | `integraops_events_accepted_total`, `integraops_events_processed_total{outcome}` | API, worker |
| Outbox'ta bekleyen en eski olayın yaşı | `integraops_outbox_oldest_age_seconds`, `integraops_outbox_pending` | API |
| İşlenmemiş en eski olay | `integraops_events_oldest_unprocessed_age_seconds`, `integraops_events_unprocessed` | API |
| Kuyruk büyüklüğü ve tüketici sayısı | `rabbitmq_detailed_queue_messages_ready`, `rabbitmq_detailed_queue_consumers` | RabbitMQ |
| Kuyruğa aktarım hızı | `integraops_outbox_published_total` | worker |
| Gecikme tarayıcısının son başarılı çalışması | `integraops_delay_scan_last_success_seconds`, `integraops_delay_scan_runs_total{outcome}` | worker |
| Worker yaşam sinyali | `integraops_worker_heartbeat_age_seconds` | API |
| Bildirim teslim başarısı ve tekrar denemeler | `integraops_notifications_deliveries_total{kind,outcome}`, `integraops_notifications_pending`, `integraops_notifications_dead` | worker, API |
| API hata oranı ve yanıt süreleri | `http_server_request_duration_seconds{http_route,http_response_status_code}` | API |
| PostgreSQL bağlantıları | `integraops_db_connections`, `integraops_db_max_connections` | API |
| Veri büyümesi | `integraops_db_size_bytes`, `integraops_db_table_rows{table}` | API |
| Disk kullanımı | `node_filesystem_avail_bytes`, `node_filesystem_size_bytes` | node-exporter |
| Son başarılı yedek | `integraops_backup_last_success_timestamp_seconds`, `integraops_backup_last_size_bytes` | yedek betiği |

Her IntegraOps ölçümü `service_version` ve `deployment_environment_name` etiketlerini taşır.

## Alarmlar

Kurallar `deploy/monitoring/alerts.yml` dosyasındadır. Eşikler pilot için başlangıç değerleridir ve gerçek trafikle ayarlanır.

| Alarm | Koşul | Önem |
|---|---|---|
| IntegraOpsEventsNotProgressing | En eski işlenmemiş olay 2 dakikadan eski, 2 dakika boyunca | kritik |
| IntegraOpsOutboxBacklog | Outbox'taki en eski kayıt 1 dakikadan eski, 2 dakika boyunca | uyarı |
| IntegraOpsWorkerHeartbeatStale | Yaşam sinyali 60 saniyeden eski | kritik |
| IntegraOpsDelayScannerStale | Gecikme taraması 2 dakikadır başarılı değil | kritik |
| IntegraOpsHealthMetricsMissing | API sağlık ölçümü göndermiyor | kritik |
| IntegraOpsNotificationDeliveryStopped | Son 15 dakikada denemesi duran bildirim var | uyarı |
| IntegraOpsNotificationRetriesHigh | Bir kanal türünde denemelerin yarısından fazlası tekrar | uyarı |
| IntegraOpsApiErrorRate | 5xx oranı %5 üzerinde, 5 dakika | uyarı |
| IntegraOpsApiLatencyHigh | p95 yanıt süresi 1 saniye üzerinde, 10 dakika | uyarı |
| RabbitMQDown, IntegraOpsQueueNoConsumer | Broker erişilemiyor veya kuyruğun tüketicisi yok | kritik |
| IntegraOpsQueueBacklog | Kuyrukta 1000'den fazla hazır mesaj, 5 dakika | uyarı |
| IntegraOpsDeadLetterMessages | İnceleme kuyruğunda mesaj var | uyarı |
| PostgresConnectionsHigh | Bağlantıların %80'i kullanımda | uyarı |
| HostDiskSpaceLow | Boş disk %15'in altında | uyarı |
| IntegraOpsBackupStale | Son başarılı yedek 26 saatten eski | kritik |
| IntegraOpsBackupNeverRecorded | Sunucuda yedek kaydı yok | uyarı |

## Yerel kullanım

```bash
python3 scripts/prepare_v1.py          # GRAFANA_ADMIN_PASSWORD ve .local/metrics oluşturur
docker compose -f docker-compose.yml -f compose.monitoring.yml up --build -d
bash scripts/backup_local.sh           # son yedek ölçümünü üretir
```

| Adres | Servis |
|---|---|
| http://127.0.0.1:3001 | Grafana. Kullanıcı `admin`, parola `.env` içindeki `GRAFANA_ADMIN_PASSWORD` |
| http://127.0.0.1:9090 | Prometheus |
| http://127.0.0.1:9093 | Alertmanager |
| http://127.0.0.1:16686 | Jaeger istek izleri |
| http://127.0.0.1:8025 | Mailpit; yerel alarm e-postaları buraya düşer |

Doğrulama:

```bash
python3 scripts/verify_monitoring.py            # yığın, ölçümler, pano
python3 scripts/verify_monitoring.py --faults   # RabbitMQ'yu durdurur, alarmı ve e-postayı bekler
```

`--faults` senaryosu yaklaşık 8 dakika sürer. RabbitMQ durdurulur, worker çalışmaya devam eder. Yaşam sinyali güncel kalırken "olaylar ilerlemiyor" alarmı açılır ve e-postası gönderilir. RabbitMQ dönünce bekleyen olay işlenir ve alarm kendiliğinden kapanır.

## Canlı ortam

`compose.live.yml` aynı izleme yığınını içerir. Grafana yalnızca sunucunun yerel arayüzünde açılır. Erişim SSH tüneliyle yapılır:

```bash
ssh -L 3001:127.0.0.1:3001 sunucu
```

Alertmanager ayarı `.env.live` içindeki `OPS_ALERT_EMAIL` ve isteğe bağlı `OPS_ALERT_WEBHOOK_URL` değerlerinden `scripts/live/render_alertmanager.py` ile üretilir. Kritik alarmlar saatte bir, uyarılar dört saatte bir tekrarlanır.

İzleme yığını da aynı sunucuda çalıştığı için sunucunun tamamen kapanmasını kendisi bildiremez. Pilot için dışarıdan basit bir erişilebilirlik kontrolü önerilir. Örneğin bir çalışma süresi izleme servisi `https://API_HOST/health/ready` adresini dakikada bir kontrol eder.
