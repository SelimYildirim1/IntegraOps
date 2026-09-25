using System.Text.RegularExpressions;

namespace IntegraOps.Domain;

public sealed record ErrorExplanation(string Kind, string Title, string Detail, string Who, bool Retryable);

/// <summary>
/// Plain-language reading of a source system's error text for notifications (Slack, Teams, e-mail). Rule based: it
/// never states more than the text carries. The panel has the same catalog in frontend/src/lib/errors.ts; keep them in step.
/// </summary>
public static partial class ErrorExplainer
{
    private sealed record Rule(string Kind, Regex Test, bool Retryable, Func<string, string> Title, Func<string, string, string, string> Detail, Func<string, string> Who);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Tr) + s[1..];
    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Rule[] Rules =
    [
        new("unavailable", Rx(@"\b50[234]\b|service unavailable|bad gateway|gateway time-?out|hizmet dışı|servis dışı|unavailable|bakımda|maintenance"), true,
            s => $"{Cap(s)} yanıt vermedi (geçici kesinti)",
            (s, h, r) => $"Kaynak sistem {s} tarafına ulaşmaya çalıştı; servis \"hizmet dışı\"{h} yanıtı verdi{r}. Bu genellikle karşı taraftaki geçici bir kesintidir; iş kaynakta yapılamadı.",
            s => $"{Cap(s)} tarafındaki ekibi ya da sağlayıcıyı bilgilendirin; servis dönünce işlem kaynaktan yeniden denenmelidir."),
        new("timeout", Rx(@"time-?out|timed out|zaman aşımı|deadline exceeded"), true,
            s => $"{Cap(s)} beklenen sürede yanıt vermedi",
            (s, _, r) => $"İstek gönderildi ama yanıt gelmeden süre doldu{r}. İşin {s} tarafında yapılıp yapılmadığı belirsiz olabilir.",
            _ => "Karşı sistemde kaydın oluşup oluşmadığını kontrol ettirin; körlemesine tekrar göndermek mükerrer kayıt yaratabilir."),
        new("connection", Rx(@"connection refused|econnrefused|econnreset|enotfound|\bdns\b|bağlanılamadı|bağlantı (hatası|kurulamadı|koptu)|no route|network (error|unreachable)|\bssl\b|certificate|sertifika"), true,
            s => $"{Cap(s)} ile bağlantı kurulamadı",
            (s, _, r) => $"Kaynak sistem {s} tarafına hiç ulaşamadı (ağ, adres ya da sertifika sorunu){r}. İş karşı tarafa iletilmedi.",
            _ => "Entegrasyon ekibinize iletin; servis adresi, ağ erişimi ve sertifika kontrol edilmeli."),
        new("auth", Rx(@"\b40[13]\b|unauthori[sz]ed|forbidden|yetki|kimlik doğrulama|authentication|invalid (api )?key|api anahtarı|token (expired|geçersiz|süresi)|expired token|access denied|erişim reddedildi"), false,
            _ => "Yetki ya da kimlik doğrulama sorunu",
            (s, h, _) => $"{Cap(s)} isteği reddetti{h}: kullanılan anahtar, şifre ya da yetki geçersiz ya da süresi dolmuş.",
            _ => "Entegrasyon ayarlarındaki kimlik bilgilerini yenileyin; sonra işi yeniden gönderin."),
        new("not_found", Rx(@"\b404\b|not found|bulunamadı|does not exist|mevcut değil|no such"), false,
            _ => "Kayıt karşı sistemde bulunamadı",
            (s, h, _) => $"{Cap(s)} istenen kaydı bulamadı{h}; işlem numarası, müşteri ya da ürün karşı tarafta yok ya da farklı yazılmış.",
            _ => "Kaydın iki sistemde de aynı numarayla olduğunu kontrol edin; çoğu zaman eşleme ya da veri hatasıdır."),
        new("duplicate", Rx(@"\b409\b|duplicate|mükerrer|zaten (var|mevcut|kayıtlı|oluşturulmuş)|already exists|conflict"), false,
            _ => "Mükerrer kayıt",
            (s, h, _) => $"{Cap(s)} bu kaydın zaten var olduğunu bildirdi{h}; iş daha önce yapılmış olabilir.",
            _ => "Karşı sistemdeki mevcut kaydı kontrol edin; genellikle yeniden gönderim gerekmez."),
        new("stock", Rx(@"stok|stock|out of stock|insufficient (quantity|inventory)"), false,
            _ => "Stok yetersiz",
            (s, _, _) => $"{Cap(s)} istenen miktarın stokta olmadığını bildirdi; iş stok tamamlanmadan ilerleyemez.",
            _ => "Depo ya da satın alma ekibini bilgilendirin; stok girince işlem kaynaktan yeniden denenmelidir."),
        new("payment", Rx(@"ödeme (reddedildi|başarısız|alınamadı)|payment (declined|failed|rejected)|insufficient funds|limit yetersiz|kart reddedildi|\bdeclined\b|provizyon"), false,
            _ => "Ödeme reddedildi",
            (s, _, _) => $"{Cap(s)} ödemeyi onaylamadı (limit, kart ya da provizyon sorunu). Sipariş ödeme adımında kaldı.",
            _ => "Müşteriyle iletişime geçin ya da farklı ödeme yöntemi isteyin; sistemde düzeltilecek bir şey genellikle yoktur."),
        new("validation", Rx(@"\b(400|422)\b|invalid|geçersiz|validation|doğrulama hatası|required|zorunlu|eksik|missing|format|hatalı (alan|değer)|bad request"), false,
            _ => "Gönderilen bilgi kabul edilmedi (veri hatası)",
            (s, h, _) => $"{Cap(s)} gönderilen veriyi reddetti{h}: bir alan eksik ya da hatalı. Kayıt düzeltilmeden tekrar denemek işe yaramaz.",
            _ => "Kayıttaki alanları (vergi numarası, adres, tutar, kod vb.) düzeltip işi yeniden gönderin."),
        new("internal", Rx(@"\b500\b|internal server error|exception|nullreference|stack ?trace|unhandled|beklenmeyen hata|unexpected error|\berror\b|\bhata\b"), true,
            s => $"{Cap(s)} tarafında beklenmeyen hata",
            (s, h, r) => $"Karşı sistem isteği işlerken kendi içinde hata verdi{h}{r}; sorun gönderilen veride değil, {s} tarafında görünüyor.",
            s => $"{Cap(s)} ekibine hata zamanı ve işlem numarasıyla bildirin."),
    ];

    private static readonly (Regex Test, string Name)[] Services =
    [
        (Rx(@"\bgib\b|e-?fatura|efatura|e-?invoice"), "e-fatura servisi"),
        (Rx(@"e-?arşiv|earsiv"), "e-arşiv servisi"),
        (Rx(@"\berp\b|\bsap\b|netsis|\blogo\b|mikro|nebim|canias"), "ERP sistemi"),
        (Rx(@"kargo|cargo|shipment|shipping|\baras\b|yurtiçi|\bmng\b|\bups\b|\bdhl\b|sürat|hepsijet|trendyol express"), "kargo firması servisi"),
        (Rx(@"ödeme|payment|iyzico|paytr|param|\bpos\b|banka|\bbank\b|provizyon"), "ödeme sağlayıcısı"),
        (Rx(@"\bcrm\b|salesforce|hubspot"), "CRM sistemi"),
        (Rx(@"depo|warehouse|\bwms\b"), "depo sistemi"),
        (Rx(@"pazaryeri|marketplace|trendyol|hepsiburada|amazon|n11"), "pazaryeri servisi"),
    ];

    private static readonly Regex Http = Rx(@"\b([45]\d\d)\b");
    private static readonly Regex Retries = Rx(@"(\d+)\s*deneme|retry\s*(\d+)\s*/\s*(\d+)|attempt\s*(\d+)\s*(?:of|/)\s*(\d+)|(\d+)\s*(?:retries|attempts)");

    public static ErrorExplanation? Explain(string? error)
    {
        var text = (error ?? "").Trim();
        if (text.Length == 0) return null;
        var rule = Rules.FirstOrDefault(r => r.Test.IsMatch(text));
        if (rule is null) return null;
        var service = Services.FirstOrDefault(s => s.Test.IsMatch(text)).Name ?? "karşı sistem";
        var code = Http.Match(text);
        var http = code.Success ? $" (HTTP {code.Groups[1].Value})" : "";
        var m = Retries.Match(text);
        var count = m.Success ? new[] { m.Groups[1], m.Groups[3], m.Groups[5], m.Groups[6] }.FirstOrDefault(g => g.Success)?.Value : null;
        var retries = count is null ? "" : $" ({count} deneme yapıldı)";
        return new(rule.Kind, rule.Title(service), rule.Detail(service, http, retries), rule.Who(service), rule.Retryable);
    }

    /// <summary>One paragraph for chat and e-mail notifications; null when the text is not recognised.</summary>
    public static string? Plain(string? error) => Explain(error) is { } x ? $"{x.Title}. {x.Detail} {x.Who}" : null;
}
