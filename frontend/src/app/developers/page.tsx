import Link from "next/link";
import { api, apiMessage } from "@/lib/api";
import type { Access } from "@/lib/pilot-types";
import { Message, PageHead } from "@/components/ui";
import Icon from "@/components/icons";
import CodeTabs from "@/components/code-tabs";

export const dynamic = "force-dynamic";

const curl = `curl -X POST https://API_HOST/api/v1/events \\
  -H "X-API-Key: io_v1_..." \\
  -H "Content-Type: application/json" \\
  -d '{
    "eventId": "0192f3c4-8b1a-7c2e-9f10-3a4b5c6d7e8f",
    "flow": "erp-order-invoice",
    "transactionId": "SO-000123",
    "step": "invoice-created",
    "status": "completed",
    "occurredAt": "2026-09-22T10:15:00+03:00"
  }'`;

const python = `from integraops_sender import IntegraOpsSender, Event

sender = IntegraOpsSender("https://API_HOST", "io_v1_...")
sender.send(Event(
    flow="erp-order-invoice",
    transaction_id="SO-000123",
    step="invoice-created",
    occurred_at="2026-09-22T10:15:00+03:00",
))`;

const node = `import { IntegraOpsSender } from "@integraops/sender";

const sender = new IntegraOpsSender({ baseUrl: "https://API_HOST", apiKey: "io_v1_..." });
await sender.send({
  flow: "erp-order-invoice",
  transactionId: "SO-000123",
  step: "invoice-created",
  occurredAt: new Date(),
});`;

const webhook = `POST https://sizin-adresiniz/integraops
X-IntegraOps-Delivery: 0192f3c4-...      teslimat kimliği
X-IntegraOps-Time: 1790073600            unix saniye
X-IntegraOps-Signature: sha256=…         imza, yanda anlatılıyor

{
  "schema_version": 1,
  "event": "opened",
  "alarm_kind": "overdue",
  "flow": "erp-order-invoice",
  "transaction_id": "SO-000123",
  "step_code": "invoice-created",
  "step_name": "Fatura",
  "deadline_at": "2026-09-22T14:15:00+00:00",
  "instructions": "e-Fatura kuyruğuna ve hata kayıtlarına bakın…",
  "link": "https://erp.sirket.com/orders/SO-000123",
  "error": null
}`;

const steps: { title: string; text: React.ReactNode }[] = [
  { title: "Akış tanımlayın", text: <><Link href="/flows/new" prefetch={false}>Yeni akış</Link> sayfasında bir sektör şablonu seçin ya da adımları yazın. Adım kodları kaynak sistemin göndereceği kodlardır.</> },
  { title: "Gönderim anahtarı alın", text: <><Link href="/settings#keys" prefetch={false}>Ayarlar</Link> sayfasında “Bildirim gönderir” yetkili bir anahtar oluşturun. Anahtar yalnızca bir kez gösterilir.</> },
  { title: "Kaynağı kaydedin", text: <><Link href="/connections" prefetch={false}>Bağlantılar</Link> sayfasında kaynak sistemi kaydedip anahtarı ona bağlayın. Sağlık ve kullanım burada görünür.</> },
  { title: "İlk olayı gönderin", text: <>Yandaki isteği kaynak sistemden çağırın. İlk yanıt <code>202</code>, aynı olay tekrar gönderilirse <code>200</code>.</> },
  { title: "Alarmları yönlendirin", text: <><Link href="/settings#channels" prefetch={false}>Bildirim kanalları</Link> altında Slack, Teams, e-posta ya da imzalı webhook ekleyin.</> },
];

const fields: [string, string][] = [
  ["eventId", "Her iş olayı için bir kez üretilen UUID. Tekrar gönderimde aynı kalır; IntegraOps yinelenen olayı tanır."],
  ["transactionId", "İki sistemde de aynı iş kaydını gösteren kimlik, örneğin sipariş numarası. Kişisel veri içermez."],
  ["flow, step", "Panelde tanımlı akış ve adım kodları."],
  ["status", "completed, failed (hata açıklamasıyla), skipped (bilinçli atlandı) ya da cancelled (işlem iptal)."],
  ["occurredAt", "Kaynakta gerçekleşme anı; saat dilimi zorunlu. Gönderim anı değildir."],
  ["group", "İsteğe bağlı. Grup sürelerini ve koşullu adımları seçer; işlemin ilk bildirimiyle sabitlenir."],
];

const responses: { tone: string; codes: string; meaning: string; action: string }[] = [
  { tone: "good", codes: "202, 200", meaning: "Kabul edildi ya da daha önce kabul edilmişti", action: "Kaydı gönderildi olarak işaretleyin" },
  { tone: "warn", codes: "408, 425, 429, 5xx, ağ hatası", meaning: "Geçici sorun", action: "Üstel beklemeyle aynı eventId ile yeniden deneyin" },
  { tone: "bad", codes: "400, 401, 403, 404, 409", meaning: "Kalıcı ret: alan, anahtar, akış ya da çakışan kimlik", action: "Kaydı ayırın, düzeltin, sonra yeniden sıraya alın" },
];

const endpoints: [string, string, string][] = [
  ["POST", "/api/v1/events", "İş olayı bildir"],
  ["GET", "/api/v1/flows/{flow}/transactions/{id}", "İşlemin zaman çizelgesi, beklenen adım ve kaynak bağlantıları"],
  ["GET", "/api/v1/transactions", "İşlem listesi: akış, durum, gecikme, arama ve tarih filtreleri"],
  ["GET", "/api/v1/transactions/summary", "Durum özeti"],
  ["GET", "/api/v1/flows", "Akışlar"],
  ["GET", "/api/v1/flows/catalog", "Sektör şablonları"],
  ["POST", "/api/v1/flows/from-template", "Şablondan akış oluştur"],
  ["POST", "/api/v1/connections/{id}/ingest", "Ham veriyi eşleme şablonuyla kabul et"],
  ["POST", "/api/v1/connections/ping", "Bağlantı testi ve istemci sürümü kaydı"],
  ["POST", "/api/v1/agents/heartbeat", "Aktarım ajanı yaşam sinyali"],
  ["POST", "/api/v1/reconciliation/lookup", "Mutabakat için toplu işlem sorgusu"],
  ["POST", "/api/v1/reconciliation/runs", "Mutabakat koşu sonucu"],
  ["GET", "/api/v1/reports/overview", "Süre, uyum, alarm ve veri kalitesi göstergeleri"],
  ["GET", "/api/v1/reports/trend", "Günlük ya da haftalık eğilim"],
  ["GET", "/api/v1/reports/breaches", "Süre ihlali listesi"],
  ["POST", "/api/v1/reports/digest", "Özet e-postasını şimdi gönder"],
];

export default async function DevelopersPage() {
  try { await api<Access>("/access"); }
  catch (e) { return <Message title="Geliştirici sayfası yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  return <>
    <PageHead icon="code" title="Geliştirici" lede="Kaynak sisteminizi bağlamak için gereken her şey: adımlar, olay biçimi, kayıpsızlık kuralları, kütüphaneler ve OpenAPI belgesi.">
      <a className="button" href="/developers/openapi.yaml" download="integraops-openapi.yaml"><Icon name="download" />OpenAPI belgesini indir</a>
    </PageHead>

    <section className="surface">
      <div className="surface-head"><div><h2>Beş adımda ilk bildirim</h2><p>Bir akış, bir anahtar ve tek bir HTTP isteği yeterlidir.</p></div></div>
      <ol className="dev-steps">
        {steps.map((s, i) => <li key={s.title}>
          <span className="dev-step-no" aria-hidden="true">{i + 1}</span>
          <h3>{s.title}</h3>
          <p>{s.text}</p>
        </li>)}
      </ol>
    </section>

    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Olay bildirimi</h2><p>Aynı istek: komut satırı, Python ve Node.</p></div></div>
        <div className="surface-body">
          <CodeTabs tabs={[
            { label: "curl", code: curl },
            { label: "Python", code: python, note: "sdk/python/integraops_sender.py, standart kütüphane dışında bağımlılık yok." },
            { label: "Node", code: node, note: "@integraops/sender, Node 20 ve üzeri." },
          ]} />
          <dl className="dev-fields">
            {fields.map(([name, text]) => <div key={name}><dt><code>{name}</code></dt><dd>{text}</dd></div>)}
          </dl>
        </div>
      </section>

      <section className="surface">
        <div className="surface-head"><div><h2>Olay kaybolmaz</h2><p>Kaynak tarafında iki kural, yanıtlar için bir tablo.</p></div></div>
        <div className="surface-body">
          <ol className="dev-rules">
            <li>
              <h3>İş kaydıyla aynı veritabanı işleminde yazın</h3>
              <p>Gönderilecek olay, kaynağın kendi outbox tablosuna iş kaydıyla aynı transaction içinde yazılır. HTTP çağrısı bu işlemin içinde yapılmaz; ayrı bir gönderici kuyruğu okur.</p>
            </li>
            <li>
              <h3>Aynı kimlikle tekrar gönderin</h3>
              <p>Ağ kesildiğinde kayıt outbox'ta bekler. Bağlantı gelince aynı <code>eventId</code> ile gönderilir; IntegraOps yinelenen olayı tanır ve bir kez işler.</p>
            </li>
          </ol>
          <table className="dev-responses">
            <thead><tr><th>Yanıt</th><th>Anlamı ve kaynağın yapacağı</th></tr></thead>
            <tbody>{responses.map(r => <tr key={r.codes}>
              <td><span className={`badge ${r.tone}`}>{r.codes}</span></td><td><strong>{r.meaning}.</strong> {r.action}.</td>
            </tr>)}</tbody>
          </table>
          <p className="help">Go aktarım ajanı bu kuralları PostgreSQL outbox ya da dizin kaynağı için hazır uygular. Sözleşmenin tamamı <code>docs/entegrasyon-sozlesmesi.md</code> içindedir.</p>
        </div>
      </section>
    </div>

    <section className="surface">
      <div className="surface-head"><div><h2>Alarm webhook'u</h2><p>İmzalı webhook kanalına giden istek. Slack ve Teams kanalları aynı bilgiyi hazır iletiye çevirir.</p></div></div>
      <div className="surface-body dev-webhook">
        <pre className="code-block" tabIndex={0}>{webhook}</pre>
        <div className="dev-webhook-notes">
          <h3>İmzayı doğrulama</h3>
          <p>İmzalanan metin <code>time.delivery.body</code>, yani zaman damgası, teslimat kimliği ve ham gövdenin nokta ile birleşimidir. HMAC-SHA256 özeti kanalın ortak sırrıyla hesaplanır ve <code>X-IntegraOps-Signature</code> değeriyle sabit zamanlı karşılaştırılır. Beş dakikadan eski zaman damgaları reddedilir.</p>
          <h3>Teslimat</h3>
          <p>Her alarm açılışı ve kapanışı ayrı bir teslimattır. Başarısız teslimat üstel beklemeyle sekiz kez denenir; sonrası alarm sayfasından elle yeniden gönderilir.</p>
          <p className="help">Örnek alıcı: <code>samples/webhook/receiver.py</code></p>
        </div>
      </div>
    </section>

    <section className="surface">
      <div className="surface-head"><div><h2>Uç noktalar</h2><p>Entegrasyon yüzeyinin özeti. Şema, parametre ve yanıt gövdeleri OpenAPI belgesinde; Postman ya da Swagger UI doğrudan içe aktarır.</p></div>
        <a className="button secondary small" href="/developers/openapi.yaml" target="_blank" rel="noopener noreferrer"><Icon name="open" size={14} />openapi.yaml</a></div>
      <div className="table-wrap"><table className="dev-endpoints"><thead><tr><th>Yöntem</th><th>Yol</th><th>Amaç</th></tr></thead><tbody>
        {endpoints.map(([method, path, purpose]) => <tr key={method + path}>
          <td><span className={`method ${method.toLowerCase()}`}>{method}</span></td><td><code>{path}</code></td><td>{purpose}</td>
        </tr>)}
      </tbody></table></div>
    </section>
  </>;
}
