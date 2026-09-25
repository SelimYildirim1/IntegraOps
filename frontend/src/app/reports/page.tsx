import Link from "next/link";
import { api, apiMessage, getFlows } from "@/lib/api";
import type { ReportOverview, ReportTrend } from "@/lib/pilot-types";
import type { Flow } from "@/lib/types";
import { testLabels } from "@/lib/types";
import { Empty, Message, number, PageHead, Time } from "@/components/ui";
import { Columns, Donut, Ring } from "@/components/charts";
import PagedTable from "@/components/paged-table";
import Icon from "@/components/icons";
import { spanText } from "@/lib/time";

export const dynamic = "force-dynamic";
const ranges: Record<string, { days: number; label: string }> = { "7": { days: 7, label: "Son 7 gün" }, "30": { days: 30, label: "Son 30 gün" }, "90": { days: 90, label: "Son 90 gün" } };

function pct(part: number, whole: number) { return whole ? `${Math.round(1000 * part / whole) / 10}%` : "—"; }
function dur(seconds: number | null | undefined) { return seconds === null || seconds === undefined ? "—" : spanText(Number(seconds)); }

export default async function ReportsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const str = (k: string) => (typeof params[k] === "string" ? params[k] as string : "");
  const range = Object.hasOwn(ranges, str("range")) ? str("range") : "30";
  const flow = /^[a-z][a-z0-9-]{0,63}$/.test(str("flow")) ? str("flow") : "";
  const test = Object.hasOwn(testLabels, str("test")) ? str("test") : "exclude";
  const from = new Date(Date.now() - ranges[range].days * 86400000).toISOString();
  const trendPeriod = ranges[range].days > 30 ? "week" : "day";
  let overview: ReportOverview; let trend: ReportTrend; let flows: Flow[];
  try {
    [overview, trend, flows] = await Promise.all([
      api<ReportOverview>("/reports/overview?" + new URLSearchParams({ from, flow, test })),
      api<ReportTrend>("/reports/trend?" + new URLSearchParams({ flow, test, period: trendPeriod, buckets: String(trendPeriod === "week" ? Math.ceil(ranges[range].days / 7) : ranges[range].days) })),
      getFlows()]);
  } catch (e) { return <Message title="Rapor yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const flowNames = new Map(flows.map(f => [f.code, f.name]));
  const t = overview.totals, a = overview.alarms, c = overview.compliance, q = overview.quality;
  const label = (iso: string) => new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit", timeZone: "Europe/Istanbul" }).format(new Date(iso));
  const compliance = c.measured ? (c.measured - c.breached) / c.measured : 0;
  const tiles: { name: string; value: string; note?: string; attention?: boolean; figure?: React.ReactNode }[] = [
    { name: "Yeni işlem", value: number(t.transactions), note: `${number(t.completed)} tamamlandı, ${number(t.failed)} hata`,
      figure: <Donut size={48} thickness={7} title="İşlem sonuçları" segments={[{ label: "Tamamlandı", value: t.completed, tone: "good" }, { label: "Devam ediyor", value: t.in_progress, tone: "info" }, { label: "Hata", value: t.failed, tone: "bad" }]} /> },
    { name: "Tamamlanma oranı", value: pct(t.completed, t.transactions), note: `${number(t.in_progress)} devam ediyor`,
      figure: <Ring ratio={t.transactions ? t.completed / t.transactions : 0} tone="good" label="Tamamlanma oranı" /> },
    { name: "Süre hedefine uyum", value: pct(c.measured - c.breached, c.measured), note: `${number(c.measured)} ölçülen adım, ${number(c.breached)} aşım`, attention: c.measured > 0 && c.breached / c.measured > 0.1,
      figure: <Ring ratio={compliance} tone={c.measured > 0 && compliance < 0.9 ? "warn" : "signal"} label="Süre hedefine uyum" /> },
    { name: "Açılan alarm", value: number(a.overdue + a.failed), note: `${number(a.overdue)} süre aşımı, ${number(a.failed)} hata, ${number(a.still_open)} hâlâ açık`, attention: a.still_open > 0,
      figure: <Donut size={48} thickness={7} title="Alarm türleri" segments={[{ label: "Süre aşımı", value: a.overdue, tone: "warn" }, { label: "Hata", value: a.failed, tone: "bad" }]} /> },
    { name: "Alarm görülme süresi", value: dur(a.mtta_seconds), note: `ortanca ${dur(a.mtta_p50_seconds)}, ${number(a.acknowledged)} görüldü` },
    { name: "Alarm çözüm süresi", value: dur(a.mttr_seconds), note: `ortanca ${dur(a.mttr_p50_seconds)}, ${number(a.resolved)} kapandı` },
  ];
  const qualityTiles = [
    { name: "Geç bildirim", value: number(q.late_events), note: `${number(q.events)} olayda; kaynak zamanından 5 dk sonra ulaşanlar. p95 gecikme ${dur(q.lag_p95_seconds)}`, attention: q.late_events > 0 },
    { name: "Tutarsız zaman", value: number(q.out_of_order_steps), note: "Önceki adımdan önce gerçekleşmiş görünen adımlar", attention: q.out_of_order_steps > 0 },
    { name: "İleri tarihli olay", value: number(q.future_events), note: "Kaynak zamanı ulaşma anından 1 dakikadan fazla ileride", attention: q.future_events > 0 },
    { name: "Mutabakat uyuşmazlığı", value: number(q.open_discrepancies), note: `${number(q.discrepancies_found)} bu dönemde bulundu, açık olanlar`, attention: q.open_discrepancies > 0 },
  ];
  return <>
    <PageHead icon="chart" title="Raporlar" lede={`${ranges[range].label}: süreçlerin hızı, gecikmeler, alarm yanıt süreleri ve veri güvenilirliği.`}>
      <Link className="button secondary" href={`/reports/breaches?${new URLSearchParams({ flow, test })}`} prefetch={false}><Icon name="clock" />Süre ihlalleri</Link>
    </PageHead>
    <section className="surface">
      <form className="filters" action="/reports">
        <div className="field"><label htmlFor="r-range">Dönem</label><select id="r-range" name="range" defaultValue={range}>
          {Object.entries(ranges).map(([k, v]) => <option key={k} value={k}>{v.label}</option>)}</select></div>
        <div className="field"><label htmlFor="r-flow">Akış</label><select id="r-flow" name="flow" defaultValue={flow}>
          <option value="">Tüm akışlar</option>{flows.map(f => <option key={f.id} value={f.code}>{f.name}</option>)}</select></div>
        <div className="field"><label htmlFor="r-test">Test verisi</label><select id="r-test" name="test" defaultValue={test}>
          {Object.entries(testLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
        <button className="button secondary" type="submit"><Icon name="filter" />Uygula</button>
      </form>
    </section>
    <div className="stat-grid six">{tiles.map(x => <div key={x.name} className={`stat ${x.attention ? "attention" : ""}${x.figure ? " with-figure" : ""}`}>
      <div><p>{x.name}</p><strong>{x.value}</strong>{x.note && <span className="hint">{x.note}</span>}</div>
      {x.figure && <span className="figure">{x.figure}</span>}
    </div>)}</div>

    <div className="two-col">
      <section className="surface"><div className="surface-head"><div><h2>İşlem hacmi</h2><p>{trendPeriod === "week" ? "Haftalık" : "Günlük"} yeni işlem sayısı.</p></div></div>
        <div className="surface-body"><Columns series={[{ name: "Yeni işlem", tone: "signal" }]} points={trend.buckets.map(b => ({ label: label(b.start), values: [b.transactions], hint: `${b.alarms} alarm` }))} /></div></section>
      <section className="surface"><div className="surface-head"><div><h2>Süre aşımı oranı</h2><p>Ölçülen adımlar içinde tanınan süreyi aşanların payı.</p></div></div>
        <div className="surface-body"><Columns unit="%" series={[{ name: "Süre aşımı oranı", tone: "warn" }]} points={trend.buckets.map(b => ({ label: label(b.start), values: [b.measured ? Math.round(1000 * b.breached / b.measured) / 10 : 0], hint: `${b.breached}/${b.measured} adım, ortanca ${dur(b.p50_seconds)}` }))} /></div></section>
    </div>

    <section className="surface">
      <div className="surface-head"><div><h2>Adım süreleri</h2><p>Bir önceki adımdan itibaren geçen süre; takvimli akışlarda çalışma saati. p95: adımların %95'i bu sürede tamamlandı.</p></div><span className="count">{overview.steps.length} adım</span></div>
      <PagedTable unit="adım"
        head={<tr><th>Akış</th><th>Adım</th><th className="right">Ölçülen</th><th className="right">Uyum</th><th className="right">Ortanca</th><th className="right">p90</th><th className="right">p95</th><th className="right">En uzun</th><th className="right">Tanınan</th></tr>}
        rows={overview.steps.map((s, i) => <tr key={i}><td>{flowNames.get(s.flow) ?? s.flow}</td><td className="mono">{s.step_code}</td><td className="right num">{number(s.measured)}</td>
          <td className={`right num ${Number(s.compliance) < 90 ? "late" : ""}`}>{s.compliance}%</td><td className="right num">{dur(s.p50)}</td><td className="right num">{dur(s.p90)}</td>
          <td className={`right num ${Number(s.p95) > Number(s.allowance) ? "late" : ""}`}>{dur(s.p95)}</td><td className="right num">{dur(s.max)}</td><td className="right num">{dur(s.allowance)}</td></tr>)}
        empty={<Empty icon="clock" title="Bu dönemde ölçülmüş adım yok">Adımlar tamamlandıkça süreleri burada toplanır.</Empty>} />
    </section>

    <div className="two-col">
      <section className="surface"><div className="surface-head"><div><h2>En sık süre aşan adımlar</h2><p>Süre aşımı alarmı açılan adımlar ve ortalama gecikme.</p></div></div>
        <PagedTable pageSize={10} unit="adım"
          head={<tr><th>Akış</th><th>Adım</th><th className="right">Alarm</th><th className="right">Ort. gecikme</th></tr>}
          rows={overview.topOverdue.map((r, i) => <tr key={i}><td>{flowNames.get(r.flow) ?? r.flow}</td><td className="mono">{r.step_code}</td><td className="right num">{number(r.count)}</td><td className="right num">{dur(r.avg_late_seconds)}</td></tr>)}
          empty={<Empty icon="check" title="Süre aşımı alarmı yok" />} /></section>
      <section className="surface"><div className="surface-head"><div><h2>En sık hata veren adımlar</h2><p>Kaynak sistemin bildirdiği hatalar, açıklama başlangıcına göre gruplanmış.</p></div></div>
        <PagedTable pageSize={10} unit="adım"
          head={<tr><th>Akış</th><th>Adım</th><th>Hata</th><th className="right">Alarm</th></tr>}
          rows={overview.topFailed.map((r, i) => <tr key={i}><td>{flowNames.get(r.flow) ?? r.flow}</td><td className="mono">{r.step_code}</td><td><small>{r.error || "—"}</small></td><td className="right num">{number(r.count)}</td></tr>)}
          empty={<Empty icon="check" title="Hata alarmı yok" />} /></section>
    </div>

    <div className="two-col">
      <section className="surface"><div className="surface-head"><div><h2>Tekrarlayan sorunlar</h2><p>Dönem içinde iki ya da daha çok alarm açılan işlemler.</p></div></div>
        <PagedTable pageSize={10} unit="işlem"
          head={<tr><th>İşlem</th><th>Akış</th><th className="right">Alarm</th><th>Son açılış</th></tr>}
          rows={overview.recurring.map((r, i) => <tr key={i}><td><Link className="ident" href={`/transactions/${encodeURIComponent(r.flow)}/${encodeURIComponent(r.business_id)}`} prefetch={false}>{r.business_id}</Link></td>
            <td>{flowNames.get(r.flow) ?? r.flow}</td><td className="right num">{r.alarms} <small>({r.overdue} süre, {r.failed} hata)</small></td><td className="nowrap"><Time value={r.last_opened_at} /></td></tr>)}
          empty={<Empty icon="check" title="Tekrarlayan sorun yok" />} /></section>
      <section className="surface"><div className="surface-head"><div><h2>Gruplara göre</h2><p>Bildirimdeki grup alanına göre işlem ve süre uyumu.</p></div></div>
        <PagedTable pageSize={10} unit="grup"
          head={<tr><th>Grup</th><th className="right">İşlem</th><th className="right">Ölçülen adım</th><th className="right">Uyum</th></tr>}
          rows={overview.byGroup.map((g, i) => <tr key={i}><td className="mono">{g.group || "—"}</td><td className="right num">{number(g.transactions)}</td><td className="right num">{number(g.measured)}</td><td className="right num">{g.compliance === null ? "—" : `${g.compliance}%`}</td></tr>)}
          empty={<Empty icon="layers" title="Grup verisi yok" />} /></section>
    </div>

    <section className="surface"><div className="surface-head"><div><h2>Veri kalitesi</h2><p>Kaynak sistemlerden gelen verinin güvenilirliği. Bunlar iş sorunları değil, entegrasyon sorunlarıdır.</p></div>
      <Link className="button quiet small" href="/reconciliation" prefetch={false}><Icon name="scale" size={14} />Mutabakat</Link></div>
      <div className="surface-body"><div className="stat-grid" style={{ marginBottom: 0 }}>{qualityTiles.map(x => <div key={x.name} className={`stat ${x.attention ? "attention" : ""}`}><p>{x.name}</p><strong>{x.value}</strong><span className="hint">{x.note}</span></div>)}</div></div>
    </section>
  </>;
}
