import Link from "next/link";
import { apiMessage, getFlows, getSummary, getTransactions } from "@/lib/api";
import type { Filters, Flow, Summary, TransactionPage } from "@/lib/types";
import { Badge, DeadlineMeter, date, delayLabels, listHref, Message, number, PageHead, statusLabels, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";

export const dynamic = "force-dynamic";
type Search = Record<string, string | string[] | undefined>;
const one = (value: Search[string]) => (Array.isArray(value) ? value[0] : value) ?? "";

export default async function Home({ searchParams }: { searchParams: Promise<Search> }) {
  const raw = await searchParams;
  const pageText = one(raw.page) || "1";
  const filters: Filters = {
    flow: one(raw.flow).trim(), status: one(raw.status), delay: one(raw.delay),
    page: Number(pageText), pageSize: Number(one(raw.pageSize) || "20"),
  };
  if (!/^\d+$/.test(pageText) || !Number.isSafeInteger(filters.page) || filters.page < 1 || filters.page > 2147483647
    || ![10, 20, 50, 100].includes(filters.pageSize)
    || (filters.flow && !/^[a-z][a-z0-9-]{0,63}$/.test(filters.flow))
    || (filters.status && !Object.hasOwn(statusLabels, filters.status))
    || (filters.delay && !Object.hasOwn(delayLabels, filters.delay))) {
    return <Message title="Filtreler geçerli değil" description="Listeyi açıp filtrelerinizi yeniden seçin." />;
  }

  let data: [Flow[], Summary, TransactionPage];
  try { data = await Promise.all([getFlows(), getSummary(filters.flow), getTransactions(filters)]); }
  catch (error) { return <Message title="Veriler yüklenemedi" description={apiMessage(error)} />; }
  const [flows, summary, result] = data;
  const flowNames = new Map(flows.map(flow => [flow.code, flow.name]));
  if (filters.flow && !flowNames.has(filters.flow)) {
    return <Message title="Akış bulunamadı" description="Seçilen akış bu projenin akış listesinde bulunmuyor." />;
  }
  const strip = [
    { label: "Toplam", value: summary.total, status: "", delay: "", tone: "total" },
    { label: "İşlenmeyi bekleyen", value: summary.pending, status: "pending", delay: "", tone: "" },
    { label: "Devam eden", value: summary.inProgress, status: "inProgress", delay: "", tone: "info" },
    { label: "Gecikmiş", value: summary.overdue, status: "", delay: "overdue", tone: "warn" },
    { label: "Hata bildirilen", value: summary.failed, status: "failed", delay: "", tone: "bad" },
    { label: "Tamamlanan", value: summary.completed, status: "completed", delay: "", tone: "good" },
  ];
  const activeStrip = (m: typeof strip[number]) => filters.status === m.status && filters.delay === m.delay;
  const scopeName = flowNames.get(filters.flow);

  return <>
    <PageHead title="İşlemler" lede={scopeName ? `${scopeName} akışındaki işlemler ve bekledikleri adımlar.` : "Bütün akışlardaki işlemler ve bekledikleri adımlar."}>
      <RefreshControls />
    </PageHead>

    <nav className="status-strip" aria-label="Durum özeti">
      {strip.map(m => <Link key={m.label} prefetch={false} className={m.tone} aria-current={activeStrip(m) ? "true" : undefined}
        href={listHref(filters, { status: m.status, delay: m.delay, page: 1 })}>
        <span className="label">{m.label}</span><strong>{number(m.value)}</strong>
      </Link>)}
    </nav>
    <p className="strip-note"><span>Gecikmiş işlemler devam edenlerin içinde sayılır. Gecikme, hata bildirimi değildir.</span>
      <span>Özet {date(summary.queriedAt)}</span></p>

    <section className="surface">
      <form key={listHref(filters)} className="filters" method="get" action="/">
        <div className="field"><label htmlFor="f-flow">Akış</label><select id="f-flow" name="flow" defaultValue={filters.flow}>
          <option value="">Tüm akışlar</option>{flows.map(flow => <option key={flow.id} value={flow.code}>{flow.name}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-status">İşlem durumu</label><select id="f-status" name="status" defaultValue={filters.status}>
          <option value="">Tüm durumlar</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-delay">Süre durumu</label><select id="f-delay" name="delay" defaultValue={filters.delay}>
          <option value="">Tümü</option>{Object.entries(delayLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-size">Sayfa boyutu</label><select id="f-size" name="pageSize" defaultValue={filters.pageSize}>
          {[10, 20, 50, 100].map(size => <option key={size} value={size}>{size} kayıt</option>)}
        </select></div>
        <button className="button secondary" type="submit">Uygula</button>
        <Link className="reset" href="/" prefetch={false}>Temizle</Link>
      </form>
      <div className="table-wrap"><table><thead><tr>
        <th>İşlem</th><th>Akış</th><th>Durum</th><th>Beklenen adım</th><th>Süre</th><th>Oluşturulma</th>
      </tr></thead><tbody>
        {result.items.map(item => <tr key={item.id}>
          <td><Link className="ident" title={item.transactionId} prefetch={false}
            href={`/transactions/${encodeURIComponent(item.flow)}/${encodeURIComponent(item.transactionId)}`}>{item.transactionId}</Link></td>
          <td>{flowNames.get(item.flow) || item.flow}<small className="mono">{item.flow}</small></td>
          <td><Badge value={item.status} /></td>
          <td>{item.delay.step ? <span className="mono">{item.delay.step}</span> : <span className="muted">—</span>}</td>
          <td>{item.delay.state === "waiting" || item.delay.state === "overdue"
            ? <DeadlineMeter startedAt={item.delay.startedAt} deadlineAt={item.delay.deadlineAt} state={item.delay.state} />
            : item.delay.state === "none" ? <span className="muted">—</span> : <Badge value={item.delay.state} delay />}</td>
          <td className="nowrap"><Time value={item.createdAt} /></td>
        </tr>)}
      </tbody></table></div>
      {result.items.length === 0 && <div className="empty"><h3>{result.totalCount ? "Bu sayfada kayıt yok" : "Bu filtrede işlem yok"}</h3>
        <p>Filtreleri değiştirin ya da bu projeye yeni bir durum bildirimi gönderin.</p>
        {filters.page > 1 && <Link className="button secondary" href={listHref(filters, { page: 1 })} prefetch={false}>İlk sayfaya dön</Link>}</div>}
      <div className="pagination"><span>{result.totalPages ? `${number(result.totalCount)} kayıt, sayfa ${result.page} / ${number(result.totalPages)}` : "0 kayıt"}</span>
        <nav aria-label="Sayfalama">
          {filters.page > 1 && <Link className="button secondary small" href={listHref(filters, { page: filters.page - 1 })} prefetch={false}>Önceki</Link>}
          {result.hasNextPage && <Link className="button secondary small" href={listHref(filters, { page: filters.page + 1 })} prefetch={false}>Sonraki</Link>}
        </nav>
      </div>
    </section>
  </>;
}
