# IntegraOps'ta Go ve Python nerede, ne için kullanılıyor?

IntegraOps'un çekirdeği .NET (API, worker, migration) ve panel Next.js/TypeScript ile yazılmıştır. Go ve Python bu çekirdeğin **dışında**, belirli işler için kullanılır. İkisi de işlem durumu, süre ya da alarm kuralı içermez; iş kuralları çekirdekte kalır.

## Kısa cevap

| Dil | Nerede | Ne iş yapar | Nasıl dağıtılır |
|---|---|---|---|
| **Go** | `agent/` | Aktarım ajanı: müşteri sisteminin outbox tablosundan ya da olay dosyalarından kayıtları alıp IntegraOps API'sine güvenilir şekilde gönderir. | Tek statik ikili (`integraops-agent`) ya da `deploy/docker/Dockerfile.agent` ile distroless konteyner. |
| **Python** | `reconciler/` | Mutabakat servisi: kaynak sistemdeki işlemleri IntegraOps kayıtlarıyla dönemsel karşılaştırır, uyuşmazlıkları API'ye yazar. | `pip install` edilen paket (`integraops-reconciler`) ya da `deploy/docker/Dockerfile.reconciler` ile konteyner. |
| **Python** | `sdk/python/` | Gönderici kütüphanesi: Python ile yazılmış kaynak sistemlerin olay göndermesi için tek dosyalık istemci. | Kaynak sistemin koduna kopyalanır; dış bağımlılığı yoktur. |
| **Python** | `scripts/` | Yerel kurulum, uçtan uca doğrulama, kapasite testi, yedek/geri yükleme provaları, canlı ortam yardımcıları. | Geliştirici makinesi ve GitHub Actions CI. |
| **Python** | `samples/webhook/` | Yerel geliştirme için örnek webhook alıcısı (imza, tazelik, tekilleştirme kontrolü). | `docker-compose.yml` içindeki `webhook` servisi, `python:3.13-alpine` imajıyla. |

## Büyük resim

```
Kaynak sistem (ERP, e-fatura, kargo ...)
   │
   ├─ .NET gönderici  (samples/OrderSource)      ─┐
   ├─ Go ajanı         (agent/)                   ├─► IntegraOps API (.NET) ─► RabbitMQ ─► Worker (.NET) ─► PostgreSQL
   ├─ Python SDK       (sdk/python)               │                                                          │
   └─ TypeScript SDK   (sdk/typescript)          ─┘                                                          │
                                                                                                             ▼
Kaynak veritabanı ◄──── Python mutabakat servisi (reconciler/) ────► /reconciliation/lookup, /runs      Panel (Next.js)
```

Go, veriyi **kaynaktan IntegraOps'a taşıyan** tarafta; Python, veriyi **kaynakla IntegraOps arasında karşılaştıran** ve projeyi **test eden** tarafta durur.

## Go: aktarım ajanı (`agent/`)

**Amaç.** Kaynak sistem, iş kaydıyla aynı veritabanı işleminde bir `integraops_outbox` satırı yazar ya da bir dizine JSON dosyası bırakır. Ajan bu kayıtları kiralar, API'ye gönderir ve sonucu kaynağa geri yazar. Ağ kesildiğinde kayıtlar kaynakta bekler; bağlantı gelince aynı olay kimlikleriyle gider. Böylece kaynak sistem HTTP, yeniden deneme ve kuyruk mantığı yazmak zorunda kalmaz.

**Neden Go.**
- Ajan müşterinin sunucusunda çalışır. Go, çalışma zamanı kurulumu gerektirmeyen **tek bir statik ikili** üretir (`CGO_ENABLED=0`); .NET runtime ya da Python yorumlayıcısı istemez.
- Konteyner imajı `distroless/static` tabanlıdır, yalnızca ikiliyi içerir ve `nonroot` kullanıcıyla çalışır. Saldırı yüzeyi ve boyut küçüktür.
- Uzun süre çalışan, düşük kaynak tüketen, eşzamanlı gönderim yapan bir süreç için goroutine modeli doğal bir seçimdir.
- Tek dış bağımlılık `pgx` (PostgreSQL sürücüsü); geri kalanı standart kütüphanedir.

**Yapı.**

| Paket | Görev |
|---|---|
| `cmd/integraops-agent` | Giriş noktası: `-config`, `-check`, `-version` bayrakları, JSON loglama. |
| `internal/config` | `key=value` dosyası ve `AGENT_*` ortam değişkenlerinden yapılandırma; tablo adı gibi değerleri doğrular. |
| `internal/source` | Kaynak adaptörleri: `postgres` (outbox tablosu, `FOR UPDATE SKIP LOCKED` ile kiralama) ve `directory` (`pending/ → sending/ → sent/` veya `dead/`). |
| `internal/sender` | API'ye gönderim; her yanıtı sınıflar (202 gönderildi, 429/5xx yeniden dene, 400/401/403/404/409 `dead`), yanıttaki `eventId`'yi doğrular. |
| `internal/agent` | `kirala → gönder → tamamla` döngüsü, `/status` ve `/metrics` uçları (9464), API'ye yaşam sinyali. |

**Çalıştırma.**

```bash
cd agent
go build -o integraops-agent ./cmd/integraops-agent
./integraops-agent -check -config agent.conf   # yapılandırmayı doğrula
./integraops-agent -config agent.conf          # çalıştır
go test ./...                                  # birim testleri
```

Ayrıntılı kurallar ve tablo sözleşmesi: `docs/aktarim-ajani.md`. Uçtan uca doğrulaması Python tarafındaki `scripts/verify_agent.py` ile yapılır; bu betik Go ajanını derler, yerel PostgreSQL'de outbox tablosu kurar ve kesinti, tekilleştirme, hız sınırı gibi senaryoları dener.

## Python 1: mutabakat servisi (`reconciler/`)

**Amaç.** Olay akışı yalnızca kendisine ulaşan bildirimleri bilir; kaynakta oluşup hiç gönderilmemiş bir işlem alarm üretemez. Mutabakat servisi bu boşluğu kapatır: bir zaman aralığı için kaynak veritabanına SQL sorgusu çalıştırır, sonuçları `POST /reconciliation/lookup` ile IntegraOps'a sorar, farkları `POST /reconciliation/runs` ile kaydeder. Bulduğu türler: hiç bildirilmemiş işlem, bildirilmemiş adım, durum uyuşmazlığı.

**Neden Python.**
- İşin özü müşteriye özgü bir **SQL sorgusu** (`QUERY_FILE`) ve satırların karşılaştırılmasıdır. Sorguyu ve karşılaştırma kurallarını sahada düzenlemesi gereken kişi için Python okunur ve değiştirilir.
- Tek bağımlılık `pg8000` (saf Python PostgreSQL sürücüsü); derleme adımı yoktur.
- Düşük hacimli, aralıklı çalışan (`--loop`, örneğin 15 dakikada bir) bir iş; Go'nun performans avantajına gerek yoktur.

**Yapı.**

| Modül | Görev |
|---|---|
| `cli.py` | Komut satırı: `--config`, `--loop`, `--check`; aralık hesabı (`WINDOW`, `OVERLAP`, `GRACE`, `INTERVAL`). |
| `sources.py` | Kaynak adaptörleri: PostgreSQL sorgusu ya da JSON dosyası; satırları işlem başına adımlara katlar. |
| `compare.py` | Kaynak kayıtlarını IntegraOps yanıtıyla karşılaştırır, uyuşmazlık listesini üretir. |
| `client.py` | IntegraOps API istemcisi (lookup ve run kayıt uçları). |
| `tests/test_compare.py` | Karşılaştırma mantığının birim testleri. |

**Çalıştırma.**

```bash
cd reconciler
python3 -m venv .venv && .venv/bin/pip install .
integraops-reconciler --config reconciler.conf --check
integraops-reconciler --config reconciler.conf --loop
```

Ayrıntı: `docs/mutabakat.md`. Uçtan uca doğrulaması `scripts/verify_reconciler.py`.

## Python 2: gönderici SDK (`sdk/python/integraops_sender.py`)

Kaynak sistemi Python ile yazılmış müşteriler için tek dosyalık istemci. Standart kütüphane dışında bağımlılığı yoktur. İsteği kurar, geçici hataları üstel beklemeyle yeniden dener, kalıcı retleri ayırır (`PermanentRejection`), yanıttaki `eventId`'yi doğrular ve istemci sürümünü bildirir. Kalıcı teslimat (outbox) sorumluluğu kaynak sistemde kalır; SDK yalnızca gönderimi kolaylaştırır. Aynı işlevin TypeScript karşılığı `sdk/typescript/` altındadır; iki SDK ve sunucu tarafı eşleyici aynı deterministik olay kimliğini üretir (`scripts/verify_connections.py` bunu doğrular).

## Python 3: kurulum, doğrulama ve işletme betikleri (`scripts/`)

Bu betikler ürünün parçası değildir; **projeyi ayağa kaldırmak ve gerçek servislerle test etmek** için vardır. Hepsi yalnızca standart kütüphane kullanır, Docker Compose ile açılan yerel yığına HTTP üzerinden konuşur ve kendi izole test projesini oluşturur.

| Grup | Betikler | Amaç |
|---|---|---|
| Kurulum | `init_local.py`, `prepare_v1.py`, `setup_v1.py` | `.env` ve yerel sırları üretir (var olanı ezmez), ilk sahibi, kanalları ve kaynak anahtarını oluşturur. |
| Çekirdek doğrulama | `verify_api.py`, `verify_v1.py`, `verify_delays.py`, `verify_v1_1.py`, `verify_v1_2.py`, `verify_v2_flows.py` | API → PostgreSQL → RabbitMQ → worker akışı, süre takibi, kayıtlı görünümler, akış sürümleri, paralel ve koşullu adımlar. |
| Bileşen doğrulama | `verify_agent.py`, `verify_reconciler.py`, `verify_connections.py`, `verify_reports.py`, `verify_ai.py`, `verify_enterprise.py`, `verify_monitoring.py` | Go ajanı, Python mutabakat servisi, bağlantılar ve SDK'lar, raporlar, yapay zekâ asistanı, organizasyon/plan/SSO, izleme yığını ve alarm e-postaları. |
| Kapasite | `load_test.py` | Olay patlaması gönderir; kabul hızı, gecikme, kuyruk derinliği ve uçtan uca gecikmeyi ölçer (`docs/olcek.md`). |
| Kurtarma provası | `rehearse_restore.py`, `rehearse_live.py` | Yedekten ayrı bir Compose projesine geri yükleme; canlı kurulum, dağıtım, geri alma, dış yedek ve tam kayıp sonrası kurtarma provası. |
| Yardımcılar | `verification_client.py`, `live/render_alertmanager.py`, `tests/` | Betiklerin ortak oturum/anahtar yönetimi, Alertmanager yapılandırma şablonunun doldurulması, betiklerin kendi birim testleri. |

**Neden Python.** Testler gerçek servislere HTTP isteği atar, JSON okur, Docker komutlarını çağırır ve zaman bekler; bu iş için kurulum gerektirmeyen, her geliştirici makinesinde ve CI'da hazır bulunan bir dil gerekir. `.github/workflows/ci.yml` bu betikleri sırayla çalıştırır; Go yalnızca `verify_agent.py` adımı için kurulur.

## Python 4: örnek webhook alıcısı (`samples/webhook/receiver.py`)

Yerel geliştirmede alarm webhook'larını almak için küçük bir HTTP sunucusu. İmzayı ve zaman damgasının tazeliğini doğrular, aynı bildirimi SQLite ile tekilleştirir. `docker-compose.yml` bunu `python:3.13-alpine` imajıyla `webhook` servisi olarak çalıştırır; canlı ortamda kullanılmaz, müşterinin kendi alıcısını nasıl yazacağına örnektir.

## Sınırlar ve kurallar

- **İş kuralı çekirdekte.** Akış tanımı, süre hesabı, alarm açma ve kapama yalnızca .NET API ve worker'da yaşar. Go ajanı ve Python servisi bu kuralları bilmez ve tekrarlamaz.
- **Olay kimliği kaynağa aittir.** Go ajanı, iki SDK ve mutabakat servisi olay kimliğini kaynağın verdiği ya da deterministik türetilen değerle kullanır; API bunu tekilleştirme için esas alır.
- **Bağımlılık az.** Go tarafında `pgx`, Python mutabakat tarafında `pg8000` dışında üçüncü parti paket yoktur; betikler ve SDK yalnızca standart kütüphane kullanır.
- **Doğrulama Python'da toplanır.** Bileşen hangi dilde yazılmış olursa olsun uçtan uca testi `scripts/verify_*.py` içinde yaşar.

## Hızlı başvuru

```bash
# Go ajanı
cd agent && go build ./cmd/integraops-agent && go test ./...

# Python mutabakat
cd reconciler && python3 -m venv .venv && .venv/bin/pip install . && .venv/bin/python -m unittest

# Betikler (yerel yığın açıkken)
python3 scripts/init_local.py && python3 scripts/prepare_v1.py
python3 scripts/verify_api.py
python3 scripts/verify_agent.py        # Go ajanını derleyip dener
python3 scripts/verify_reconciler.py   # Python mutabakat servisini dener
python3 -m unittest discover -s scripts/tests -v
```

İlgili belgeler: `docs/aktarim-ajani.md`, `docs/mutabakat.md`, `docs/entegrasyon-sozlesmesi.md`, `docs/baglantilar.md`, `docs/olcek.md`.
