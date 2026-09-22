# Kurumsal kullanım ve ticari ürün (Aşama 13)

Bir **organizasyon** birden çok projeyi, kullanıcıyı, planı ve güvenlik ayarını bir arada tutar. Proje ayarları (üyeler, anahtarlar, kanallar, saklama) her projenin kendi Ayarlar sayfasında kalır; bu paket organizasyon katmanını ekler. Migration `011_organizations.sql`.

## Organizasyon ve roller

| Organizasyon rolü | Yetki |
|---|---|
| `owner` (sahip) | Her şey: üyeler, gruplar, SSO, dışa aktarım, silme. Son sahip kaldırılamaz ve rolü düşürülemez; sahip rolünü yalnızca sahipler verir |
| `admin` (yönetici) | Üyeler, gruplar, organizasyon adı; bütün projelere **proje sahibi** olarak erişir |
| `member` (üye) | Organizasyon sayfasını görür; projelere ancak proje üyeliği ya da grup üyeliğiyle erişir |

Bir kullanıcının bir projedeki etkin rolü üç kaynaktan gelir ve en güçlüsü kazanır: doğrudan proje üyeliği, organizasyon sahibi/yönetici olması (proje sahibi sayılır) ve **proje grubu** üyeliği. Grup, birden çok projeye aynı kullanıcı kümesini aynı rolle (`owner`, `operator`, `viewer`) bağlar. Migration sırasında her projenin sahipleri o projenin organizasyonuna sahip olarak eklenir; eski organizasyonlar `business` planına alınır.

Yeni proje, kullanıcının sahip ya da yönetici olduğu ilk organizasyonda açılır (`POST /me/projects` isteğe bağlı `organizationId` alır) ve plan sınırına tabidir.

## Planlar, sınırlar ve kullanım ölçümü

| Plan | Proje | Kullanıcı | Günlük olay | Saklama |
|---|---|---|---|---|
| `trial` (Deneme, 14 gün) | 3 | 5 | 5.000 | 30 gün |
| `starter` | 5 | 10 | 50.000 | 90 gün |
| `business` | 25 | 50 | 500.000 | 365 gün |
| `enterprise` | sınırsız | sınırsız | sınırsız | sınırsız |
| `suspended` (Askıda) | 0 | 0 | 0 | - |

Sistem yöneticisi müşteri bazında sınırları geçersiz kılar (`organization_limits`). Kullanım `usage_daily` tablosunda gün bazında sayılır: her `POST /events` isteği olay sayacını artırır; sınır aşımı **429**, askıdaki ya da deneme süresi dolmuş hesap **402** döner ve red sayacına yazılır. Organizasyon sayfası son 30 günü ve bugünün kullanımını gösterir; proje ve kullanıcı sınırları proje oluştururken ve üye eklerken **403** ile uygulanır.

Ödeme sağlayıcı bağlı değildir; plan değişikliği sistem yöneticisi tarafından `/admin` sayfasından elle yapılır ve `plan_note` alanına sözleşme/fatura notu yazılır. Deneme süresi bitmeden 3 gün önce ve bittiği gün sahiplere e-posta gider (worker, saatlik).

## Hesap açılışı ve başlangıç

`POST /api/v1/auth/signup` (panelde `/signup`) organizasyon, sahip hesabı ve "İlk proje"yi oluşturur, deneme planını başlatır, hoş geldiniz e-postası kuyruğa alır ve oturum döner. `SIGNUP_ENABLED=false` ile kapatılır (canlı örnekte kapalı; davetle katılım sürer). Projeler sayfasındaki **başlangıç listesi** (`GET /onboarding`) altı adımı izler: akış, gönderim anahtarı, bağlantı, ilk gerçek bildirim, bildirim kanalı, ekip arkadaşı.

## Sistem yöneticisi

`app_users.is_system_admin` işaretli kullanıcılar `/admin` sayfasında bütün organizasyonları, planları ve 30 günlük kullanımı görür; plan ve sınır atar. İşaret `SYSTEM_ADMIN_EMAILS` (virgülle ayrılmış) ile API açılışında ya da SQL ile verilir. Sistem yöneticisi her organizasyona sahip olarak erişir ve SSO zorunluluğundan muaftır.

## İki adımlı doğrulama (TOTP)

RFC 6238 uyumlu; Google Authenticator, Microsoft Authenticator, 1Password ile çalışır. Hesabım sayfasında gizli anahtar üretilir (`POST /me/mfa/begin`), uygulamadaki kodla etkinleştirilir (`/me/mfa/enable`, 8 tek kullanımlık kurtarma kodu döner). Açıkken parola girişi oturum yerine 5 dakikalık bir **doğrulama isteği** döner (`mfaRequired`, `challenge`); `POST /auth/mfa` kod ya da kurtarma koduyla oturumu açar. Beş hatalı kod hesabı 15 dakika kilitler. Kapatmak parola ve güncel kod ister ve bütün oturumları kapatır. Gizli anahtar DataProtection ile şifrelenir, kurtarma kodları SHA-256 özetiyle saklanır.

## Kurumsal giriş (OpenID Connect)

Sahip, organizasyon sayfasında issuer adresi, client ID, client secret ve e-posta alanını (`sirket.com`) tanımlar. Sunucu sağlayıcının `/.well-known/openid-configuration` belgesini ve JWKS anahtarlarını okur (issuer https olmalı; yerel doğrulama için `Sso__AllowHttp=true`). Akış:

1. Giriş sayfasında "Kurumsal giriş": e-posta alanından organizasyon bulunur, `state` ve `nonce` üretilir, tarayıcı sağlayıcının yetkilendirme adresine gider (`POST /auth/sso/start`).
2. Sağlayıcı `{API_PUBLIC_URL}/api/v1/auth/sso/callback` adresine döner; sunucu kodu token'a çevirir, ID token imzasını, issuer, audience, süre ve nonce'u doğrular, e-postanın organizasyonun alanında olduğunu kontrol eder.
3. Kullanıcı yoksa oluşturulur ve organizasyona **üye** olarak eklenir; proje erişimini bir yönetici verir.
4. Tarayıcı panele 2 dakikalık tek kullanımlık **bilet** ile yönlendirilir (`/sso?ticket=`); panel bileti oturuma çevirir (`POST /auth/sso/ticket`). Oturum belirteci URL'de taşınmaz.

"Yalnızca SSO" seçeneği açıkken üyeler parola ile giremez (403); sistem yöneticileri muaftır. Sağlayıcıda yönlendirme adresi olarak callback yolu, izin olarak `openid email profile` tanımlanır. `Api__PublicUrl` (`API_PUBLIC_URL`) callback adresinin temelidir.

## Güvenlik olayları ve denetim

`security_events` tablosu giriş (parola, iki adım, SSO), hatalı denemeler, doğrulama değişiklikleri, SSO ayarları, organizasyon üyelik ve plan değişiklikleri, dışa aktarım ve silme isteklerini IP ve tarayıcı bilgisiyle kaydeder. Kullanıcı kendi olaylarını Hesabım sayfasında, sahip ve yöneticiler organizasyonun olaylarını ve bütün projelerin denetim kaydını (`audit_log`) organizasyon sayfasında görür.

## Veri saklama, dışa aktarma ve silme

- **Saklama:** proje bazındaki saklama kuralları (`docs/v1-1.md`) sürer; plan `max_retention_days` üst sınırı tanımlar.
- **Dışa aktarma:** sahip `POST /organizations/{id}/exports` ile ister; API sunucusundaki `ExportRunner` 15 saniyede bir bekleyen isteği alır, her tablo için JSON Lines dosyası ve `manifest.json` içeren bir zip üretir (`Exports__Path`, compose'da `exports_data` birimi). Parola ve anahtar özetleri ile şifrelenmiş kanal hedefleri dışa aktarıma girmez. Dosya 7 gün sonra silinir; indirme `data.export_downloaded` olayı olarak kaydedilir.
- **Silme:** sahip parolasıyla `POST /organizations/{id}/deletion` çağırır; hesap askıya alınır, bütün sahiplere e-posta gider, 7 gün içinde iptal edilebilir. Süre dolunca worker (`EnterpriseScheduler`, saatlik) bütün proje verilerini, üyelikleri, sınırları ve organizasyonu kalıcı olarak siler. Sunucu dışı yedekler kendi saklama süreleri boyunca kalır (`docs/canli-ortam.md`).

## API

| Uç | Yetki | Amaç |
|---|---|---|
| `POST /auth/signup`, `POST /auth/mfa`, `POST /auth/sso/start`, `GET /auth/sso/callback`, `POST /auth/sso/ticket` | herkese açık, hız sınırlı | Hesap açılışı ve giriş adımları |
| `GET /me/organizations`, `GET /me/security`, `POST /me/mfa/{begin,enable,disable}` | oturum | Kullanıcının organizasyonları ve güvenliği |
| `GET /onboarding` | proje, her rol | Başlangıç listesi |
| `GET/PUT /organizations/{id}`, `/members`, `/groups` | sahip, yönetici | Organizasyon yönetimi |
| `GET /organizations/{id}/security-events`, `/audit` | sahip, yönetici | Kayıtlar |
| `PUT /organizations/{id}/sso`, `/exports`, `/deletion` | sahip | Güvenlik ve veri süreçleri |
| `GET /admin/organizations`, `GET /admin/plans`, `PUT /admin/organizations/{id}/plan` | sistem yöneticisi | Planlar ve sınırlar |

## Yapılandırma

| Değişken | Varsayılan | Amaç |
|---|---|---|
| `API_PUBLIC_URL` / `Api__PublicUrl` | `http://127.0.0.1:8080` | SSO callback adresinin temeli; canlıda `https://API_HOST` |
| `SYSTEM_ADMIN_EMAILS` | boş | Açılışta sistem yöneticisi işaretlenecek e-postalar |
| `SIGNUP_ENABLED` | yerel `true`, canlı `false` | Kendi kendine hesap açılışı |
| `Sso__AllowHttp` | `false` (yerel compose `true`) | http issuer'a izin; yalnızca doğrulama için |
| `Exports__Path` | `/var/lib/integraops/exports` | Dışa aktarım dosyaları |

## Doğrulama

```bash
python3 scripts/verify_enterprise.py   # 68 kontrol; openssl gerektirir, süreç içi OpenID sağlayıcısı 8799 portunu kullanır
dotnet test backend/tests              # TotpTests RFC 6238 örnek vektörlerini içerir
```

22 Eylül 2026 sonucu: 68 kurumsal kontrol geçti (kayıt, plan sınırları, kullanım ölçümü, gruplar, yönetim listesi, TOTP, SSO, güvenlik olayları, dışa aktarım, silme isteği); 91 .NET birim testi geçti; migration 011 uygulandı. Giriş uçlarındaki hız sınırı (dakikada 30) nedeniyle betik gerekirse 61 saniye bekler.

Yerel doğrulama betiği SSO için `host.docker.internal` üzerinden bu makinedeki sahte sağlayıcıya ulaşır; canlıda gerçek sağlayıcı (Entra ID, Google, Okta, Keycloak) ile bir kez el ile kabul yapılmalıdır. Silme sonrası kalıcı temizleme 7 günlük süre gerektirdiğinden betikte istek ve iptal sınanır; temizleme SQL'i `PostgresSecurityStore.PurgeSql` içindedir.
