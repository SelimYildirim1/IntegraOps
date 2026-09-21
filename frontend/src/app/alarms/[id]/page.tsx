import Link from "next/link";
import { api, apiMessage, getAccess } from "@/lib/api";
import type { AlarmDetail } from "@/lib/pilot-types";
import { eventNames, deliveryNames } from "@/lib/pilot-types";
import { alarmKind, alarmState } from "@/lib/alarms";
import { Message, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import RefreshControls from "@/components/refresh-controls";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";

const deliveryTone: Record<string, string> = { sent: "good", dead: "bad", sending: "info", pending: "info", cancelled: "" };

export default async function AlarmDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; let result: AlarmDetail; let canAct = false;
  if (!/^[0-9a-f-]{36}$/i.test(id)) return <Message title="Alarm bulunamadı" description="Bağlantıyı kontrol edin." href="/alarms" action="Alarmlara dön" />;
  try { const access = await getAccess(); canAct = access.role !== "viewer"; result = await api<AlarmDetail>(`/alarms/${id}`); }
  catch (e) { return <Message title="Alarm yüklenemedi" description={apiMessage(e)} href="/alarms" action="Alarmlara dön" />; }
  const a = result.alarm;
  const s = alarmState(a);
  const transactionHref = `/transactions/${encodeURIComponent(a.flow)}/${encodeURIComponent(a.business_id)}`;

  return <>
    <PageHead crumb={{ href: "/alarms", label: "Alarmlar" }}
      title={<>{alarmKind(a.kind)}: <span className="mono">{a.business_id}</span></>}
      lede={<>{a.flow} akışında <span className="mono">{a.step_code}</span> adımı için açıldı.</>}>
      <span className={`badge ${s.tone}`}>{s.label}</span>
      {!a.acknowledged_at && !a.resolved_at && canAct && <ActionForm action={adminAction} label="Gördüm olarak işaretle" compact tone="secondary">
        <input type="hidden" name="op" value="ack" /><input type="hidden" name="id" value={id} /></ActionForm>}
      <Link className="button" href={transactionHref} prefetch={false}>İşlemi aç</Link>
      <RefreshControls />
    </PageHead>

    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>{a.resolved_at ? "Alarm kapandı" : "Alarm açık"}</h2></div>
        <div className="surface-body">
          <dl className="facts">
            <div><dt>Açılış</dt><dd className="time"><Time value={a.opened_at} /></dd></div>
            <div><dt>Beklenen son zaman</dt><dd className="time">{a.deadline_at ? <Time value={a.deadline_at} /> : "—"}</dd></div>
            <div><dt>Görüldü</dt><dd className="time">{a.acknowledged_at ? <Time value={a.acknowledged_at} /> : "Henüz değil"}</dd></div>
            <div><dt>Kapanış</dt><dd className="time">{a.resolved_at ? <Time value={a.resolved_at} /> : "Açık"}</dd></div>
          </dl>
          {a.resolution && <p className="callout good">{a.resolution === "step_completed"
            ? "Eksik adımın tamamlandığı bildirildi; alarm bu bildirimle kapandı."
            : "İşlemin durumu değişti; bu gecikme koşulu artık geçerli değil."}</p>}
          {a.error && <div><p className="help" style={{ marginBottom: 6 }}>Kaynak sistemin bildirdiği hata</p><pre className="error-content">{a.error}</pre></div>}
          <p className="help">Gördüm işareti alarmı kapatmaz; yalnızca bir kullanıcının alarmı fark ettiğini kaydeder. Kapanış, gelen bildirimlerle belirlenir.</p>
        </div>
      </section>

      <section className="surface">
        <div className="surface-head"><h2>Geçmiş</h2></div>
        <div className="surface-body"><ol className="history">
          {result.events.map(e => <li key={e.id}><span className="time"><Time value={e.created_at} /></span>
            <span><strong>{eventNames[e.kind] ?? e.kind}</strong>{e.actor && <span className="who">, {e.actor}</span>}</span></li>)}
        </ol></div>
      </section>
    </div>

    <section className="surface">
      <div className="surface-head"><div><h2>Bildirim gönderimleri</h2><p>Açılış ve kapanış, etkin kanallara ayrı ayrı gönderilir.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Kanal</th><th>Olay</th><th>Durum</th><th className="right">Deneme</th><th>Sonraki deneme ya da gönderim</th><th></th></tr></thead><tbody>
        {result.deliveries.map(d => <tr key={d.id}>
          <td>{d.channel}</td><td>{eventNames[d.event] ?? d.event}</td>
          <td><span className={`badge ${deliveryTone[d.state] ?? ""}`}>{deliveryNames[d.state] ?? d.state}</span>
            {d.last_error && <small>{d.last_error}</small>}</td>
          <td className="right num">{d.attempts}</td>
          <td className="nowrap"><Time value={d.state === "sent" ? d.sent_at : d.state === "pending" ? d.available_at : null} /></td>
          <td><div className="cell-actions">{d.state === "dead" && canAct && <ActionForm action={adminAction} label="Tekrar dene" compact tone="secondary">
            <input type="hidden" name="op" value="retry" /><input type="hidden" name="id" value={d.id} /></ActionForm>}</div></td>
        </tr>)}
      </tbody></table></div>
      {result.deliveries.length === 0 && <div className="empty"><h3>Gönderim yok</h3><p>Bu alarm oluşurken etkin bir bildirim kanalı yoktu.</p></div>}
    </section>
  </>;
}
