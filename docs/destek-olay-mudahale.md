# Destek ve olay müdahale prosedürleri

Bu belge müşteri desteğinin ve üretim olaylarının nasıl ele alınacağını tanımlar. Teknik işletim adımları `docs/canli-ortam.md`, izleme ve uyarı kuralları `docs/izleme.md` içindedir.

## Destek kanalları ve seviyeler

| Seviye | Örnek | İlk yanıt | Çözüm hedefi |
|---|---|---|---|
| S1 · Kritik | Bildirim kabulü durdu (`/health/ready` başarısız, `/events` 5xx), veri kaybı şüphesi, güvenlik ihlali | 30 dakika | 4 saat |
| S2 · Yüksek | Alarmlar üretilmiyor ya da bildirimler gitmiyor, panel açılmıyor, SSO girişi çalışmıyor | 2 saat | 1 iş günü |
| S3 · Normal | Yanlış hesaplanan süre, rapor tutarsızlığı, eşleme şablonu sorunu, sınır aşımı soruları | 1 iş günü | 5 iş günü |
| S4 · Düşük | Özellik isteği, belge sorusu | 2 iş günü | planlanır |

Müşteri desteğe e-posta ile ulaşır; her istek organizasyon adı, proje, ilgili işlem kimliği (`transactionId`) ve zaman aralığıyla açılır. Destek ekibi organizasyona **sistem yöneticisi** olarak erişir; müşteri verisine erişim `security_events` ve `audit_log` kayıtlarında görünür ve yalnızca isteğin çözümü için yapılır.

## Sık gelen istekler

| İstek | Adım |
|---|---|
| Plan yükseltme / sınır artırma | `/admin` sayfasında plan ve sınır atanır; not alanına sözleşme numarası yazılır. Müşteriye organizasyon sayfasındaki yeni sınır gösterilir |
| "Bildirimlerimiz 429 alıyor" | Organizasyon sayfasında günlük kullanım kontrol edilir. Beklenen artışsa sınır yükseltilir; değilse kaynak sistemde tekrar gönderim döngüsü aranır (`Bağlantılar` sekmesi red sayısı ve son hata) |
| "Bildirimlerimiz 402 alıyor" | Deneme süresi dolmuş ya da hesap askıda. Plan atanır ya da silme isteği varsa müşteriyle teyit edilir |
| İki adımlı doğrulama cihazı kaybı | Kurtarma kodu istenir. Yoksa kimlik teyidi (kayıtlı e-postadan yazılı onay ve organizasyon sahibinin onayı) sonrası `UPDATE app_users SET mfa_enabled=false, mfa_secret_protected=NULL, mfa_recovery_hashes='{}'` ve bütün oturumların silinmesi; işlem destek kaydına yazılır |
| Sahip hesabına erişim kaybı | Parola sıfırlama bağlantısı (`/forgot`). Sahip ayrıldıysa başka bir sahip yeni sahip atar; tek sahipse şirket yetkilisinin yazılı onayıyla destek `organization_members` üzerinden sahip atar |
| SSO girişi çalışmıyor | Organizasyon güvenlik olaylarında `sso.failed` ayrıntısı (`token endpoint`, `id_token`, `claims`) okunur. Sağlayıcıda callback adresi ve `openid email profile` izinleri kontrol edilir. Zorunlu SSO kilitlenmişse sistem yöneticisi parola ile girip zorunluluğu kaldırır |
| Veri dışa aktarma | Müşteri sahibi kendisi ister; 7 gün içinde indirir. Destek gerekirse `data_exports` durumunu ve API günlüğündeki `ExportRunner` uyarılarını kontrol eder |
| Hesap silme | Müşteri sahibi kendisi ister. Destek yalnızca 7 günlük sürenin dolmasını izler; kalıcı silmeden sonra yedeklerin saklama süresi müşteriye bildirilir |

## Olay müdahale akışı

1. **Tespit:** Alertmanager uyarısı (`docs/izleme.md`), müşteri bildirimi ya da `health/ready` izlemesi. Olay kaydı açılır: başlangıç saati, etkilenen bileşen, etkilenen organizasyonlar.
2. **Sınıflandırma:** Yukarıdaki seviye tablosuna göre. S1 ve S2 için nöbetçi anında bilgilendirilir.
3. **İlk müdahale (S1):**
   - `scripts/live/status.sh` ile servis durumu, `docker compose logs --tail=200 api worker` ile son hatalar.
   - Kabul durduysa öncelik kaynak sistemlerin outbox'ta biriktirmesidir; ajan ve SDK'lar yeniden dener, veri kaybolmaz (`docs/entegrasyon-sozlesmesi.md`). Müşteriye "kabul geçici olarak durdu, gönderimler kuyrukta bekletilmeli" mesajı verilir.
   - Veritabanı sorunu: `docs/canli-ortam.md` geri yükleme provası adımları; RabbitMQ sorunu: broker yeniden başlatılır, outbox yayıncısı kaldığı yerden sürer.
   - Güvenlik ihlali şüphesi: etkilenen anahtarlar iptal edilir (`/settings` ya da SQL), etkilenen kullanıcıların oturumları silinir (`DELETE FROM user_sessions`), `security_events` ve `audit_log` dışa aktarılıp saklanır, müşteri 24 saat içinde bilgilendirilir.
4. **İletişim:** S1 ve S2 olaylarında etkilenen organizasyon sahiplerine ilk saatte durum, sonrasında her 2 saatte bir güncelleme, kapanışta özet e-posta.
5. **Çözüm ve kapanış:** Kabul, alarm ve bildirim akışı `scripts/verify_monitoring.py` ve bir gerçek bildirim ile doğrulanır. Olay kaydı kapanır.
6. **Olay sonrası inceleme (S1, S2):** 5 iş günü içinde: zaman çizelgesi, kök neden, neyin işe yaradığı, kalıcı düzeltme ve izleme kuralı eklemeleri. Sonuç `docs/yol-haritasi.md` ilerleme günlüğüne bir satır olarak işlenir.

## Değişiklik ve bakım

- Sürüm dağıtımı `scripts/live/deploy.sh` ile, mesai saatleri dışında ve önce yerel prova (`scripts/rehearse_live.py`) ile yapılır. Migration içeren sürümlerden önce yedek alınır.
- Planlı bakım en az 2 iş günü önce organizasyon sahiplerine e-postayla bildirilir; kaynak sistemler bakım süresince outbox'ta biriktirir.
- Sistem yöneticisi hesaplarında iki adımlı doğrulama zorunludur; destek erişimi kişiseldir, paylaşılan hesap kullanılmaz.

## Kayıtlar ve saklama

| Kayıt | Yer | Saklama |
|---|---|---|
| Destek istekleri ve olay kayıtları | destek e-posta kutusu / takip aracı | 2 yıl |
| Güvenlik olayları | `security_events` | organizasyon silinene kadar; proje saklama kurallarından bağımsız |
| Denetim kaydı | `audit_log` | proje saklama kuralı (`docs/v1-1.md`) |
| Sunucu dışı yedekler | rclone hedefi | `docs/canli-ortam.md` |
