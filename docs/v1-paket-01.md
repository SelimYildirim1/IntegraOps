# V1 Paket 01 — Panel doğruluğu ve oturumlu doğrulama

Tarih: 21 Eylül 2026. Temel alınan sürüm: 18 Eylül'de incelenen proje arşivindeki çalışma dosyaları.

## Değişiklikler

1. İşlem detayında negatif olay zamanı farkı sıfıra sıkıştırılmak yerine açık bir zaman sırası uyarısı gösterir. Pozitif süre “sonra gerçekleşti” olarak adlandırılır; olay zamanı ile bildirim ulaşma zamanı ayrılır. Bu yalnızca sunum düzeltmesidir; işlem ve alarm kuralları değişmez.
2. Sistem ekranı işlenmemiş olay sayısının henüz yayımlanmamış olayları da içerdiğini açıklar. Duran teslimatların yalnızca deneme sınırından değil kalıcı retten de kaynaklanabileceği belirtilir.
3. `verify_api.py` ve `verify_delays.py` için açık `--session` seçeneği eklendi. V1 kullanıcısıyla giriş yapıp her koşuda ayrı test projeleri açar. Proje içi akış oluşturma oturumla, gönderim ingest, okuma read anahtarıyla yapılır.
4. Geçici anahtarlar normal bitişte ve kontrol hatasında iptal edilir; yalnızca betiğin açtığı oturum kapatılır. Temizlik başarısızsa hata raporlanır; asıl test hatası varsa korunur. Ağ kesintisi/zorla kapatma durumunda elle temizlik gerekebilir.
5. Bayraksız ilk kurulum modu korunur. İlk kullanıcı varsa bootstrap 409 için açıkça `--session` önerilir. Oturumlu koşu ilk kurulumun demo erişim dosyasını değiştirmez.
6. README güncel V1 modüllerini ve boş ortam/kurulu ortam ayrımını anlatır. CI'ye doğrulama istemcisinin testleri ve ilk sahip kurulduktan sonraki oturumlu senaryolar eklenir.

Yeni yardımcı: `scripts/verification_client.py`. Yeni otomatik kontroller: `scripts/tests/test_verification_client.py`. Yeni paket bağımlılığı ve SQL migration yoktur.

## Bu pakette gerçekleştirilen doğrulama

| Kontrol | Sonuç |
|---|---|
| Panel TypeScript kontrolü | Geçti (`tsc --noEmit --incremental false`) |
| Doğrulama istemcisi HTTP/oturum sözleşmesi | 9 test geçti; yerel sahte HTTP sunucusu |
| Süre gösterimi | 8 doğrudan kontrol geçti: negatif, saniye altı negatif, sıfır, normal, gecikmiş, zaman farkı ve eksik zaman |
| Python sözdizimi ve CLI yardım seçenekleri | Geçti |

Bu kontroller gerçek .NET API, PostgreSQL veya RabbitMQ'yu çalıştırmaz. Bu ortamda .NET/Docker bulunmadığı için üretim panel derlemesi, tarayıcı görsel kontrolü, backend testleri, gerçek oturumlu senaryolar ve CI koşusu burada yeniden doğrulanmadı. Önceki 51/28 başarılı test kaydı bu paket için yeni test sonucu sayılmaz.

## Mevcut V1'de kabul adımları

API, veritabanı ve worker açıkken proje kökünde:

```bash
python3 -m unittest discover -s scripts/tests -v
python3 scripts/verify_api.py --session
python3 scripts/verify_delays.py --session
```

Panel klasöründe:

```bash
npm run typecheck
npm run build
npm run start
```

Yeni paneli başlatmadan önce eski panel sürecini durdurun. İlk test iki proje, ikinci test bir proje oluşturur. Sonuçları panelden görebilirsiniz; bu projelere bildirim kanalı eklenmez. Gecikme testi 30 saniyelik gerçek süreyi bekler.

Mailpit, webhook alıcısı ve kaynak örneği de çalışırken:

```bash
python3 scripts/verify_v1.py --load 100
```

Kesinti provası ayrı bir adım olarak yerel servislerin durdurulabileceği zamanda `--faults` ile yapılır. Bu paket otomatik olarak servis durdurmaz, veri geri yüklemez veya canlı dağıtım yapmaz.

## Sonraki paket

Bu değişikliklerin yerel kabulünden sonra kesinti sonuçları ve tam uygulama kurtarma provası tamamlanır. Ardından ilk gerçek kaynak sistemi bağlantısı seçilir. Arama, tarih filtreleri, kullanıcı daveti, Python/Go servisleri bu paketin kapsamı değildir.

## Teknik referans

Sayfaların mevcut sunucu bileşeni yapısı korunur. Framework referansı: [Next.js Server and Client Components](https://nextjs.org/docs/app/getting-started/server-and-client-components).
