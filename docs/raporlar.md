# Raporlar: süre analizi ve operasyon göstergeleri

Panelin "Raporlar" sekmesi geçmiş veriden şu soruları yanıtlar: hangi süreç ne sıklıkta ve ne kadar gecikiyor, alarmlara ne kadar sürede yanıt veriliyor, hangi sorunlar tekrarlıyor, veri ne kadar güvenilir. Sayılar dönem (7/30/90 gün), akış ve test verisi filtresiyle hesaplanır.

## Göstergeler

| Gösterge | Kaynak | Hesap |
|---|---|---|
| Yeni işlem, tamamlanma oranı | `transactions` | Dönemde oluşturulan işlemler ve durumları |
| Süre hedefine uyum | `transaction_step_stats` | Dönemde tamamlanan adımlar içinde tanınan süreyi aşmayanların payı |
| Adım süreleri | `transaction_step_stats` | Adım başına ortanca, p90, p95, en uzun; takvimli akışlarda çalışma saati |
| Açılan alarm, hâlâ açık | `alarms` | Dönemde açılan süre aşımı ve hata alarmları |
| Alarm görülme ve çözüm süresi | `alarms` | Açılıştan "gördüm" işaretine ve kapanışa kadar geçen ortalama ve ortanca süre |
| En sık süre aşan adımlar | `alarms` (overdue) | Adım başına alarm sayısı ve ortalama gecikme |
| En sık hata veren adımlar | `alarms` (failed) | Adım ve hata açıklamasının ilk 80 karakterine göre gruplanmış sayı |
| Tekrarlayan sorunlar | `alarms` | Dönemde 2+ alarm açılan işlemler |
| Gruplara göre | `transactions.group_code` | Grup başına işlem ve süre uyumu |
| Eğilim | `transactions`, `transaction_step_stats`, `alarms` | Günlük (≤30 gün) ya da haftalık kova: işlem, aşım oranı, alarm, ortanca süre |

**Veri kalitesi** kutuları entegrasyon sorunlarını gösterir, iş sorunlarını değil:

- **Geç bildirim:** kaynak zamanından 5 dakikadan sonra ulaşan olaylar ve gecikmenin p95 değeri.
- **Tutarsız zaman:** önceki adımdan önce gerçekleşmiş görünen adımlar (`out_of_order`). Süresi ölçülmez, aşım sayılmaz.
- **İleri tarihli olay:** ulaşma anından 1 dakikadan fazla ileride kaynak zamanı taşıyanlar.
- **Mutabakat uyuşmazlığı:** açık uyuşmazlık sayısı ve dönemde bulunanlar.

Süre ihlali listesi `/reports/breaches` sayfasındadır.

## Özet e-postaları

Proje sahibi Ayarlar → Rapor özeti bölümünden günlük ya da haftalık özeti, gönderim saatini ve saat dilimini seçer. Worker beş dakikada bir kontrol eder; seçilen saatte (haftalıkta pazartesi) özeti etkin e-posta kanallarına kuyruğa alır ve `report_digests` tablosuna kaydeder. Aynı gün ikinci kez göndermez.

Özet şunları içerir: işlem sayıları, süre uyumu, alarm sayıları ve yanıt süreleri, veri kalitesi, en çok süre aşan ve hata veren adımlar, tekrarlayan sorunlar, panel bağlantısı.

Özet anında da gönderilebilir: panelden "şimdi gönder" düğmeleri ya da Python komutu:

```bash
cd reconciler && .venv/bin/integraops-reconciler --config reconciler.conf --digest weekly
```

Komut `POST /api/v1/reports/digest` ucunu çağırır; gönderim anahtarı yeterlidir. Mutabakat kaynağı gerekmeden yalnızca özet için `SOURCE=digest-only` yazılabilir.

## API

| Uç | Yetki | Amaç |
|---|---|---|
| `GET /api/v1/reports/overview?from&to&flow&test` | oturum ya da okuma anahtarı | Bütün göstergeler; aralık en fazla 400 gün |
| `GET /api/v1/reports/trend?flow&test&period=day\|week&buckets` | oturum ya da okuma anahtarı | Eğilim kovaları (2–90) |
| `GET/PUT /api/v1/reports/digest` | sahip | Özet ayarları ve gönderim geçmişi |
| `POST /api/v1/reports/digest` `{period}` | sahip ya da gönderim anahtarı | Özeti şimdi gönder |
| `GET /api/v1/reports/breaches` | okuma | İhlal listesi (V1.2) |

## Doğrulama

```bash
python3 scripts/verify_reports.py
```

24 kontrol: toplamlar, uyum ve tutarsız zaman ayrımı, yüzdelikler, alarm sayıları ve yanıt süreleri, hata gruplaması, tekrarlayan sorun, grup dağılımı, veri kalitesi, aralık doğrulaması, günlük ve haftalık eğilim, okuma anahtarı erişimi, özet ayarı doğrulaması, anlık özet ve Mailpit'e ulaşan e-posta, Python özet komutu, denetim kaydı.

22 Eylül 2026 sonucu: 24 kontrol geçti; migration 008 uygulandı. Rapor sayfası ve özet ayarları tarayıcıda görüntülendi.
