import Link from "next/link";
import { api, apiMessage, getFlows } from "@/lib/api";
import type { BreachReport } from "@/lib/pilot-types";
import type { Flow } from "@/lib/types";
import { testLabels } from "@/lib/types";
import { dateInput, Message, number, PageHead, Time } from "@/components/ui";
import { apiFilters } from "@/lib/filters";
import { spanText } from "@/lib/time";

export const dynamic = "force-dynamic";

export default async function ReportsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const str = (k: string) => (typeof params[k] === "string" ? params[k] as string : "");
  const flow = /^[a-z][a-z0-9-]{0,63}$/.test(str("flow")) ? str("flow") : "";
  const from = dateInput.test(str("from")) ? str("from") : "";
  const to = dateInput.test(str("to")) ? str("to") : "";
  const test = Object.hasOwn(testLabels, str("test")) ? str("test") : "exclude";
  const page = /^\d{1,9}$/.test(str("page")) ? str("page") : "1";
  const filters = { flow, from, to, test };
  let report: BreachReport; let flows: Flow[];
  try {
    [report, flows] = await Promise.all([api<BreachReport>("/reports/breaches?" + new URLSearchParams({ ...apiFilters(filters), page })), getFlows()]);
  } catch (e) { return <Message title="Rapor yüklenemedi" description={apiMessage(e)} />; }
  const flowNames = new Map(flows.map(f => [f.code, f.name]));
  const href = (p: number) => "/reports/breaches?" + new URLSearchParams({ ...filters, page: String(p) });
  const totalPages = Math.max(1, Math.ceil(report.total / report.pageSize));
  return <>
    <PageHead crumb={{ href: "/reports", label: "Raporlar" }} title="Süre ihlalleri" lede="Tamamlanmış adımlarda tanınan sürenin aşıldığı durumlar. Süreler, adımın bağlı olduğu sürüm ve takvime göre ölçülür." />
    <section className="surface">
      <form className="filters" action="/reports/breaches">
        <div className="field"><label htmlFor="r-flow">Akış</label><select id="r-flow" name="flow" defaultValue={flow}>
          <option value="">Tüm akışlar</option>{flows.map(f => <option key={f.id} value={f.code}>{f.name}</option>)}</select></div>
        <div className="field"><label htmlFor="r-from">Tamamlanma başlangıcı</label><input id="r-from" name="from" type="date" defaultValue={from} /></div>
        <div className="field"><label htmlFor="r-to">Tamamlanma bitişi</label><input id="r-to" name="to" type="date" defaultValue={to} /></div>
        <div className="field"><label htmlFor="r-test">Test verisi</label><select id="r-test" name="test" defaultValue={test}>
          {Object.entries(testLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
        <button className="button secondary" type="submit">Uygula</button>
      </form>
      <div className="surface-head"><div><h2>Adım bazında</h2><p>Ölçülen adımlar içinde süresi aşılanların payı ve ortalama gerçekleşme süresi.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Akış</th><th>Adım</th><th className="right">Ölçülen</th><th className="right">Aşılan</th><th className="right">Oran</th><th className="right">Ortalama süre</th><th className="right">Tanınan</th></tr></thead><tbody>
        {report.byStep.map((s, i) => <tr key={i}><td>{flowNames.get(s.flow) ?? s.flow}</td><td className="mono">{s.step_code}</td>
          <td className="right num">{number(s.total)}</td><td className="right num">{number(s.breached)}</td>
          <td className="right num">{s.total ? Math.round(100 * s.breached / s.total) : 0}%</td>
          <td className="right num">{spanText(Number(s.avg_elapsed))}</td><td className="right num">{spanText(s.allowance)}</td></tr>)}
      </tbody></table></div>
      {report.byStep.length === 0 && <div className="empty"><h3>Henüz ölçülmüş adım yok</h3><p>Bir adım tamamlandığında gerçekleşme süresi burada toplanır.</p></div>}
    </section>
    <section className="surface">
      <div className="surface-head"><div><h2>İhlal listesi</h2></div><span className="count">{number(report.total)} ihlal</span></div>
      <div className="table-wrap"><table><thead><tr><th>İşlem</th><th>Akış</th><th>Adım</th><th>Grup</th><th className="right">Geçen</th><th className="right">Tanınan</th><th>Tamamlanma</th></tr></thead><tbody>
        {report.items.map((r, i) => <tr key={i}>
          <td><Link className="ident" href={`/transactions/${encodeURIComponent(r.flow)}/${encodeURIComponent(r.business_id)}`} prefetch={false}>{r.business_id}</Link>{r.is_test && <span className="badge test">Test</span>}</td>
          <td>{flowNames.get(r.flow) ?? r.flow}</td><td className="mono">{r.step_code}</td><td>{r.group_code ?? <span className="muted">—</span>}</td>
          <td className="right num late">{spanText(r.elapsed_seconds)}</td><td className="right num">{spanText(r.allowance_seconds)}</td>
          <td className="nowrap"><Time value={r.completed_at} /></td></tr>)}
      </tbody></table></div>
      {report.items.length === 0 && <div className="empty"><h3>İhlal yok</h3><p>Seçilen aralıkta tanınan süresi aşılan tamamlanmış adım bulunmuyor.</p></div>}
      <div className="pagination"><span>sayfa {report.page} / {number(totalPages)}</span><nav aria-label="Sayfalama">
        {report.page > 1 && <Link className="button secondary small" href={href(report.page - 1)} prefetch={false}>Önceki</Link>}
        {report.page * report.pageSize < report.total && <Link className="button secondary small" href={href(report.page + 1)} prefetch={false}>Sonraki</Link>}
      </nav></div>
    </section>
  </>;
}
