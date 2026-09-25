import Link from "next/link";
import { apiMessage, getAccess, getFlows, getSavedViews, getSummary, getTransactions } from "@/lib/api";
import type { Filters, Flow, Summary, TransactionPage } from "@/lib/types";
import { testLabels } from "@/lib/types";
import type { SavedView } from "@/lib/pilot-types";
import { Badge, DeadlineMeter, date, dateInput, delayLabels, Empty, Explain, filterQuery, listHref, Message, number, PageHead, statusLabels, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import SavedViews from "@/components/saved-views";
import Pager from "@/components/pager";
import Icon from "@/components/icons";
import { Donut, TrendChart } from "@/components/charts";
import { api } from "@/lib/api";
import type { ReportTrend } from "@/lib/pilot-types";
import { apiFilters } from "@/lib/filters";

export const dynamic = "force-dynamic";
type Search = Record<string, string | string[] | undefined>;
const one = (value: Search[string]) => (Array.isArray(value) ? value[0] : value) ?? "";

export default async function Home({ searchParams }: { searchParams: Promise<Search> }) {
  const raw = await searchParams;
  const pageText = one(raw.page) || "1";
  const filters: Filters = {
    flow: one(raw.flow).trim(), status: one(raw.status), delay: one(raw.delay),
    page: Number(pageText), pageSize: Number(one(raw.pageSize) || "20"),
    search: one(raw.search).trim().slice(0, 128), from: one(raw.from), to: one(raw.to), test: one(raw.test) || "exclude",
  };
  if (!/^\d+$/.test(pageText) || !Number.isSafeInteger(filters.page) || filters.page < 1 || filters.page > 2147483647
    || ![10, 20, 50, 100].includes(filters.pageSize)
    || (filters.flow && !/^[a-z][a-z0-9-]{0,63}$/.test(filters.flow))
    || (filters.status && !Object.hasOwn(statusLabels, filters.status))
    || (filters.delay && !Object.hasOwn(delayLabels, filters.delay))
    || (filters.from && !dateInput.test(filters.from)) || (filters.to && !dateInput.test(filters.to))
    || !Object.hasOwn(testLabels, filters.test)) {
    return <Message title="Filtreler geçerli değil" description="Listeyi açıp filtrelerinizi yeniden seçin." icon="filter" />;
  }

  let data: [Flow[], Summary, TransactionPage, SavedView[], boolean, ReportTrend | null];
  try {
    data = await Promise.all([getFlows(), getSummary(filters.flow, filters.test), getTransactions(apiFilters(filters)),
      getSavedViews("transactions"), getAccess().then(a => a.role !== "viewer"),
      // Eğilim kartları isteğe bağlıdır: rapor servisi yanıt vermezse liste yine açılır.
      api<ReportTrend>("/reports/trend?" + new URLSearchParams({ flow: filters.flow, test: filters.test, period: "day", buckets: "14" })).catch(() => null)]);
  } catch (error) { return <Message title="Veriler yüklenemedi" description={apiMessage(error)} icon="alert" />; }
  const [flows, summary, result, views, canExport, trend] = data;
  const dayLabel = (iso: string) => new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit", timeZone: "Europe/Istanbul" }).format(new Date(iso));
  const trendTx = trend?.buckets.reduce((a, b) => a + b.transactions, 0) ?? 0;
  const trendAlarms = trend?.buckets.reduce((a, b) => a + b.alarms, 0) ?? 0;
  const flowNames = new Map(flows.map(flow => [flow.code, flow.name]));
  if (filters.flow && !flowNames.has(filters.flow)) {
    return <Message title="Akış bulunamadı" description="Seçilen akış bu projenin akış listesinde bulunmuyor." icon="workflow" />;
  }
  const strip = [
    { label: "Toplam", value: summary.total, status: "", delay: "", tone: "total" },
    { label: "Bekleyen", value: summary.pending, status: "pending", delay: "", tone: "" },
    { label: "Devam eden", value: summary.inProgress, status: "inProgress", delay: "", tone: "info" },
    { label: "Gecikmiş", value: summary.overdue, status: "", delay: "overdue", tone: "warn" },
    { label: "Hatalı", value: summary.failed, status: "failed", delay: "", tone: "bad" },
    { label: "Tamamlanan", value: summary.completed, status: "completed", delay: "", tone: "good" },
    { label: "İptal edilen", value: summary.cancelled, status: "cancelled", delay: "", tone: "" },
  ];
  const activeStrip = (m: typeof strip[number]) => filters.status === m.status && filters.delay === m.delay;
  // Halka: gecikmiş olanlar devam edenlerin içinde sayıldığından ayrı dilimdir; benzer renkler yan yana gelmez.
  const donut = [
    { label: "Tamamlanan", value: summary.completed, tone: "good" as const },
    { label: "Gecikmiş", value: summary.overdue, tone: "warn" as const },
    { label: "Devam eden", value: Math.max(0, summary.inProgress - summary.overdue), tone: "info" as const },
    { label: "Hatalı", value: summary.failed, tone: "bad" as const },
    { label: "Bekleyen ve iptal", value: summary.pending + summary.cancelled, tone: "mute" as const },
  ];
  const scopeName = flowNames.get(filters.flow);
  const query = filterQuery({ ...filters, page: 1 });
  const filtered = Boolean(filters.flow || filters.status || filters.delay || filters.search || filters.from || filters.to || filters.test !== "exclude");
  const totalPages = Math.max(1, result.totalPages || 1);

  return <>
    <PageHead icon="activity" title="İşlemler" lede={scopeName ? `${scopeName} akışındaki işlemler.` : "Bütün akışlardaki işlemler ve bekledikleri adımlar."}>
      {canExport && <a className="button secondary" href={`/export/transactions?${query}`}><Icon name="download" />CSV indir</a>}
      <RefreshControls />
    </PageHead>

    <nav className="status-strip" aria-label="Durum özeti">
      {strip.map(m => <Link key={m.label} prefetch={false} className={m.tone} aria-current={activeStrip(m) ? "true" : undefined}
        href={listHref(filters, { status: m.status, delay: m.delay, page: 1 })}>
        {m.tone === "total" && <Donut segments={donut} size={72} thickness={10} title="İşlem dağılımı" center={<strong>{number(m.value)}</strong>} />}
        {m.tone === "total" ? <div className="text"><span className="label">{m.label}</span></div>
          : <><span className="label">{m.label}</span><strong>{number(m.value)}</strong></>}
      </Link>)}
    </nav>
    <p className="strip-note"><span>Gecikmiş işlemler devam edenlerin içinde sayılır; gecikme hata değildir.</span>
      <span>Özet {date(summary.queriedAt)}, {testLabels[filters.test as keyof typeof testLabels].toLowerCase()}</span></p>
    {trend && trend.buckets.length > 1 && <div className="trend-card">
      <div className="trend-head">
        <div><h2>Son 14 gün</h2><p>Günlük yeni işlem ve açılan alarm sayısı.</p></div>
        <div className="trend-stats">
          <div className="trend-stat"><i className="line" style={{ background: "var(--chart)" }} /><div><strong>{number(trendTx)}</strong><span>yeni işlem</span></div></div>
          <div className="trend-stat"><i style={{ background: "var(--chart-bad)" }} /><div><strong>{number(trendAlarms)}</strong><span>açılan alarm</span></div></div>
        </div>
      </div>
      <TrendChart primary={{ name: "Yeni işlem", tone: "signal" }} secondary={{ name: "Açılan alarm", tone: "bad" }}
        points={trend.buckets.map(b => ({ label: dayLabel(b.start), a: b.transactions, b: b.alarms }))} />
    </div>}

    <section className="surface">
      <form key={listHref(filters)} className="filters" method="get" action="/">
        <div className="field search"><label htmlFor="f-search">İşlem numarası</label>
          <div className="control"><Icon name="search" /><input id="f-search" name="search" defaultValue={filters.search} maxLength={128} placeholder="ORDER-10092" /></div></div>
        <div className="field"><label htmlFor="f-flow">Akış</label><select id="f-flow" name="flow" defaultValue={filters.flow}>
          <option value="">Tüm akışlar</option>{flows.map(flow => <option key={flow.id} value={flow.code}>{flow.name}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-status">Durum</label><select id="f-status" name="status" defaultValue={filters.status}>
          <option value="">Tüm durumlar</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-delay">Süre</label><select id="f-delay" name="delay" defaultValue={filters.delay}>
          <option value="">Tümü</option>{Object.entries(delayLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></div>
        <div className="field"><label htmlFor="f-from">Başlangıç</label><input id="f-from" name="from" type="date" defaultValue={filters.from} /></div>
        <div className="field"><label htmlFor="f-to">Bitiş</label><input id="f-to" name="to" type="date" defaultValue={filters.to} /></div>
        <div className="field"><label htmlFor="f-test">Test verisi</label><select id="f-test" name="test" defaultValue={filters.test}>
          {Object.entries(testLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></div>
        {filters.pageSize !== 20 && <input type="hidden" name="pageSize" value={filters.pageSize} />}
        <button className="button secondary" type="submit"><Icon name="filter" />Uygula</button>
        {filtered && <Link className="reset" href="/" prefetch={false}><Icon name="x" size={14} />Temizle</Link>}
      </form>
      <SavedViews kind="transactions" views={views} basePath="/" currentQuery={query} />
      {result.items.length > 0 && <div className="table-wrap"><table><thead><tr>
        <th>İşlem</th><th>Akış</th><th>Durum</th><th>Beklenen adım</th><th>Süre</th><th>Oluşturulma</th>
      </tr></thead><tbody>
        {result.items.map(item => <tr key={item.id}>
          <td><Link className="ident" title={item.transactionId} prefetch={false}
            href={`/transactions/${encodeURIComponent(item.flow)}/${encodeURIComponent(item.transactionId)}`}>{item.transactionId}</Link>
            {item.isTest && <span className="badge test">Test</span>}</td>
          <td>{flowNames.get(item.flow) || item.flow}</td>
          <td><Badge value={item.status} /></td>
          <td>{item.delay.step ? <span className="mono">{item.delay.step}</span> : <span className="muted">—</span>}</td>
          <td>{item.delay.state === "waiting" || item.delay.state === "overdue"
            ? <DeadlineMeter startedAt={item.delay.startedAt} deadlineAt={item.delay.deadlineAt} state={item.delay.state} />
            : item.delay.state === "none" ? <span className="muted">—</span> : <Badge value={item.delay.state} delay />}</td>
          <td className="nowrap"><Time value={item.createdAt} /></td>
        </tr>)}
      </tbody></table></div>}
      {result.items.length === 0 && <Empty title={result.totalCount ? "Bu sayfada kayıt yok" : "Bu filtrede işlem yok"}
        action={filters.page > 1 ? <Link className="button secondary" href={listHref(filters, { page: 1 })} prefetch={false}>İlk sayfaya dön</Link> : undefined}>
        Filtreleri değiştirin ya da bu projeye yeni bir durum bildirimi gönderin.</Empty>}
      <Pager page={filters.page} totalPages={totalPages} total={result.totalCount} pageSize={filters.pageSize} unit="işlem" href={p => listHref(filters, { page: p })}
        sizes={[10, 20, 50, 100].map(size => ({ value: size, href: listHref(filters, { pageSize: size, page: 1 }) }))} />
    </section>
    <Explain title="Sayılar nasıl hesaplanır?">
      <p><strong>Bekleyen:</strong> bildirimi alınmış, henüz işlenmemiş işlemler.</p>
      <p><strong>Devam eden:</strong> en az bir adımı gerçekleşmiş, sonraki adımı beklenen işlemler. Gecikmiş olanlar bu sayının içindedir.</p>
      <p><strong>Gecikmiş:</strong> beklenen adım tanınan sürede bildirilmedi. Kaynak sistem hata bildirmediği sürece işlem başarısız sayılmaz.</p>
    </Explain>
  </>;
}
