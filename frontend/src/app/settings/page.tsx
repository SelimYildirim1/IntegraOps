import { api, apiMessage, getAccess } from "@/lib/api";
import type { Member, ProjectKey, Channel } from "@/lib/pilot-types";
import { roleNames } from "@/lib/pilot-types";
import { Message, PageHead } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";

function Op({ value, id }: { value: string; id?: string }) {
  return <><input type="hidden" name="op" value={value} />{id && <input type="hidden" name="id" value={id} />}</>;
}
const scopeNames: Record<string, string> = { ingest: "Bildirim gönderir", read: "Takip verilerini okur", legacy: "Eski geniş yetki" };

export default async function SettingsPage() {
  let members: Member[], keys: ProjectKey[], channels: Channel[];
  try {
    const access = await getAccess();
    if (access.role !== "owner") return <Message title="Proje ayarları" description="Kullanıcıları, anahtarları ve bildirim kanallarını yalnızca proje sahibi yönetir." />;
    [members, keys, channels] = await Promise.all([api<Member[]>("/members"), api<ProjectKey[]>("/keys"), api<Channel[]>("/channels")]);
  } catch (e) { return <Message title="Ayarlar yüklenemedi" description={apiMessage(e)} />; }
  const activeKeys = keys.filter(k => !k.revoked_at), revokedKeys = keys.filter(k => k.revoked_at);

  return <>
    <PageHead title="Proje ayarları" lede="Bu projeye kimlerin erişeceği, hangi sistemlerin bildirim göndereceği ve alarmların nereye iletileceği." />

    <section className="surface" id="members">
      <div className="surface-head"><div><h2>Kullanıcılar</h2><p>Proje sahibi bütün ayarları yönetir; operatör akış ve alarm işlemlerini yapar; izleyici yalnızca görüntüler.</p></div>
        <span className="count">{members.length} kullanıcı</span></div>
      <div className="table-wrap"><table><thead><tr><th>Kullanıcı</th><th>Rol</th><th></th></tr></thead><tbody>
        {members.map(m => <tr key={m.id}><td>{m.name}<small>{m.email}</small></td><td>{roleNames[m.role]}</td>
          <td><div className="cell-actions"><ActionForm action={adminAction} label="Projeden çıkar" compact tone="danger"><Op value="member-remove" id={m.id} /></ActionForm></div></td></tr>)}
      </tbody></table></div>
      <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <h3>Kullanıcı ekle ya da rolünü değiştir</h3>
        <p className="help">Aynı e-postayla kayıtlı bir kullanıcı varsa parolayı boş bırakın; yalnızca rolü güncellenir. Yeni kullanıcı için başlangıç parolası belirleyin.</p>
        <ActionForm action={adminAction} label="Kullanıcıyı kaydet"><Op value="member-save" />
          <div className="form-grid">
            <div className="field"><label htmlFor="m-name">Ad soyad</label><input id="m-name" name="name" required maxLength={120} /></div>
            <div className="field"><label htmlFor="m-email">E-posta</label><input id="m-email" name="email" type="email" required maxLength={254} /></div>
            <div className="field"><label htmlFor="m-role">Rol</label><select id="m-role" name="role" defaultValue="viewer">
              <option value="viewer">İzleyici</option><option value="operator">Operatör</option><option value="owner">Proje sahibi</option></select></div>
            <div className="field"><label htmlFor="m-pass">Başlangıç parolası</label>
              <input id="m-pass" name="password" type="password" minLength={12} maxLength={128} autoComplete="new-password" />
              <span className="hint">En az 12 karakter. Mevcut kullanıcı için boş bırakın.</span></div>
          </div>
        </ActionForm>
      </div>
    </section>

    <section className="surface" id="keys">
      <div className="surface-head"><div><h2>API anahtarları</h2><p>Kaynak sistemler bildirim göndermek için anahtarı X-API-Key başlığında kullanır. İptal edilen anahtar hemen geçersiz olur.</p></div>
        <span className="count">{activeKeys.length} etkin</span></div>
      <div className="table-wrap"><table><thead><tr><th>Ad</th><th>Yetki</th><th>Önek</th><th>Durum</th><th></th></tr></thead><tbody>
        {[...activeKeys, ...revokedKeys].map(k => <tr key={k.id}><td>{k.name}</td><td>{scopeNames[k.scope] ?? k.scope}</td>
          <td><code>{k.key_prefix}…</code></td>
          <td>{k.revoked_at ? <span className="badge">İptal edildi</span> : <span className="badge good">Etkin</span>}</td>
          <td><div className="cell-actions">{!k.revoked_at && <ActionForm action={adminAction} label="İptal et" compact tone="danger"><Op value="key-revoke" id={k.id} /></ActionForm>}</div></td></tr>)}
      </tbody></table></div>
      {keys.length === 0 && <div className="empty"><h3>Henüz anahtar yok</h3><p>Bir kaynak sistemi bağlamak için aşağıdan anahtar oluşturun.</p></div>}
      <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <h3>Yeni anahtar</h3>
        <p className="help">Anahtar yalnızca oluşturulduğu anda gösterilir. Bağlanacak sistemde saklayın.</p>
        <ActionForm action={adminAction} label="Anahtar oluştur"><Op value="key-create" />
          <div className="form-grid">
            <div className="field"><label htmlFor="k-name">Anahtar adı</label><input id="k-name" name="name" required maxLength={120} placeholder="ERP bağlantısı" /></div>
            <div className="field"><label htmlFor="k-scope">Yetki</label><select id="k-scope" name="scope" defaultValue="ingest">
              <option value="ingest">Bildirim gönderir</option><option value="read">Takip verilerini okur</option></select></div>
          </div>
        </ActionForm>
      </div>
    </section>

    <section className="surface" id="channels">
      <div className="surface-head"><div><h2>Bildirim kanalları</h2><p>Etkin kanallara yeni açılan ve kapanan alarmlar gönderilir. Kanalı kapatmak bekleyen gönderimleri iptal eder.</p></div>
        <span className="count">{channels.filter(c => c.enabled).length} etkin</span></div>
      <div className="table-wrap"><table><thead><tr><th>Kanal</th><th>Tür</th><th>Hedef</th><th>Durum</th><th></th></tr></thead><tbody>
        {channels.map(c => <tr key={c.id}><td>{c.name}</td><td>{c.kind === "email" ? "E-posta" : "Webhook"}</td>
          <td className="mono" style={{ overflowWrap: "anywhere", fontSize: 12.5 }}>{c.target}</td>
          <td>{c.enabled ? <span className="badge good">Etkin</span> : <span className="badge">Kapalı</span>}</td>
          <td><div className="cell-actions"><ActionForm action={adminAction} label={c.enabled ? "Kapat" : "Etkinleştir"} compact tone="secondary">
            <Op value="channel-toggle" id={c.id} /><input type="hidden" name="enabled" value={String(!c.enabled)} /></ActionForm></div></td></tr>)}
      </tbody></table></div>
      {channels.length === 0 && <div className="empty"><h3>Henüz kanal yok</h3><p>Alarmların e-posta ya da webhook ile iletilmesi için kanal ekleyin.</p></div>}
      <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <h3>Yeni kanal</h3>
        <p className="help">Hedef sonradan düzenlenmez; değişecekse eski kanalı kapatıp yeni bir kanal oluşturun. Webhook adresleri sunucudaki izin listesine uymalıdır.</p>
        <ActionForm action={adminAction} label="Kanal oluştur"><Op value="channel-create" />
          <div className="form-grid">
            <div className="field"><label htmlFor="c-name">Kanal adı</label><input id="c-name" name="name" required maxLength={120} placeholder="Operasyon ekibi" /></div>
            <div className="field"><label htmlFor="c-kind">Tür</label><select id="c-kind" name="kind" defaultValue="email">
              <option value="email">E-posta</option><option value="webhook">Webhook</option></select></div>
            <div className="field"><label htmlFor="c-target">E-posta adresi ya da webhook adresi</label><input id="c-target" name="target" required maxLength={2048} /></div>
            <div className="field"><label htmlFor="c-secret">Webhook ortak sırrı</label>
              <input id="c-secret" name="secret" type="password" minLength={32} maxLength={128} autoComplete="off" />
              <span className="hint">Boş bırakılırsa üretilir ve bir kez gösterilir. E-posta kanalında kullanılmaz.</span></div>
          </div>
        </ActionForm>
      </div>
    </section>
  </>;
}
