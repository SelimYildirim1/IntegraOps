import Link from "next/link";
import { api, apiMessage, getAccess, getFlows, getSavedViews } from "@/lib/api";
import type { AlarmPage, SavedView, TriageResult } from "@/lib/pilot-types";
import type { Flow } from "@/lib/types";
import { testLabels } from "@/lib/types";
import { dateInput, Empty, Explain, Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import SavedViews from "@/components/saved-views";
import Pager from "@/components/pager";
import Icon from "@/components/icons";
import { alarmKind, alarmState } from "@/lib/alarms";
import { alarmCauseNames, triageSignalNames } from "@/lib/pilot-types";
import type { AlarmCause } from "@/lib/pilot-types";
import { apiFilters } from "@/lib/filters";
import { Columns } from "@/components/charts";
import type { ReportTrend } from "@/lib/pilot-types";

export const dynamic = "force-dynamic";

export default async function AlarmsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const str = (key: string) => (typeof params[key] === "string" ? params[key] as string : "");
  const state = ["open", "resolved", "all"].includes(str("state")) ? str("state") : "open";
  const flow = /^[a-z][a-z0-9-]{0,63}$/.test(str("flow")) ? str("flow") : "";
  const page = /^\d{1,9}$/.test(str("page")) ? str("page") : "1";
  const search = str("search").trim().slice(0, 128);
  const from = dateInput.test(str("from")) ? str("from") : "";
  const to = dateInput.test(str("to")) ? str("to") : "";
  const test = Object.hasOwn(testLabels, str("test")) ? str("test") : "exclude";
  const assigned = ["me", "none"].includes(str("assigned")) ? str("assigned") : "";
  const step = /^[a-z][a-z0-9_-]{0,63}$/i.test(str("step")) ? str("step") : "";
  const cause = str("cause") === "none" || Object.hasOwn(alarmCauseNames, str("cause")) ? str("cause") : "";
  const filters = { state, flow, search, from, to, test, assigned, step, cause };
  const query = new URLSearchParams(Object.entries(filters).filter(([k, v]) => v && !(k === "state" && v === "open") && !(k === "test" && v === "exclude"))).toString();
  let result: AlarmPage; let flows: Flow[]; let views: SavedView[]; let canExport = false; let trend: ReportTrend | null = null; let triage: TriageResult | null = null;
  try {
    [result, flows, views, canExport, trend, triage] = await Promise.all([
      api<AlarmPage>("/alarms?" + new URLSearchParams({ ...apiFilters(filters), page })), getFlows(), getSavedViews("alarms"),
      getAccess().then(a => a.role !== "viewer"),
      api<ReportTrend>("/reports/trend?" + new URLSearchParams({ flow, test, period: "day", buckets: "30" })).catch(() => null),
      state === "open" ? api<TriageResult>("/alarms/triage?" + new URLSearchParams({ test })).catch(() => null) : Promise.resolve(null)]);
  } catch (e) { return <Message title="Alarmlar yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const dayLabel = (iso: string) => new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit", timeZone: "Europe/Istanbul" }).format(new Date(iso));
  const trendAlarms = trend?.buckets.reduce((a, b) => a + b.alarms, 0) ?? 0;
  const flowNames = new Map(flows.map(f => [f.code, f.name]));
  const href = (p: number) => "/alarms?" + new URLSearchParams({ ...filters, page: String(p) });
  const totalPages = Math.max(1, Math.ceil(result.total / result.pageSize));
  const stateLabel = state === "open" ? "açık" : state === "resolved" ? "kapanmış" : "";
  const filtered = Boolean(query);

  return <>
    <PageHead icon="bell" title="Alarmlar" lede="Süresi aşılan adımlar ve kaynak sistemin bildirdiği hatalar.">
      <Link className="button secondary" href={"/alarms/triage?" + new URLSearchParams({ test })} prefetch={false}><Icon name="layers" />Sorun grupları</Link>
      {canExport && <a className="button secondary" href={`/export/alarms?${query}`}><Icon name="download" />CSV indir</a>}
      <RefreshControls />
    </PageHead>
    {triage && triage.groups.length > 0 && <p className={`callout ${triage.groups[0].priority >= 70 ? "bad" : triage.groups[0].priority >= 45 ? "warn" : ""}`}>
      <strong>{triage.openAlarms} açık alarm, {triage.groups.length} sorun grubu.</strong> En öncelikli: {triage.groups[0].flowName} · <span className="mono">{triage.groups[0].step}</span> ({triage.groups[0].count} alarm{triage.groups[0].signals.length ? `, ${triage.groups[0].signals.map(s => triageSignalNames[s]).join(", ").toLowerCase()}` : ""}).{" "}
      <Link href={"/alarms/triage?" + new URLSearchParams({ test })} prefetch={false}>Grupları aç</Link></p>}
    {trend && trend.buckets.length > 1 && <div className="trend-card">
      <div className="trend-head">
        <div><h2>Son 30 gün</h2><p>Günlük açılan alarm sayısı.</p></div>
        <div className="trend-stats"><div className="trend-stat"><i style={{ background: "var(--chart-bad)" }} /><div><strong>{number(trendAlarms)}</strong><span>açılan alarm</span></div></div></div>
      </div>
      <Columns height={110} series={[{ name: "Açılan alarm", tone: "bad" }]} points={trend.buckets.map(b => ({ label: dayLabel(b.start), values: [b.alarms] }))} />
    </div>}
    <section className="surface">
      <form className="filters" action="/alarms">
        <div className="field search"><label htmlFor="a-search">İşlem numarası</label>
          <div className="control"><Icon name="search" /><input id="a-search" name="search" defaultValue={search} maxLength={128} placeholder="ORDER-10092" /></div></div>
        <div className="field"><label htmlFor="a-state">Durum</label><select id="a-state" name="state" defaultValue={state}>
          <option value="open">Açık alarmlar</option><option value="resolved">Kapanan alarmlar</option><option value="all">Tümü</option></select></div>
        <div className="field"><label htmlFor="a-flow">Akış</label><select id="a-flow" name="flow" defaultValue={flow}>
          <option value="">Tüm akışlar</option>{flows.map(f => <option key={f.id} value={f.code}>{f.name}</option>)}</select></div>
        <div className="field"><label htmlFor="a-assigned">Sorumlu</label><select id="a-assigned" name="assigned" defaultValue={assigned}>
          <option value="">Herkes</option><option value="me">Bendekiler</option><option value="none">Sorumlusu olmayanlar</option></select></div>
        <div className="field"><label htmlFor="a-from">Açılış başlangıcı</label><input id="a-from" name="from" type="date" defaultValue={from} /></div>
        <div className="field"><label htmlFor="a-to">Açılış bitişi</label><input id="a-to" name="to" type="date" defaultValue={to} /></div>
        <div className="field"><label htmlFor="a-cause">Neden</label><select id="a-cause" name="cause" defaultValue={cause}>
          <option value="">Tümü</option><option value="none">Nedeni girilmemiş</option>
          {(Object.keys(alarmCauseNames) as AlarmCause[]).map(c => <option key={c} value={c}>{alarmCauseNames[c]}</option>)}</select></div>
        {step && <input type="hidden" name="step" value={step} />}
        <div className="field"><label htmlFor="a-test">Test verisi</label><select id="a-test" name="test" defaultValue={test}>
          {Object.entries(testLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></div>
        <button className="button secondary" type="submit"><Icon name="filter" />Uygula</button>
        {filtered && <Link className="reset" href="/alarms" prefetch={false}><Icon name="x" size={14} />Temizle</Link>}
      </form>
      <SavedViews kind="alarms" views={views} basePath="/alarms" currentQuery={query} />
      {step && <p className="help">Adım filtresi: <span className="mono">{step}</span> · <Link href={"/alarms?" + new URLSearchParams({ ...filters, step: "" })} prefetch={false}>kaldır</Link></p>}
      {result.items.length > 0 && <div className="table-wrap"><table><thead><tr><th>İşlem</th><th>Akış</th><th>Alarm</th><th>Beklenen adım</th><th>Sorumlu</th><th>Açılış</th><th>Durum</th><th>Neden</th></tr></thead><tbody>
        {result.items.map(a => { const s = alarmState(a); return <tr key={a.id}>
          <td><Link className="ident" href={`/alarms/${a.id}`} prefetch={false}>{a.business_id}</Link>{a.is_test && <span className="badge test">Test</span>}</td>
          <td>{flowNames.get(a.flow) ?? a.flow}</td>
          <td>{alarmKind(a.kind)}</td>
          <td className="mono">{a.step_code}</td>
          <td>{a.assigned_name ?? <span className="muted">—</span>}</td>
          <td className="nowrap"><Time value={a.opened_at} /></td>
          <td><span className={`badge ${s.tone}`}>{s.label}</span></td>
          <td>{a.cause ? <span className="badge">{alarmCauseNames[a.cause]}</span> : <span className="muted">—</span>}</td>
        </tr>; })}
      </tbody></table></div>}
      {result.items.length === 0 && <Empty icon="check" title={stateLabel ? `Şu anda ${stateLabel} alarm yok` : "Alarm yok"}>
        {state === "open" && !filtered ? "Bütün beklenen adımlar zamanında bildirilmiş. Süre aşımı ya da hata oluşursa burada görünür." : "Seçilen filtrede kayıt bulunmuyor."}</Empty>}
      <Pager page={result.page} totalPages={totalPages} total={result.total} pageSize={result.pageSize} unit="alarm" href={href} />
    </section>
    <Explain>
      <p>Bir adım tanınan sürede bildirilmezse süre aşımı alarmı, kaynak sistem hata bildirirse hata alarmı açılır.</p>
      <p>Eksik adım sonradan bildirilince ya da işlem iptal edilince alarm kendiliğinden kapanır. Gördüm işareti alarmı kapatmaz.</p>
    </Explain>
  </>;
}
