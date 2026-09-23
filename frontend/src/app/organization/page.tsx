import Link from "next/link";
import { cookies } from "next/headers";
import { api, apiMessage } from "@/lib/api";
import type { AuditRow, DataExport, MyOrganization, Organization, SecurityEvent, SecurityInfo } from "@/lib/pilot-types";
import { orgRoleNames, roleNames, securityKinds } from "@/lib/pilot-types";
import { Message, number, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { orgAction, switchOrganization } from "@/app/organization/actions";

export const dynamic = "force-dynamic";

function Op({ value, org, id }: { value: string; org: string; id?: string }) {
  return <><input type="hidden" name="op" value={value} /><input type="hidden" name="org" value={org} />{id && <input type="hidden" name="id" value={id} />}</>;
}
const planTone: Record<string, string> = { trial: "info", starter: "good", business: "good", enterprise: "good", suspended: "bad" };
const limitText = (v: number | null) => v === null ? "Sınırsız" : number(v);
const bytes = (n: number | null) => n === null ? "-" : n > 1_048_576 ? (n / 1_048_576).toFixed(1) + " MB" : n > 1024 ? Math.round(n / 1024) + " KB" : n + " B";

export default async function OrganizationPage() {
  let mine: MyOrganization[], org: Organization, security: SecurityInfo;
  let events: SecurityEvent[] = [], audit: AuditRow[] = [], exports: DataExport[] = [];
  try {
    [mine, security] = await Promise.all([api<MyOrganization[]>("/me/organizations", "GET", undefined, false), api<SecurityInfo>("/me/security", "GET", undefined, false)]);
    if (mine.length === 0) return <Message title="Organizasyon" description="Herhangi bir organizasyonun üyesi değilsiniz. Organizasyon sahibi sizi ekleyebilir." href="/projects" action="Projelere dön" />;
    const wanted = (await cookies()).get("io_org")?.value;
    const selected = mine.find(o => o.id === wanted) ?? mine[0];
    org = await api<Organization>(`/organizations/${selected.id}`, "GET", undefined, false);
    if (org.myRole !== "member") {
      [events, audit] = await Promise.all([api<SecurityEvent[]>(`/organizations/${org.id}/security-events`, "GET", undefined, false), api<AuditRow[]>(`/organizations/${org.id}/audit`, "GET", undefined, false)]);
      if (org.myRole === "owner") exports = await api<DataExport[]>(`/organizations/${org.id}/exports`, "GET", undefined, false);
    }
  } catch (e) { return <Message title="Organizasyon yüklenemedi" description={apiMessage(e)} />; }
  const manage = org.myRole === "owner" || org.myRole === "admin"; const owner = org.myRole === "owner";
  const usage30 = org.usage.reduce((a, u) => a + u.events, 0);
  const peak = Math.max(1, ...org.usage.map(u => u.events));

  return <>
    <PageHead title={org.name} lede="Organizasyon; projeleri, kullanıcıları, planı ve güvenlik ayarlarını bir arada tutar. Proje ayarları her projenin kendi Ayarlar sayfasındadır.">
      {mine.length > 1 && <form action={switchOrganization} className="inline-form">
        <select name="organization" defaultValue={org.id} aria-label="Organizasyon seç">{mine.map(o => <option key={o.id} value={o.id}>{o.name}</option>)}</select>
        <button className="button secondary small" type="submit" style={{ marginLeft: 8 }}>Geç</button></form>}
      {security.systemAdmin && <Link className="button secondary" href="/admin" prefetch={false}>Sistem yönetimi</Link>}
    </PageHead>

    {org.deletion.scheduledAt && <p className="callout bad" role="alert">Bu organizasyon için silme isteği var. Bütün veriler <Time value={org.deletion.scheduledAt} /> tarihinde kalıcı olarak silinecek. Hesap o zamana kadar askıda.</p>}
    {org.trialExpired && !org.deletion.scheduledAt && <p className="callout warn">Deneme süresi doldu; bildirim kabulü durdu. Devam etmek için plan seçilmesi gerekir (destek ile iletişime geçin).</p>}

    <div className="status-strip" role="list">
      <a role="listitem"><span className="label">Plan</span><strong><span className={`badge ${planTone[org.plan] ?? "info"}`}>{org.planName}</span></strong></a>
      <a role="listitem"><span className="label">Projeler</span><strong>{number(org.counts.projects)} / {limitText(org.limits.maxProjects)}</strong></a>
      <a role="listitem"><span className="label">Kullanıcılar</span><strong>{number(org.counts.users)} / {limitText(org.limits.maxUsers)}</strong></a>
      <a role="listitem"><span className="label">Bugünkü olaylar</span><strong>{number(org.counts.eventsToday)} / {limitText(org.limits.maxEventsPerDay)}</strong></a>
      <a role="listitem" className="total"><span className="label">Son 30 gün</span><strong>{number(usage30)}</strong></a>
    </div>

    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Plan ve kullanım</h2><p>{org.plan === "trial" && org.trialEndsAt ? <>Deneme süresi <Time value={org.trialEndsAt} /> tarihinde bitiyor.</> : org.planUpdatedAt ? <>Plan <Time value={org.planUpdatedAt} /> tarihinde güncellendi.</> : "Plan bilgisi."}</p></div></div>
        <div className="surface-body">
          <table><tbody>
            <tr><th>Proje sınırı</th><td>{limitText(org.limits.maxProjects)}</td></tr>
            <tr><th>Kullanıcı sınırı</th><td>{limitText(org.limits.maxUsers)}</td></tr>
            <tr><th>Günlük olay sınırı</th><td>{limitText(org.limits.maxEventsPerDay)}</td></tr>
            <tr><th>En uzun saklama</th><td>{org.limits.maxRetentionDays === null ? "Sınırsız" : org.limits.maxRetentionDays + " gün"}</td></tr>
            <tr><th>Yapay zekâ asistanı</th><td>{org.aiEnabled ? "Planda dahil" : "Planda yok"}</td></tr>
            {org.planNote && <tr><th>Not</th><td>{org.planNote}</td></tr>}
          </tbody></table>
          <h3>Günlük olaylar (30 gün)</h3>
          {org.usage.length === 0 ? <p className="help">Henüz olay kabul edilmedi.</p>
            : <div className="usage-bars" role="img" aria-label="Günlük olay sayıları">{org.usage.map(u => <span key={u.day} title={`${u.day}: ${u.events} olay, ${u.rejected} red`} style={{ height: Math.max(3, Math.round(u.events / peak * 60)) + "px" }} />)}</div>}
          <p className="help">Sınırlar plan varsayılanıdır; sistem yöneticisi müşteri bazında değiştirebilir. Sınır aşıldığında bildirimler 429 ile reddedilir ve ertesi gün yeniden kabul edilir. Plan değişikliği ve ödeme için destek ile iletişime geçin.</p>
        </div>
      </section>

      <section className="surface">
        <div className="surface-head"><div><h2>Projeler</h2><p>Organizasyon sahipleri ve yöneticileri bütün projelere sahip olarak erişir.</p></div><span className="count">{org.projects.length} proje</span></div>
        <div className="table-wrap"><table><thead><tr><th>Proje</th><th>Oluşturma</th></tr></thead><tbody>
          {org.projects.map(p => <tr key={p.id}><td>{p.name}</td><td><Time value={p.created_at} /></td></tr>)}
        </tbody></table></div>
        {manage && <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
          <h3>Organizasyon adı</h3>
          <ActionForm action={orgAction} label="Adı kaydet" compact><Op value="rename" org={org.id} />
            <input name="name" required maxLength={160} defaultValue={org.name} aria-label="Organizasyon adı" style={{ marginRight: 8 }} /></ActionForm>
        </div>}
      </section>
    </div>

    <section className="surface" id="members">
      <div className="surface-head"><div><h2>Organizasyon üyeleri</h2><p>Sahip her şeyi yönetir; yönetici üyeleri, grupları ve projeleri yönetir; üye yalnızca gruplarla ya da proje üyeliğiyle erişir.</p></div><span className="count">{org.members.length} üye</span></div>
      <div className="table-wrap"><table><thead><tr><th>Kullanıcı</th><th>Rol</th><th>İki adım</th><th>Son giriş</th>{manage && <th></th>}</tr></thead><tbody>
        {org.members.map(m => <tr key={m.id}><td>{m.name}<small>{m.email}</small></td><td>{orgRoleNames[m.role]}</td>
          <td>{m.mfa_enabled ? <span className="badge good">Açık</span> : <span className="badge warn">Kapalı</span>}</td><td><Time value={m.last_login_at} /></td>
          {manage && <td><div className="cell-actions"><ActionForm action={orgAction} label="Çıkar" compact tone="danger"><Op value="member-remove" org={org.id} id={m.id} /></ActionForm></div></td>}</tr>)}
      </tbody></table></div>
      {manage && <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <h3>Üye ekle ya da rol değiştir</h3>
        <p className="help">Kullanıcının IntegraOps hesabı olmalıdır (bir projeye davet edilmiş olması yeterlidir). Sahip rolünü yalnızca sahipler verebilir.</p>
        <ActionForm action={orgAction} label="Kaydet"><Op value="member-put" org={org.id} />
          <div className="form-grid">
            <div className="field"><label htmlFor="om-email">E-posta</label><input id="om-email" name="email" type="email" required maxLength={254} /></div>
            <div className="field"><label htmlFor="om-role">Rol</label><select id="om-role" name="role" defaultValue="member">
              <option value="member">Üye</option><option value="admin">Yönetici</option>{owner && <option value="owner">Sahip</option>}</select></div>
          </div></ActionForm>
      </div>}
    </section>

    <section className="surface" id="groups">
      <div className="surface-head"><div><h2>Proje grupları</h2><p>Bir grup, birden çok projeye aynı kullanıcı kümesini aynı rolle bağlar. Proje ayarlarındaki üyelikler ayrıca geçerlidir; güçlü olan rol kazanır.</p></div><span className="count">{org.groups.length} grup</span></div>
      {org.groups.length > 0 && <div className="table-wrap"><table><thead><tr><th>Grup</th><th>Projeler</th><th>Üyeler</th>{manage && <th></th>}</tr></thead><tbody>
        {org.groups.map(g => <tr key={g.id}><td>{g.name}</td>
          <td>{(g.project_ids ?? []).map(id => org.projects.find(p => p.id === id)?.name ?? id).join(", ") || <span className="muted">Proje yok</span>}</td>
          <td>{g.members.length === 0 ? <span className="muted">Üye yok</span> : g.members.map(m => <span key={m.id}>{m.name} <small>({roleNames[m.role]})</small><br /></span>)}</td>
          {manage && <td><div className="cell-actions"><ActionForm action={orgAction} label="Sil" compact tone="danger"><Op value="group-delete" org={org.id} id={g.id} /></ActionForm></div></td>}</tr>)}
      </tbody></table></div>}
      {manage && <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        {[...org.groups.map(g => ({ g })), { g: undefined }].map(({ g }) => <details key={g?.id ?? "new"} open={!g && org.groups.length === 0}>
          <summary className="help">{g ? `"${g.name}" grubunu düzenle` : "Yeni grup"}</summary>
          <div style={{ marginTop: 12 }}><ActionForm action={orgAction} label={g ? "Grubu kaydet" : "Grup oluştur"}><Op value={g ? "group-update" : "group-create"} org={org.id} id={g?.id} />
            <div className="form-grid">
              <div className="field"><label htmlFor={`g-${g?.id ?? "new"}-name`}>Grup adı</label><input id={`g-${g?.id ?? "new"}-name`} name="name" required maxLength={120} defaultValue={g?.name ?? ""} placeholder="Muhasebe ekibi" /></div>
              <div className="field"><span className="label">Projeler</span><div className="day-picks">
                {org.projects.map(p => <label key={p.id} className="toggle"><input type="checkbox" name="projects" value={p.id} defaultChecked={g?.project_ids?.includes(p.id) ?? false} />{p.name}</label>)}</div></div>
              <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor={`g-${g?.id ?? "new"}-members`}>Üyeler (her satır: e-posta rol; rol owner, operator ya da viewer)</label>
                <textarea id={`g-${g?.id ?? "new"}-members`} name="members" rows={3} defaultValue={(g?.members ?? []).map(m => `${m.email} ${m.role}`).join("\n")} placeholder={"ayse@sirket.com operator\nmehmet@sirket.com viewer"} /></div>
            </div></ActionForm></div>
        </details>)}
      </div>}
    </section>

    {owner && <section className="surface" id="sso">
      <div className="surface-head"><div><h2>Tek oturum açma (OpenID Connect)</h2><p>Şirket kimlik sağlayıcısı (Microsoft Entra ID, Google Workspace, Okta, Keycloak) ile giriş. Sağlayıcıda yönlendirme adresi olarak API adresinizin <code>/api/v1/auth/sso/callback</code> yolunu tanımlayın.</p></div>
        <span className={`badge ${org.sso.configured ? "good" : "info"}`}>{org.sso.configured ? (org.sso.required ? "Açık, zorunlu" : "Açık") : "Tanımlı değil"}</span></div>
      <div className="surface-body">
        <ActionForm action={orgAction} label="SSO ayarını kaydet"><Op value="sso-save" org={org.id} />
          <div className="form-grid">
            <div className="field"><label htmlFor="sso-issuer">Issuer adresi</label><input id="sso-issuer" name="issuer" maxLength={500} defaultValue={org.sso.issuer ?? ""} placeholder="https://login.microsoftonline.com/<tenant>/v2.0" /><span className="hint">Boş bırakıp kaydederseniz SSO kaldırılır.</span></div>
            <div className="field"><label htmlFor="sso-domain">E-posta alanı</label><input id="sso-domain" name="domain" maxLength={120} defaultValue={org.sso.domain ?? ""} placeholder="sirket.com" /></div>
            <div className="field"><label htmlFor="sso-client">Client ID</label><input id="sso-client" name="clientId" maxLength={200} defaultValue={org.sso.clientId ?? ""} /></div>
            <div className="field"><label htmlFor="sso-secret">Client secret</label><input id="sso-secret" name="clientSecret" type="password" maxLength={500} autoComplete="off" placeholder={org.sso.configured ? "Değiştirmek için girin" : ""} /></div>
            <div className="field" style={{ gridColumn: "1 / -1" }}><label className="toggle"><input type="checkbox" name="required" defaultChecked={org.sso.required} />Üyeler yalnızca SSO ile giriş yapabilsin (parola girişi kapanır; sistem yöneticileri hariç)</label></div>
          </div></ActionForm>
      </div>
    </section>}

    {manage && <div className="two-col">
      <section className="surface" id="security">
        <div className="surface-head"><div><h2>Güvenlik olayları</h2><p>Giriş, doğrulama, SSO ve organizasyon değişiklikleri.</p></div><span className="count">{events.length} kayıt</span></div>
        <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Olay</th><th>Kim</th><th>Ayrıntı</th></tr></thead><tbody>
          {events.slice(0, 60).map(e => <tr key={e.id}><td><Time value={e.created_at} /></td><td>{securityKinds[e.kind] ?? e.kind}</td><td>{e.actor ?? "-"}<small>{e.ip ?? ""}</small></td><td className="muted">{e.detail ?? ""}</td></tr>)}
          {events.length === 0 && <tr><td colSpan={4} className="muted">Kayıt yok.</td></tr>}
        </tbody></table></div>
      </section>
      <section className="surface" id="audit">
        <div className="surface-head"><div><h2>Denetim kaydı</h2><p>Bütün projelerdeki yönetim işlemleri.</p></div><span className="count">{audit.length} kayıt</span></div>
        <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Proje</th><th>İşlem</th><th>Kim</th></tr></thead><tbody>
          {audit.slice(0, 60).map(a => <tr key={a.id}><td><Time value={a.created_at} /></td><td>{a.project}</td><td>{a.action}<small className="mono">{a.target}</small></td><td>{a.actor ?? "-"}</td></tr>)}
          {audit.length === 0 && <tr><td colSpan={4} className="muted">Kayıt yok.</td></tr>}
        </tbody></table></div>
      </section>
    </div>}

    {owner && <section className="surface" id="data">
      <div className="surface-head"><div><h2>Veri taşınabilirliği ve silme</h2><p>Bütün organizasyon verisini JSON Lines biçiminde zip olarak alın. Silme isteği hesabı askıya alır ve 7 gün sonra verileri kalıcı olarak siler.</p></div></div>
      <div className="surface-body">
        <div className="cell-actions"><ActionForm action={orgAction} label="Dışa aktarım başlat" compact tone="secondary"><Op value="export" org={org.id} /></ActionForm></div>
        {exports.length > 0 && <div className="table-wrap"><table><thead><tr><th>İstek</th><th>Durum</th><th>Boyut</th><th>Geçerlilik</th><th></th></tr></thead><tbody>
          {exports.map(x => <tr key={x.id}><td><Time value={x.created_at} /><small>{x.requested_by ?? ""}</small></td>
            <td>{x.state === "ready" ? <span className="badge good">Hazır</span> : x.state === "failed" ? <span className="badge bad">Başarısız</span> : <span className="badge info">Hazırlanıyor</span>}</td>
            <td>{bytes(x.size_bytes)}</td><td><Time value={x.expires_at} /></td>
            <td>{x.state === "ready" && <a className="button secondary small" href={`/organization/exports/${x.id}?org=${org.id}`}>İndir</a>}</td></tr>)}
        </tbody></table></div>}
        <h3>Organizasyonu sil</h3>
        {org.deletion.scheduledAt
          ? <ActionForm action={orgAction} label="Silme isteğini iptal et" tone="secondary"><Op value="delete-cancel" org={org.id} /></ActionForm>
          : <ActionForm action={orgAction} label="Silme isteği gönder" tone="danger"><Op value="delete-request" org={org.id} />
            <div className="field"><label htmlFor="del-pass">Onay için parolanız</label><input id="del-pass" name="password" type="password" required maxLength={128} autoComplete="current-password" /></div>
            <p className="help">Bütün projeler, akışlar, işlemler, alarmlar, anahtarlar ve üyelikler silinir. Silmeden önce dışa aktarım almanız önerilir.</p></ActionForm>}
      </div>
    </section>}
  </>;
}
