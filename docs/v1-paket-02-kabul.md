# V1 Paket 02 — Kurtarma kabul kaydı

Tarih: 22 Eylül 2026. Prova kimliği: `e04c837883fa`. Kaynak Compose projesi: `integraops`.

## Otomatik kontroller

`python3 scripts/rehearse_restore.py run` 21 kontrolün tamamını geçti. Rapor durumu `automated_passed_manual_pending` oldu. Veri ve anahtar geri yükleme aşaması 13,18 saniye sürdü. Derleme ve giriş beklemesi dahil toplam süre 64,29 saniyedir. Bu değerler RTO/RPO taahhüdü değildir.

Aynı gün önce yapılan iki prova başarısız oldu ve kök nedenleri düzeltildi:

| Prova | Sorun | Düzeltme |
|---|---|---|
| `91994ef133d4` | Dış ağı kapalı prova ağında port yayımlanmadığı için API hazırlık kontrolü zaman aşımına uğradı | API ve panel için ayrı `host_access` ağı eklendi, doğrulayıcı iki ağlı yapıyı zorunlu kıldı |
| `babe508e8618` | RabbitMQ sağlık kontrolü sunucudan önce çalışıp Erlang cookie dosyasını root sahipliğinde oluşturdu | Sağlık kontrolü cookie dosyası oluşmadan tanı komutunu çalıştırmıyor |

## Tarayıcı kabulü

Ayrı ve kalıcı olmayan bir tarayıcı profiliyle (Playwright, Chromium) yapıldı.

| Rehber adımı | Sonuç |
|---|---|
| Giriş formundan mevcut sahip hesabıyla giriş | Geçti; proje listesine yönlendirdi |
| Pilot proje ve yedekteki akışlar | Geçti; 6 akış listelendi |
| Yedekteki `ORDER-10092` işlemi | Geçti; 5 adım tamamlanmış, 6 bildirim kabul edildi ve işlendi |
| `RESTORE-QUEUE-…` ve `RESTORE-ALARM-…` işlemleri | Geçti; ikisi de tamamlandı |
| Hata alarmı | Geçti; açıldı ve 5 saniye sonra adım tamamlanınca kapandı |
| `RESTORE-DELAY-…` gecikme işlemi ve alarmı | Geçti; süre aşımı alarmı açıldı, geç tamamlanmayla kapandı |
| Ayarlarda yerel e-posta ve webhook kanalları | Geçti; iki kanal eski anahtarlarla çözülmüş hedefleriyle etkin |
| Sistem ekranında worker yaşam sinyali | Geçti; sinyal güncel, bekleyen iş sıfır |
| Prova Mailpit | Geçti; 2 açılış ve 2 kapanış e-postası |
| Prova webhook alıcısı | Geçti; imzası doğrulanmış 4 kayıt |
| Çıkış | Geçti; korumalı sayfa giriş ekranına yönlendirdi |
| Asıl kurulum | Geçti; API hazır, panel yanıt veriyor, 8 servis çalışıyor |

## Temizlik

`python3 scripts/rehearse_restore.py cleanup --run-id e04c837883fa` yalnızca prova konteynerlerini, ağlarını ve disklerini kaldırdı. Yedek ve rapor `.local/restore-checks/e04c837883fa/` altında korunuyor. Bu dizin hassas içerik taşır ve depoya eklenmez.

## Sonuç

Aşama 1'in bitiş ölçütü karşılandı. Yerel kurtarma kabulü tamamlandı. Başka sunucuya dönüş ve canlı yedekleme düzeni Aşama 3'te ele alınacak.
