import Link from "next/link";
import { api, apiMessage, getFlows } from "@/lib/api";
import type { ReconciliationDiscrepancies, ReconciliationRuns } from "@/lib/pilot-types";
import type { Flow } from "@/lib/types";
import { Empty, Explain, Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import PagedTable from "@/components/paged-table";
import Pager from "@/components/pager";
import Icon from "@/components/icons";
import { Columns } from "@/components/charts";

export const dynamic = "force-dynamic";

const kindNames: Record<string, string> = {
  missing_transaction: "IntegraOps'a hiç bildirilmemiş", missing_step: "Adım bildirilmemiş", status_mismatch: "Durum uyuşmazlığı",
};

export default async function ReconciliationPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const str = (k: string) => (typeof params[k] === "string" ? params[k] as string : "");
  const state = ["open", "resolved", "all"].includes(str("state")) ? str("state") : "open";
  const flow = /^[a-z][a-z0-9-]{0,63}$/.test(str("flow")) ? str("flow") : "";
  const kind = Object.hasOwn(kindNames, str("kind")) ? str("kind") : "";
  const page = /^\d{1,9}$/.test(str("page")) ? str("page") : "1";
  let discrepancies: ReconciliationDiscrepancies; let runs: ReconciliationRuns; let flows: Flow[];
  try {
    [discrepancies, runs, flows] = await Promise.all([
      api<ReconciliationDiscrepancies>("/reconciliation/discrepancies?" + new URLSearchParams({ state, flow, kind, page })),
      api<ReconciliationRuns>("/reconciliation/runs?" + new URLSearchParams({ flow })), getFlows()]);
  } catch (e) { return <Message title="Mutabakat yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const flowNames = new Map(flows.map(f => [f.code, f.name]));
  const href = (p: number) => "/reconciliation?" + new URLSearchParams({ state, flow, kind, page: String(p) });
  const totalPages = Math.max(1, Math.ceil(discrepancies.total / discrepancies.pageSize));
  return <>
    <PageHead icon="scale" title="Mutabakat" lede="Kaynak sistemdeki işlemlerin IntegraOps kayıtlarıyla dönemsel karşılaştırması.">
      <RefreshControls />
    </PageHead>
    <div className="stat-grid">
      <div className={`stat ${discrepancies.counts.open > 0 ? "attention" : ""}`}><p>Açık uyuşmazlık</p><strong>{number(discrepancies.counts.open)}</strong></div>
      <div className={`stat ${discrepancies.counts.missing_transactions > 0 ? "attention" : ""}`}><p>Hiç bildirilmemiş işlem</p><strong>{number(discrepancies.counts.missing_transactions)}</strong></div>
      {runs.lastRuns.map(r => <div className="stat" key={r.name + r.flow}><p>Son koşu, {r.name}, {flowNames.get(r.flow) ?? r.flow}</p><strong className="time-stat"><Time value={r.created_at} /></strong>
        <span className="hint">Aralık sonu <Time value={r.window_to} /></span></div>)}
    </div>
    <section className="surface">
      <form className="filters" action="/reconciliation">
        <div className="field"><label htmlFor="d-state">Durum</label><select id="d-state" name="state" defaultValue={state}>
          <option value="open">Açık</option><option value="resolved">Çözülen</option><option value="all">Tümü</option></select></div>
        <div className="field"><label htmlFor="d-flow">Akış</label><select id="d-flow" name="flow" defaultValue={flow}>
          <option value="">Tüm akışlar</option>{flows.map(f => <option key={f.id} value={f.code}>{f.name}</option>)}</select></div>
        <div className="field"><label htmlFor="d-kind">Tür</label><select id="d-kind" name="kind" defaultValue={kind}>
          <option value="">Tüm türler</option>{Object.entries(kindNames).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
        <button className="button secondary" type="submit"><Icon name="filter" />Uygula</button>
      </form>
      {discrepancies.items.length > 0 && <div className="table-wrap"><table><thead><tr><th>İşlem</th><th>Akış</th><th>Uyuşmazlık</th><th>Kaynak zamanı</th><th>İlk görülme</th><th className="right">Görülme</th><th>Durum</th></tr></thead><tbody>
        {discrepancies.items.map(d => <tr key={d.id}>
          <td>{d.known ? <Link className="ident" href={`/transactions/${encodeURIComponent(d.flow)}/${encodeURIComponent(d.transaction_id)}`} prefetch={false}>{d.transaction_id}</Link> : <span className="mono">{d.transaction_id}</span>}</td>
          <td>{flowNames.get(d.flow) ?? d.flow}</td>
          <td>{kindNames[d.kind] ?? d.kind}{d.step_code && <small className="mono">{d.step_code}</small>}{d.detail && <small>{d.detail}</small>}</td>
          <td className="nowrap"><Time value={d.source_occurred_at} /></td><td className="nowrap"><Time value={d.first_seen_at} /></td>
          <td className="right num">{d.seen_count}</td>
          <td>{d.resolved_at ? <span className="badge good">Çözüldü</span> : <span className="badge warn">Açık</span>}</td>
        </tr>)}
      </tbody></table></div>}
      {discrepancies.items.length === 0 && <Empty icon="check" title={state === "open" ? "Açık uyuşmazlık yok" : "Kayıt yok"}>
        {runs.items.length === 0 ? "Henüz mutabakat koşusu yapılmadı. Mutabakat servisi çalışınca sonuçlar burada görünür." : "Kaynak ile IntegraOps kayıtları seçilen filtrede uyuşuyor."}</Empty>}
      <Pager page={discrepancies.page} totalPages={totalPages} total={discrepancies.total} pageSize={discrepancies.pageSize} unit="uyuşmazlık" href={href} />
    </section>
    <section className="surface">
      <div className="surface-head"><div><h2>Koşular</h2><p>Her koşu karşılaştırdığı aralığı ve bulduğu yeni, tekrarlanan, çözülen uyuşmazlıkları kaydeder.</p></div></div>
      {runs.items.length > 1 && <div className="surface-body">
        <Columns height={120} series={[{ name: "Yeni uyuşmazlık", tone: "warn" }, { name: "Çözülen", tone: "signal" }]}
          points={[...runs.items].slice(0, 20).reverse().map(r => ({ label: new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit", timeZone: "Europe/Istanbul" }).format(new Date(r.created_at)), values: [r.new_count, r.resolved_count], hint: `${r.name}, ${flowNames.get(r.flow) ?? r.flow}` }))} />
      </div>}
      <PagedTable unit="koşu"
        head={<tr><th>Zaman</th><th>Mutabakat</th><th>Akış</th><th>Aralık</th><th className="right">Kaynak</th><th className="right">Eşleşen</th><th className="right">Yeni</th><th className="right">Tekrar</th><th className="right">Çözülen</th><th>Bekleme payı</th></tr>}
        rows={runs.items.map(r => <tr key={r.id}><td className="nowrap"><Time value={r.created_at} /></td><td>{r.name}<small className="mono">{r.version}</small></td><td>{flowNames.get(r.flow) ?? r.flow}</td>
          <td className="nowrap"><Time value={r.window_from} /> – <Time value={r.window_to} /></td>
          <td className="right num">{number(r.source_count)}</td><td className="right num">{number(r.matched_count)}</td>
          <td className="right num">{number(r.new_count)}</td><td className="right num">{number(r.repeated_count)}</td><td className="right num">{number(r.resolved_count)}</td>
          <td>{Math.round(r.grace_seconds / 60)} dk</td></tr>)}
        empty={<Empty icon="scale" title="Koşu yok">Mutabakat servisini kurmak için docs/mutabakat.md belgesine bakın.</Empty>} />
    </section>
    <Explain>
      <p>Mutabakat servisi kaynak sistemi belirli aralıklarla tarar ve IntegraOps'un olay akışından hiç öğrenemediği işlemleri, bildirilmemiş adımları ve durum farklarını burada listeler.</p>
    </Explain>
  </>;
}
