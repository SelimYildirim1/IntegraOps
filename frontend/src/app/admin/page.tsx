import { api, apiMessage } from "@/lib/api";
import type { AdminOrganization, PlanInfo, SecurityInfo } from "@/lib/pilot-types";
import { Empty, Message, number, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import PagedTable from "@/components/paged-table";
import { planAction } from "@/app/organization/actions";

export const dynamic = "force-dynamic";
const planTone: Record<string, string> = { trial: "info", starter: "good", business: "good", enterprise: "good", suspended: "bad" };

export default async function AdminPage() {
  let orgs: AdminOrganization[], plans: PlanInfo[];
  try {
    const security = await api<SecurityInfo>("/me/security", "GET", undefined, false);
    if (!security.systemAdmin) return <Message title="Sistem yönetimi" description="Bu sayfa yalnızca sistem yöneticilerine açıktır. Yetki SYSTEM_ADMIN_EMAILS ayarıyla ya da veritabanından verilir." href="/organization" action="Organizasyona dön" icon="lock" />;
    [orgs, plans] = await Promise.all([api<AdminOrganization[]>("/admin/organizations", "GET", undefined, false), api<PlanInfo[]>("/admin/plans", "GET", undefined, false)]);
  } catch (e) { return <Message title="Sistem yönetimi yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const names = Object.fromEntries(plans.map(p => [p.code, p.name]));

  return <>
    <PageHead icon="shield" title="Sistem yönetimi" lede="Bütün müşteri organizasyonları, planları ve kullanım sınırları." />
    <section className="surface">
      <div className="surface-head"><div><h2>Planlar</h2><p>Boş sınır sınırsız demektir. Müşteri bazında sınır aşağıdaki formla değiştirilir.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Plan</th><th>Proje</th><th>Kullanıcı</th><th>Günlük olay</th><th>Saklama</th><th>Yapay zekâ</th></tr></thead><tbody>
        {plans.map(p => <tr key={p.code}><td>{p.name} <small className="mono">{p.code}</small></td><td>{p.limits.maxProjects ?? "∞"}</td><td>{p.limits.maxUsers ?? "∞"}</td>
          <td>{p.limits.maxEventsPerDay === null ? "∞" : number(p.limits.maxEventsPerDay)}</td><td>{p.limits.maxRetentionDays === null ? "∞" : p.limits.maxRetentionDays + " gün"}</td><td>{p.ai ? "Dahil" : "—"}</td></tr>)}
      </tbody></table></div>
    </section>
    <section className="surface">
      <div className="surface-head"><div><h2>Organizasyonlar</h2></div><span className="count">{orgs.length} organizasyon</span></div>
      <PagedTable unit="organizasyon"
        head={<tr><th>Organizasyon</th><th>Plan</th><th>Proje</th><th>Üye</th><th>30 günlük olay</th><th>Yapay zekâ</th><th>Oluşturma</th></tr>}
        rows={orgs.map(o => <tr key={o.id}><td>{o.name}<small>{o.owner_email ?? "sahip yok"}</small></td>
          <td><span className={`badge ${planTone[o.plan] ?? "info"}`}>{names[o.plan] ?? o.plan}</span>{o.plan === "trial" && o.trial_ends_at && <small>bitiş: <Time value={o.trial_ends_at} /></small>}{o.deletion_scheduled_at && <small className="muted">silinecek: <Time value={o.deletion_scheduled_at} /></small>}</td>
          <td>{o.projects}</td><td>{o.members}</td><td>{number(o.events_30d)}</td><td>{o.ai_enabled ? <span className="badge good">Açık</span> : <span className="badge">Kapalı</span>}</td><td><Time value={o.created_at} /></td></tr>)}
        empty={<Empty icon="building" title="Organizasyon yok" />} />
      <div className="surface-body">
        <h3>Plan ve sınır ata</h3>
        <ActionForm action={planAction} label="Kaydet">
          <div className="form-grid">
            <div className="field"><label htmlFor="pl-org">Organizasyon</label><select id="pl-org" name="id" required>{orgs.map(o => <option key={o.id} value={o.id}>{o.name}</option>)}</select></div>
            <div className="field"><label htmlFor="pl-plan">Plan</label><select id="pl-plan" name="plan" defaultValue=""><option value="">Değiştirme</option>{plans.map(p => <option key={p.code} value={p.code}>{p.name}</option>)}</select></div>
            <div className="field"><label htmlFor="pl-p">Proje sınırı</label><input id="pl-p" name="maxProjects" inputMode="numeric" placeholder="plan varsayılanı" /></div>
            <div className="field"><label htmlFor="pl-u">Kullanıcı sınırı</label><input id="pl-u" name="maxUsers" inputMode="numeric" placeholder="plan varsayılanı" /></div>
            <div className="field"><label htmlFor="pl-e">Günlük olay sınırı</label><input id="pl-e" name="maxEventsPerDay" inputMode="numeric" placeholder="plan varsayılanı" /></div>
            <div className="field"><label htmlFor="pl-r">Saklama sınırı (gün)</label><input id="pl-r" name="maxRetentionDays" inputMode="numeric" placeholder="plan varsayılanı" /></div>
            <div className="field"><label htmlFor="pl-ai">Yapay zekâ asistanı</label><select id="pl-ai" name="aiEnabled" defaultValue=""><option value="">Plan varsayılanı</option><option value="true">Açık</option><option value="false">Kapalı</option></select></div>
            <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor="pl-note">Not (sözleşme, fatura numarası)</label><input id="pl-note" name="note" maxLength={500} /></div>
          </div>
        </ActionForm>
      </div>
    </section>
  </>;
}
