// Plain-language reading of a source system's error text, for operators who do not read HTTP codes and stack traces.
// Rule based and deterministic: it runs everywhere an error is shown, needs no model and never invents facts the text
// does not carry. The backend mirror (IntegraOps.Domain/ErrorExplainer.cs) drives Slack, Teams and e-mail; keep the
// catalogs in step.

export type ErrorExplanation = {
  kind: "unavailable" | "timeout" | "connection" | "auth" | "not_found" | "duplicate" | "validation" | "stock" | "payment" | "internal";
  title: string; detail: string; who: string; retryable: boolean;
};

type Rule = { kind: ErrorExplanation["kind"]; test: RegExp; retryable: boolean;
  title: (s: string) => string; detail: (s: string, http: string, retries: string) => string; who: (s: string) => string };

const cap = (s: string) => s.charAt(0).toLocaleUpperCase("tr-TR") + s.slice(1);

const rules: Rule[] = [
  { kind: "unavailable", test: /\b50[234]\b|service unavailable|bad gateway|gateway time-?out|hizmet dışı|servis dışı|unavailable|bakımda|maintenance/i, retryable: true,
    title: s => `${cap(s)} yanıt vermedi (geçici kesinti)`,
    detail: (s, http, r) => `Kaynak sistem ${s} tarafına ulaşmaya çalıştı; servis "hizmet dışı"${http} yanıtı verdi${r}. Bu genellikle karşı taraftaki geçici bir kesintidir; iş kaynakta yapılamadı.`,
    who: s => `${cap(s)} tarafındaki ekibi ya da sağlayıcıyı bilgilendirin; servis dönünce işlem kaynaktan yeniden denenmelidir.` },
  { kind: "timeout", test: /time-?out|timed out|zaman aşımı|deadline exceeded/i, retryable: true,
    title: s => `${cap(s)} beklenen sürede yanıt vermedi`,
    detail: (s, http, r) => `İstek gönderildi ama yanıt gelmeden süre doldu${r}. İşin ${s} tarafında yapılıp yapılmadığı belirsiz olabilir.`,
    who: () => "Karşı sistemde kaydın oluşup oluşmadığını kontrol ettirin; körlemesine tekrar göndermek mükerrer kayıt yaratabilir." },
  { kind: "connection", test: /connection refused|econnrefused|econnreset|enotfound|\bdns\b|bağlanılamadı|bağlantı (hatası|kurulamadı|koptu)|no route|network (error|unreachable)|\bssl\b|certificate|sertifika/i, retryable: true,
    title: s => `${cap(s)} ile bağlantı kurulamadı`,
    detail: (s, _h, r) => `Kaynak sistem ${s} tarafına hiç ulaşamadı (ağ, adres ya da sertifika sorunu)${r}. İş karşı tarafa iletilmedi.`,
    who: () => "Entegrasyon ekibinize iletin; servis adresi, ağ erişimi ve sertifika kontrol edilmeli." },
  { kind: "auth", test: /\b40[13]\b|unauthori[sz]ed|forbidden|yetki|kimlik doğrulama|authentication|invalid (api )?key|api anahtarı|token (expired|geçersiz|süresi)|expired token|access denied|erişim reddedildi/i, retryable: false,
    title: () => "Yetki ya da kimlik doğrulama sorunu",
    detail: (s, http) => `${cap(s)} isteği reddetti${http}: kullanılan anahtar, şifre ya da yetki geçersiz ya da süresi dolmuş.`,
    who: () => "Entegrasyon ayarlarındaki kimlik bilgilerini yenileyin; sonra işi yeniden gönderin." },
  { kind: "not_found", test: /\b404\b|not found|bulunamadı|does not exist|mevcut değil|no such/i, retryable: false,
    title: () => "Kayıt karşı sistemde bulunamadı",
    detail: (s, http) => `${cap(s)} istenen kaydı bulamadı${http}; işlem numarası, müşteri ya da ürün karşı tarafta yok ya da farklı yazılmış.`,
    who: () => "Kaydın iki sistemde de aynı numarayla olduğunu kontrol edin; çoğu zaman eşleme ya da veri hatasıdır." },
  { kind: "duplicate", test: /\b409\b|duplicate|mükerrer|zaten (var|mevcut|kayıtlı|oluşturulmuş)|already exists|conflict/i, retryable: false,
    title: () => "Mükerrer kayıt",
    detail: (s, http) => `${cap(s)} bu kaydın zaten var olduğunu bildirdi${http}; iş daha önce yapılmış olabilir.`,
    who: () => "Karşı sistemdeki mevcut kaydı kontrol edin; genellikle yeniden gönderim gerekmez." },
  { kind: "stock", test: /stok|stock|out of stock|insufficient (quantity|inventory)/i, retryable: false,
    title: () => "Stok yetersiz",
    detail: s => `${cap(s)} istenen miktarın stokta olmadığını bildirdi; iş stok tamamlanmadan ilerleyemez.`,
    who: () => "Depo ya da satın alma ekibini bilgilendirin; stok girince işlem kaynaktan yeniden denenmelidir." },
  { kind: "payment", test: /ödeme (reddedildi|başarısız|alınamadı)|payment (declined|failed|rejected)|insufficient funds|limit yetersiz|kart reddedildi|\bdeclined\b|provizyon/i, retryable: false,
    title: () => "Ödeme reddedildi",
    detail: s => `${cap(s)} ödemeyi onaylamadı (limit, kart ya da provizyon sorunu). Sipariş ödeme adımında kaldı.`,
    who: () => "Müşteriyle iletişime geçin ya da farklı ödeme yöntemi isteyin; sistemde düzeltilecek bir şey genellikle yoktur." },
  { kind: "validation", test: /\b(400|422)\b|invalid|geçersiz|validation|doğrulama hatası|required|zorunlu|eksik|missing|format|hatalı (alan|değer)|bad request/i, retryable: false,
    title: () => "Gönderilen bilgi kabul edilmedi (veri hatası)",
    detail: (s, http) => `${cap(s)} gönderilen veriyi reddetti${http}: bir alan eksik ya da hatalı. Kayıt düzeltilmeden tekrar denemek işe yaramaz.`,
    who: () => "Kayıttaki alanları (vergi numarası, adres, tutar, kod vb.) düzeltip işi yeniden gönderin." },
  { kind: "internal", test: /\b500\b|internal server error|exception|nullreference|stack ?trace|unhandled|beklenmeyen hata|unexpected error|\berror\b|\bhata\b/i, retryable: true,
    title: s => `${cap(s)} tarafında beklenmeyen hata`,
    detail: (s, http, r) => `Karşı sistem isteği işlerken kendi içinde hata verdi${http}${r}; sorun gönderilen veride değil, ${s} tarafında görünüyor.`,
    who: s => `${cap(s)} ekibine hata zamanı ve işlem numarasıyla bildirin.` },
];

const services: [RegExp, string][] = [
  [/\bgib\b|e-?fatura|efatura|e-?invoice/i, "e-fatura servisi"],
  [/e-?arşiv|earsiv/i, "e-arşiv servisi"],
  [/\berp\b|\bsap\b|netsis|\blogo\b|mikro|nebim|canias/i, "ERP sistemi"],
  [/kargo|cargo|shipment|shipping|\baras\b|yurtiçi|\bmng\b|\bups\b|\bdhl\b|sürat|hepsijet|trendyol express/i, "kargo firması servisi"],
  [/ödeme|payment|iyzico|paytr|param|\bpos\b|banka|\bbank\b|provizyon/i, "ödeme sağlayıcısı"],
  [/\bcrm\b|salesforce|hubspot/i, "CRM sistemi"],
  [/depo|warehouse|\bwms\b/i, "depo sistemi"],
  [/pazaryeri|marketplace|trendyol|hepsiburada|amazon|n11/i, "pazaryeri servisi"],
];

export function explainError(error: string | null | undefined): ErrorExplanation | null {
  const text = (error ?? "").trim();
  if (!text) return null;
  const rule = rules.find(r => r.test.test(text));
  if (!rule) return null;
  const service = services.find(([re]) => re.test(text))?.[1] ?? "karşı sistem";
  const code = text.match(/\b([45]\d\d)\b/)?.[1];
  const http = code ? ` (HTTP ${code})` : "";
  const m = text.match(/(\d+)\s*deneme|retry\s*(\d+)\s*\/\s*(\d+)|attempt\s*(\d+)\s*(?:of|\/)\s*(\d+)|(\d+)\s*(?:retries|attempts)/i);
  const count = m ? (m[1] ?? m[3] ?? m[5] ?? m[6]) : null;
  const retries = count ? ` (${count} deneme yapıldı)` : "";
  return { kind: rule.kind, retryable: rule.retryable, title: rule.title(service), detail: rule.detail(service, http, retries), who: rule.who(service) };
}
