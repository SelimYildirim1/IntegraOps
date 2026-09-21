**İlk teslimin doğrulama durumu — 7 Eylül 2026**

| Kontrol | Sonuç |
| --- | --- |
| .NET çözümünün Release derlemesi | Başarılı; 0 hata, 0 uyarı |
| xUnit testleri | 16 başarılı, 0 başarısız, 0 atlanan |
| API'nin gerçekten başlatılması | Başarılı |
| GET /health/live | 200 doğrulandı |
| Veritabanı kapalıyken GET /health/ready | 503 doğrulandı |
| Anahtarsız kurulum isteği | 401 doğrulandı |
| Anahtarsız olay gönderimi | 401 doğrulandı |
| Python betiklerinin sözdizimi | Kontrol edildi |
| Proje XML ve JSON dosyaları | Ayrıştırıldı |
| PostgreSQL migration'ının gerçek veritabanında uygulanması | Bu ortamda çalıştırılmadı |
| API → PostgreSQL → RabbitMQ → worker akışı | Bu ortamda çalıştırılmadı |
| Docker görüntüleri ve Compose servislerinin birlikte açılması | Bu ortamda çalıştırılmadı; Docker yok |
| scripts/verify_api.py bütünleşik doğrulaması | Hazırlandı; gerçek servisler açıldıktan sonra çalıştırılmalı |
| GitHub Actions | İş akışı dosyası hazırlandı; uzak depoda çalıştırılmadı |

Derleme ortamı: Linux x64, .NET SDK 10.0.400, hedef net10.0. Bu çalışma ortamında derleme sunucusu kullanımı sorun çıkardığı için derleme tek işçiyle ve paylaşımlı derleyici kapalı olarak tamamlandı; uygulama kaynak kodunu veya test kapsamını değiştirmek gerekmedi.

Testler olayların ters sırada gelişi, eski hatanın geç gelmesi, daha yeni hata, gözlenmeyen adımın başarısız sayılmaması, henüz işlenmemiş kayıt, aynı zaman damgasının kararlı değerlendirilmesi, saat dilimi, gelecekteki zaman, geçersiz durum, eksik olay kimliği ve hatalı akış tanımlarını kapsar. Veritabanı üzerinde tekilleştirme, eşzamanlılık ve proje ayrımı kontrolleri bütünleşik doğrulama betiğinde bulunur; bunlar bu teslimde geçmiş testler olarak raporlanmamaktadır.

Kaynak kod ve ilk backend kuralları hazırdır. PostgreSQL ve RabbitMQ ile çalışan bütün akışın onayı, yerel Docker doğrulamasının ardından verilebilir. Sonraki özellik çalışmasına geçmeden önce README'deki üç başlangıç komutu uygulanmalıdır.

## V1 kurulumu — 17.09.2026

| Kontrol | Sonuç |
|---|---|
| `dotnet build IntegraOps.slnx -c Release` | 0 hata, 0 uyarı |
| `dotnet test` | 51 test geçti |
| Panel `npm run typecheck` ve `npm run build` | Geçti, 13 rota |
| `python3 scripts/verify_v1.py --load 100` | 28 kontrol geçti |
| Migration 003_pilot | Uygulandı (schema_migrations: 1, 2, 3) |
| Elle senaryo (OrderSource → alarm → fatura) | Alarm açıldı ve kapandı |
| Mailpit / webhook alıcısı | Açılış ve kapanış bildirimleri ulaştı |
| `scripts/backup_local.sh` + `scripts/check_restore.sh` | Geri yükleme başarılı (113 işlem, 8 alarm, 3 kullanıcı) |

Yapılmayanlar: `--faults` kesinti denemesi, canlı ortam (DNS/TLS/SMTP) provası.
