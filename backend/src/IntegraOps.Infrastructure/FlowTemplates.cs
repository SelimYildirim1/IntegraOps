using IntegraOps.Domain;

namespace IntegraOps.Infrastructure;

/// <summary>A ready-made flow for one sector: steps with stages, allowances and runbooks, plus a demo profile.</summary>
public sealed record FlowTemplate(string Code, string Name, string Sector, string Description, string Promise,
    IReadOnlyList<FlowStep> Steps, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>Sector templates a project starts from. The catalog is static: templates are copied into the project as version 1.</summary>
public static class FlowTemplates
{
    private static FlowStep S(string code, string name, int position, int stage, int? seconds, string instructions,
        string kind = StepKind.Required, string[]? groups = null, Dictionary<string, int>? overrides = null) =>
        new(code, name, position, seconds, overrides, stage, kind, groups ?? [], instructions, "");

    private const int Minute = 60, Hour = 3600, Day = 86400;

    public static readonly IReadOnlyList<FlowTemplate> All =
    [
        new("ecommerce-order", "E-ticaret sipariş takibi", "E-ticaret ve pazar yeri",
            "Sipariş, ödeme ve stok, fatura, ihracat siparişlerinde gümrük, kargo ve teslimat. Ödeme ile stok paralel ilerler.",
            "Pazar yerine geç faturayı ceza yemeden önce görün.",
            [
                S("order-created", "Sipariş alındı", 0, 0, null, "Sipariş kaydı e-ticaret platformundan geldi; sürecin saati bu adımla başlar."),
                S("payment-completed", "Ödeme tamamlandı", 1, 1, 15 * Minute,
                    "Ödeme sağlayıcı panelinde işlemin durumunu kontrol edin. 3D Secure bekleyen ödemelerde müşteriye SMS bağlantısı yeniden gönderilir; 30 dakikada tamamlanmayan ödeme için sipariş iptal edilir."),
                S("stock-reserved", "Stok rezerve edildi", 2, 1, 30 * Minute,
                    "WMS'te rezervasyon kuyruğunu kontrol edin. Stok yoksa alternatif depo aranır; bulunamazsa müşteri hizmetleri müşteriyi bilgilendirir."),
                S("gift-wrap", "Hediye paketi", 3, 1, null, "İsteğe bağlı adım; yalnızca hediye paketi seçilen siparişlerde bildirilir.", StepKind.Optional),
                S("invoice-created", "Fatura kesildi", 4, 2, 4 * Hour,
                    "e-Fatura entegratörünün gönderim kuyruğuna ve hata kayıtlarına bakın. Vergi numarası ya da adres eksikse müşteri kaydı düzeltilip fatura yeniden tetiklenir.",
                    overrides: new() { ["vip"] = Hour }),
                S("customs-declared", "Gümrük beyanı verildi", 5, 3, Day,
                    "Yalnızca ihracat (export) grubundaki siparişler için beklenir. Gümrük müşavirinden beyanname numarasını isteyin; eksik belge varsa GTİP ve fatura kontrol edilir.",
                    StepKind.Conditional, ["export"]),
                S("shipped", "Kargoya verildi", 6, 4, Day,
                    "Kargo etiketinin üretildiğini ve paketin depodan çıktığını kontrol edin. Etiket üretilmediyse kargo API'sinin hata yanıtına bakın.",
                    overrides: new() { ["vip"] = 4 * Hour }),
                S("delivered", "Teslim edildi", 7, 5, 5 * Day,
                    "Kargo takip numarasıyla son hareketi kontrol edin. 'Adreste bulunamadı' durumunda müşteriye ulaşılır; ikinci teslimat denemesi planlanır."),
            ],
            new Dictionary<string, string[]>
            {
                ["payment-completed"] = ["3D Secure doğrulaması zaman aşımına uğradı", "Kart limiti yetersiz (banka yanıtı 51)", "Ödeme sağlayıcı 502 döndü"],
                ["stock-reserved"] = ["Stok yetersiz: SKU-4471 için 0 adet", "WMS rezervasyon servisi 30 sn içinde yanıt vermedi"],
                ["invoice-created"] = ["e-Fatura reddedildi: alıcı vergi numarası geçersiz", "Entegratör kuyruğu dolu (429)", "Fatura serisi için numara aralığı bitti"],
                ["shipped"] = ["Kargo etiketi üretilemedi: adres ilçe kodu eşleşmedi", "Kargo API 401: anahtar süresi dolmuş"],
                ["delivered"] = ["Alıcı adreste bulunamadı", "Paket hasarlı, iade sürecine alındı"],
            }),

        new("erp-order-invoice", "ERP sipariş → e-fatura", "Toptan ve dağıtım",
            "ERP'deki sipariş onayından sevk emri, irsaliye, e-fatura ve tahsilata kadar belge zinciri.",
            "ERP ile e-fatura arasında kaybolan belgeyi bulun.",
            [
                S("order-created", "Sipariş kaydedildi", 0, 0, null, "Satış siparişi ERP'de açıldı."),
                S("approved", "Sipariş onaylandı", 1, 1, 2 * Hour,
                    "Onay bekleyen siparişler ERP'de 'Onay kuyruğu' ekranındadır. Kredi limiti aşımı varsa finans onayı gerekir."),
                S("dispatch-order", "Sevk emri oluştu", 2, 2, 4 * Hour,
                    "Sevk emri açılmadıysa depo planlama ekranında siparişin rezervasyonunu kontrol edin."),
                S("waybill", "İrsaliye kesildi", 3, 3, Day,
                    "e-İrsaliye gönderim durumunu entegratörde kontrol edin. Araç plakası ya da şoför bilgisi eksikse irsaliye taslakta kalır."),
                S("e-invoice", "e-Fatura gönderildi", 4, 4, Day,
                    "Fatura GİB'e ulaşmadıysa entegratör yanıt kodunu okuyun. Alıcı e-fatura mükellefi değilse e-arşiv olarak kesilmelidir."),
                S("payment-collected", "Tahsilat yapıldı", 5, 5, 14 * Day,
                    "Vade geçmişse müşteri temsilcisine hatırlatma açılır; kurumsal müşterilerde mutabakat mektubu gönderilir.",
                    overrides: new() { ["kurumsal"] = 30 * Day }),
            ],
            new Dictionary<string, string[]>
            {
                ["approved"] = ["Kredi limiti aşıldı; finans onayı bekliyor"],
                ["waybill"] = ["e-İrsaliye reddedildi: taşıyıcı VKN geçersiz"],
                ["e-invoice"] = ["GİB zarf reddi: şema doğrulama hatası", "Alıcı e-fatura mükellefi değil; e-arşiv gerekir"],
                ["payment-collected"] = ["Banka mutabakatında tahsilat eşleşmedi"],
            }),

        new("logistics-delivery", "Lojistik teslimat", "Lojistik",
            "Yük kabulünden araç atama, yükleme, transit ve teslim belgesine kadar teslimat süreci.",
            "Teslim süresi ihlalini müşteri aramadan bilin.",
            [
                S("shipment-accepted", "Yük kabul edildi", 0, 0, null, "Taşıma emri TMS'e girildi."),
                S("vehicle-assigned", "Araç atandı", 1, 1, 2 * Hour, "Planlama ekranında boş araç ve şoför kontrol edilir; yoksa taşeron havuzu açılır."),
                S("loaded", "Yüklendi", 2, 2, 4 * Hour, "Depo rampasında bekleyen yüklerin listesine bakın; yükleme belgesi imzalanmamış olabilir."),
                S("in-transit", "Yola çıktı", 3, 3, 6 * Hour, "Araç takip sisteminde çıkış saati yoksa şoförle iletişime geçin."),
                S("delivered", "Teslim edildi", 4, 4, 2 * Day,
                    "Takipte son konum ve teslimat notu kontrol edilir. Adres hatası varsa müşteri hizmetleri yeni adres alır.",
                    overrides: new() { ["express"] = Day }),
                S("pod-received", "Teslim belgesi alındı", 5, 5, Day, "Şoför uygulamasında imzalı belge yüklenmemişse şube ile görüşün."),
            ],
            new Dictionary<string, string[]>
            {
                ["vehicle-assigned"] = ["Uygun araç bulunamadı: tonaj sınırı"],
                ["loaded"] = ["Rampa dolu; yükleme ertelendi"],
                ["delivered"] = ["Alıcı adreste yok", "Adres bulunamadı; navigasyon eşleşmedi"],
                ["pod-received"] = ["Teslim belgesi okunamıyor; yeniden yüklenmeli"],
            }),

        new("payment-settlement", "Ödeme mutabakatı ve hakediş", "Ödeme ve fintek",
            "İşlem yetkilendirmesinden tahsilat, mutabakat, hakediş ve ödeme aktarımına kadar para akışı.",
            "Gün sonu mutabakatını gün içinde yapın.",
            [
                S("authorized", "Yetkilendirildi", 0, 0, null, "Kart işlemi banka tarafından yetkilendirildi."),
                S("captured", "Tahsil edildi", 1, 1, Hour, "Provizyon kapama kuyruğunu kontrol edin; kapanmayan provizyon 7 gün sonra düşer."),
                S("refund", "İade", 2, 2, null, "İsteğe bağlı; iade yapılan işlemlerde bildirilir.", StepKind.Optional),
                S("reconciled", "Mutabakat yapıldı", 3, 2, Day, "Banka ekstresi ile işlem eşleşmedi. Tutar ya da tarih farkı varsa ekstre satırını inceleyin."),
                S("settled", "Hakediş hesaplandı", 4, 3, 2 * Day, "Hakediş dosyası oluşmadıysa komisyon oranı tablosunda eksik üye işyeri olabilir."),
                S("paid-out", "Ödeme aktarıldı", 5, 4, 3 * Day, "EFT talimatı bankaya iletilmedi ya da IBAN doğrulaması başarısız; üye işyeri hesap bilgisini kontrol edin."),
            ],
            new Dictionary<string, string[]>
            {
                ["captured"] = ["Provizyon kapama reddedildi: işlem süresi doldu"],
                ["reconciled"] = ["Ekstre satırı bulunamadı", "Tutar farkı: 1.250,00 ₺ / 1.205,00 ₺"],
                ["paid-out"] = ["IBAN doğrulaması başarısız", "EFT saatleri dışında; bir sonraki güne ertelendi"],
            }),

        new("insurance-claim", "Sigorta hasar dosyası", "Sigorta",
            "Hasar ihbarından eksper atama, ekspertiz, onay ve ödemeye kadar dosya akışı.",
            "Hasar dosyasının hangi masada beklediğini görün.",
            [
                S("claim-reported", "Hasar ihbarı alındı", 0, 0, null, "İhbar çağrı merkezi ya da mobil uygulamadan açıldı."),
                S("assessor-assigned", "Eksper atandı", 1, 1, Day, "Eksper havuzunda bölgeye uygun eksper yoksa merkez atama yapar."),
                S("assessment-done", "Ekspertiz tamamlandı", 2, 2, 5 * Day, "Eksper raporu yüklenmemişse eksper ile görüşülür; ek belge bekleniyorsa sigortalıya hatırlatma gider."),
                S("approved", "Ödeme onaylandı", 3, 3, 3 * Day, "Onay masasında bekleyen dosyalar için yetki limiti kontrol edilir; limit üstü dosyalar yönetici onayına gider."),
                S("paid", "Ödeme yapıldı", 4, 4, 5 * Day, "Ödeme emri muhasebeye ulaştı mı? IBAN eksikse sigortalıdan alınır.",
                    overrides: new() { ["kasko"] = 3 * Day }),
            ],
            new Dictionary<string, string[]>
            {
                ["assessor-assigned"] = ["Bölgede uygun eksper yok"],
                ["assessment-done"] = ["Eksik belge: ruhsat fotokopisi", "Eksper raporu sistemde açılmadı"],
                ["paid"] = ["IBAN eksik", "Ödeme limiti yönetici onayı bekliyor"],
            }),

        new("manufacturing-order", "Üretim siparişi", "Üretim",
            "Sipariş alımından planlama, üretim emri, kalite kontrol ve sevkiyata kadar üretim süreci.",
            "Sipariş sözünü tutamayacağınızı önceden öğrenin.",
            [
                S("order-received", "Sipariş alındı", 0, 0, null, "Müşteri siparişi ERP'ye işlendi."),
                S("planned", "Planlandı", 1, 1, Day, "MRP çalıştırılmadıysa ya da hammadde eksikse planlama bekler; satın alma talebini kontrol edin."),
                S("production-order", "Üretim emri açıldı", 2, 2, 2 * Day, "Üretim emri açılmadıysa kapasite planında hat çakışması olabilir."),
                S("quality-check", "Kalite kontrol", 3, 3, 3 * Day, "Kalite kayıtlarında bekleyen parti var mı? Red durumunda yeniden işlem adımı bildirilir."),
                S("rework", "Yeniden işlem", 4, 3, null, "İsteğe bağlı; kalite reddi sonrası yapılır.", StepKind.Optional),
                S("shipped", "Sevk edildi", 5, 4, 2 * Day, "Sevkiyat planında araç ve irsaliye kontrol edilir."),
            ],
            new Dictionary<string, string[]>
            {
                ["planned"] = ["Hammadde eksik: 4 kalem satın alma bekliyor"],
                ["production-order"] = ["Hat kapasitesi dolu; en erken slot 3 gün sonra"],
                ["quality-check"] = ["Parti reddedildi: ölçü toleransı dışında"],
                ["shipped"] = ["Araç planlanamadı"],
            }),
    ];

    public static FlowTemplate? Find(string? code) => All.FirstOrDefault(t => t.Code == (code ?? "").Trim());

    /// <summary>Catalog view for the panel: steps summarized, runbooks included so the user sees what they get.</summary>
    public static object Catalog() => All.Select(t => new
    {
        code = t.Code, name = t.Name, sector = t.Sector, description = t.Description, promise = t.Promise,
        stepCount = t.Steps.Count, stageCount = t.Steps.Select(s => s.EffectiveStage).Distinct().Count(),
        groups = t.Steps.SelectMany(s => (s.Groups ?? []).Concat(s.Overrides?.Keys ?? [])).Distinct().Order(StringComparer.Ordinal),
        steps = t.Steps.Select(PostgresFlowVersionStore.Step),
    });
}
