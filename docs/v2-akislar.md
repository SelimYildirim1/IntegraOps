# V2 — Daha karmaşık süreçlerin takibi

Tarih: 22 Eylül 2026. Migration: `009_complex_flows.sql`. Doğrusal akışlar aynen çalışmaya devam eder; her adım kendi aşamasında, zorunlu ve gruptan bağımsızdır.

## Kavramlar

| Kavram | Kural |
|---|---|
| Aşama | Her adım bir aşama numarası taşır. Aynı aşamadaki adımlar **paralel** ilerler; sonraki aşama önceki aşamanın beklenen adımlarının hepsi bitince başlar. Aşama 0 tek bir zorunlu adımdan oluşur ve saati başlatır. |
| Zorunlu adım | Tamamlanması ya da açıkça atlanması beklenir. Süresi aşama başlangıcından ölçülür. |
| İsteğe bağlı adım | Hiç beklenmez, süre takibine girmez, alarm açmaz. Bildirilirse görünür. |
| Koşullu adım | Yalnızca yazılan gruplardaki işlemler için beklenir. Grup, işlemin ilk bildirimindeki `group` alanıdır. Diğer işlemlerde "bu işlem için beklenmiyor" olarak görünür. |
| Atlama | `status: "skipped"` bildirimi adımı tamamlanmış sayar, süresini ölçmez, açık süre aşımı alarmını `step_completed` ile kapatır. |
| İptal | Herhangi bir adımda `status: "cancelled"` bildirimi işlemi **İptal edildi** durumuna alır. Süre takibi durur; açık alarmlar `cancelled` nedeniyle kapanır. `error` alanı iptal nedenini taşıyabilir. |
| Birleşme noktası | Bir aşamadaki bütün beklenen adımlar tamamlanmadan sonraki aşama beklenmez. Süre, en son biten dalın gerçekleşme anından başlar. |

## Duruma ve alarmlara etkisi

- **Tamamlandı:** Bütün beklenen adımlar (zorunlu ve işlemin grubuna uygulanan koşullu adımlar) tamamlandı ya da atlandı. İsteğe bağlı adımlar sonucu etkilemez.
- **Hata bildirildi:** Herhangi bir adımda `failed` bildirimi varsa (isteğe bağlı olsa da); yeni bir `completed` ya da `skipped` düzeltir.
- **İptal edildi:** `cancelled` bildirimi geldiyse. İptal geri alınmaz; yeni işlem için yeni kimlik kullanılır.
- **Süre takibi:** Bekleyen aşamadaki her beklenen adım kendi süresiyle izlenir. Panelde en yakın son zaman gösterilir; süresi dolan her dal ayrı süre aşımı alarmı açar. Koşula bağlı olarak atlanan bir adım için yanlış alarm üretilmez.
- **Adım süreleri:** Rapor ve ihlal ölçümleri aşama başlangıcından yapılır.

## Sürüm tanımı

Taslak düzenleyicide her adım için tür (zorunlu, isteğe bağlı, koşullu), "önceki adımla paralel" işareti ve koşullu adımda gruplar seçilir. API'de:

```json
{"steps": [
  {"code": "order",    "name": "Sipariş", "maxDelaySeconds": null, "stage": 0},
  {"code": "payment",  "name": "Ödeme",   "maxDelaySeconds": 600,  "stage": 1},
  {"code": "stock",    "name": "Stok",    "maxDelaySeconds": 1800, "stage": 1},
  {"code": "gift-wrap","name": "Hediye paketi", "maxDelaySeconds": null, "stage": 1, "kind": "optional"},
  {"code": "customs",  "name": "Gümrük",  "maxDelaySeconds": 3600, "stage": 2, "kind": "conditional", "groups": ["export"]},
  {"code": "shipped",  "name": "Kargo",   "maxDelaySeconds": 7200, "stage": 3}
]}
```

Kurallar: aşama numarası bir öncekiyle aynı ya da bir fazla olmalı; 0. aşamada tek adım bulunur; koşullu adım en az bir grup ister; isteğe bağlı adımın süresi boş olabilir. Sürüm farkı aşama, tür ve grup değişikliklerini de gösterir.

## Kaynak sistem için

`docs/entegrasyon-sozlesmesi.md` içindeki olay biçimine iki durum eklendi: `skipped` (adım bilinçli olarak yapılmadı) ve `cancelled` (işlem iptal edildi). Her ikisi de kendi `eventId` değerini taşır.

## Doğrulama

```bash
python3 scripts/verify_v2_flows.py
```

24 kontrol: aşama ve grup doğrulaması, sürüm farkı, paralel dallarda ayrı süre aşımı alarmları ve en yakın son zaman, birleşme noktası, koşullu adımın gruba göre beklenmesi, isteğe bağlı adım, atlama, iptal ve alarm kapanışı, iptal filtresi ve özeti, geçersiz durum, 1. sürümün doğrusal kalması. 8 yeni birim testi (`ComplexFlowTests`). 22 Eylül 2026 sonucu: 24 kontrol ve 75 birim testi geçti; `verify_v1_2.py` ve `verify_delays.py` regresyonu geçti.
