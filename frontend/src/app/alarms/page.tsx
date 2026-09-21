import Link from "next/link";
import { api, apiMessage, getFlows } from "@/lib/api";
import type { AlarmPage } from "@/lib/pilot-types";
import type { Flow } from "@/lib/types";
import { Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import { alarmKind, alarmState } from "@/lib/alarms";

export const dynamic = "force-dynamic";

export default async function AlarmsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const state = typeof params.state === "string" && ["open", "resolved", "all"].includes(params.state) ? params.state : "open";
  const flow = typeof params.flow === "string" && /^[a-z][a-z0-9-]{0,63}$/.test(params.flow) ? params.flow : "";
  const page = typeof params.page === "string" && /^\d{1,9}$/.test(params.page) ? params.page : "1";
  let result: AlarmPage; let flows: Flow[];
  try { [result, flows] = await Promise.all([api<AlarmPage>("/alarms?" + new URLSearchParams({ state, flow, page })), getFlows()]); }
  catch (e) { return <Message title="Alarmlar yüklenemedi" description={apiMessage(e)} />; }
  const flowNames = new Map(flows.map(f => [f.code, f.name]));
  const href = (p: number) => "/alarms?" + new URLSearchParams({ state, flow, page: String(p) });
  const totalPages = Math.max(1, Math.ceil(result.total / result.pageSize));
  const stateLabel = state === "open" ? "açık" : state === "resolved" ? "kapanmış" : "";

  return <>
    <PageHead title="Alarmlar" lede="Süresi aşılan adımlar ve kaynak sistemin bildirdiği hatalar. Eksik adım bildirilince alarm kendiliğinden kapanır.">
      <RefreshControls />
    </PageHead>
    <section className="surface">
      <form className="filters" action="/alarms">
        <div className="field"><label htmlFor="a-state">Durum</label><select id="a-state" name="state" defaultValue={state}>
          <option value="open">Açık alarmlar</option><option value="resolved">Kapanan alarmlar</option><option value="all">Tümü</option></select></div>
        <div className="field"><label htmlFor="a-flow">Akış</label><select id="a-flow" name="flow" defaultValue={flow}>
          <option value="">Tüm akışlar</option>{flows.map(f => <option key={f.id} value={f.code}>{f.name}</option>)}</select></div>
        <button className="button secondary" type="submit">Uygula</button>
        {(state !== "open" || flow) && <Link className="reset" href="/alarms" prefetch={false}>Temizle</Link>}
      </form>
      <div className="table-wrap"><table><thead><tr><th>İşlem</th><th>Akış</th><th>Alarm</th><th>Beklenen adım</th><th>Açılış</th><th>Durum</th></tr></thead><tbody>
        {result.items.map(a => { const s = alarmState(a); return <tr key={a.id}>
          <td><Link className="ident" href={`/alarms/${a.id}`} prefetch={false}>{a.business_id}</Link></td>
          <td>{flowNames.get(a.flow) ?? a.flow}<small className="mono">{a.flow}</small></td>
          <td>{alarmKind(a.kind)}</td>
          <td className="mono">{a.step_code}</td>
          <td className="nowrap"><Time value={a.opened_at} /></td>
          <td><span className={`badge ${s.tone}`}>{s.label}</span></td>
        </tr>; })}
      </tbody></table></div>
      {result.items.length === 0 && <div className="empty"><h3>{stateLabel ? `Şu anda ${stateLabel} alarm yok` : "Alarm yok"}</h3>
        <p>{state === "open" ? "Bütün beklenen adımlar zamanında bildirilmiş. Süre aşımı ya da hata oluşursa burada görünür." : "Seçilen filtrede kayıt bulunmuyor."}</p></div>}
      <div className="pagination"><span>{number(result.total)} alarm, sayfa {result.page} / {number(totalPages)}</span>
        <nav aria-label="Sayfalama">
          {result.page > 1 && <Link className="button secondary small" href={href(result.page - 1)} prefetch={false}>Önceki</Link>}
          {result.page * result.pageSize < result.total && <Link className="button secondary small" href={href(result.page + 1)} prefetch={false}>Sonraki</Link>}
        </nav></div>
    </section>
  </>;
}
