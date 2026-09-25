import { api, apiMessage, getAccess } from "@/lib/api";
import type { Member, ProjectKey, Channel, RetentionInfo, Calendar, DigestInfo } from "@/lib/pilot-types";
import { channelKindNames, roleNames } from "@/lib/pilot-types";
import { Empty, Explain, Message, number, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import PagedTable from "@/components/paged-table";
import Icon from "@/components/icons";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";

function Op({ value, id }: { value: string; id?: string }) {
  return <><input type="hidden" name="op" value={value} />{id && <input type="hidden" name="id" value={id} />}</>;
}
const scopeNames: Record<string, string> = { ingest: "Bildirim gönderir", read: "Takip verilerini okur", legacy: "Eski geniş yetki" };
const dayNames: Record<number, string> = { 1: "Pzt", 2: "Sal", 3: "Çar", 4: "Per", 5: "Cum", 6: "Cmt", 7: "Paz" };

function CalendarForm({ calendar }: { calendar?: Calendar }) {
  const p = calendar ? `cal-${calendar.id.slice(0, 8)}` : "cal-new";
  return <ActionForm action={adminAction} label={calendar ? "Takvimi kaydet" : "Takvim oluştur"}>
    <Op value={calendar ? "calendar-update" : "calendar-create"} id={calendar?.id} />
    <div className="form-grid">
      <div className="field"><label htmlFor={`${p}-name`}>Takvim adı</label><input id={`${p}-name`} name="name" required maxLength={120} defaultValue={calendar?.name ?? ""} placeholder="Mesai saatleri" /></div>
      <div className="field"><label htmlFor={`${p}-tz`}>Saat dilimi</label><input id={`${p}-tz`} name="timezone" required maxLength={64} defaultValue={calendar?.timezone ?? "Europe/Istanbul"} /></div>
      <div className="field"><label htmlFor={`${p}-start`}>Başlangıç saati</label><input id={`${p}-start`} name="startTime" type="time" required defaultValue={calendar?.start_time ?? "09:00"} /></div>
      <div className="field"><label htmlFor={`${p}-end`}>Bitiş saati</label><input id={`${p}-end`} name="endTime" type="time" required defaultValue={calendar?.end_time ?? "18:00"} /></div>
      <div className="field" style={{ gridColumn: "1 / -1" }}><span className="label">Çalışma günleri</span>
        <div className="day-picks">{[1, 2, 3, 4, 5, 6, 7].map(d => <label key={d} className="toggle"><input type="checkbox" name="workDays" value={d} defaultChecked={calendar ? calendar.work_days.includes(d) : d <= 5} />{dayNames[d]}</label>)}</div></div>
      <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor={`${p}-hol`}>Tatiller (her satır: YYYY-AA-GG ad)</label>
        <textarea id={`${p}-hol`} name="holidays" rows={4} defaultValue={(calendar?.holidays ?? []).map(h => `${h.day} ${h.name}`.trim()).join("\n")} placeholder={"2026-10-29 Cumhuriyet Bayramı"} /></div>
    </div>
  </ActionForm>;
}

export default async function SettingsPage() {
  let members: Member[], keys: ProjectKey[], channels: Channel[], retention: RetentionInfo, calendars: Calendar[], digest: DigestInfo;
  try {
    const access = await getAccess();
    if (access.role !== "owner") return <Message title="Proje ayarları" description="Kullanıcıları, anahtarları ve bildirim kanallarını yalnızca proje sahibi yönetir." icon="lock" />;
    [members, keys, channels, retention, calendars, digest] = await Promise.all([api<Member[]>("/members"), api<ProjectKey[]>("/keys"), api<Channel[]>("/channels"), api<RetentionInfo>("/retention"), api<Calendar[]>("/calendars"), api<DigestInfo>("/reports/digest")]);
  } catch (e) { return <Message title="Ayarlar yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const activeKeys = keys.filter(k => !k.revoked_at), revokedKeys = keys.filter(k => k.revoked_at);
  const p = retention.policy;

  return <>
    <PageHead icon="sliders" title="Proje ayarları" lede="Erişim, anahtarlar, bildirim kanalları, takvimler ve saklama kuralları." />

    <section className="surface" id="members">
      <div className="surface-head"><div><h2>Kullanıcılar</h2><p>Sahip her şeyi yönetir, operatör akış ve alarm işlemlerini yapar, izleyici yalnızca görüntüler.</p></div>
        <span className="count">{members.length} kullanıcı</span></div>
      <PagedTable unit="kullanıcı"
        head={<tr><th>Kullanıcı</th><th>Rol</th><th>Durum</th><th></th></tr>}
        rows={members.map(m => <tr key={m.id}><td>{m.name}<small>{m.email}</small></td><td>{roleNames[m.role]}</td>
          <td>{m.activated ? <span className="badge good">Etkin</span> : <span className="badge info">Davet bekliyor</span>}</td>
          <td><div className="cell-actions"><ActionForm action={adminAction} label="Projeden çıkar" compact tone="danger"><Op value="member-remove" id={m.id} /></ActionForm></div></td></tr>)}
        empty={<Empty icon="users" title="Henüz kullanıcı yok" />} />
      <div className="surface-body">
        <h3>E-posta ile davet et</h3>
        <ActionForm action={adminAction} label="Davet gönder"><Op value="member-invite" />
          <div className="form-grid">
            <div className="field"><label htmlFor="i-name">Ad soyad</label><input id="i-name" name="name" required maxLength={120} /></div>
            <div className="field"><label htmlFor="i-email">E-posta</label><input id="i-email" name="email" type="email" required maxLength={254} /></div>
            <div className="field"><label htmlFor="i-role">Rol</label><select id="i-role" name="role" defaultValue="viewer">
              <option value="viewer">İzleyici</option><option value="operator">Operatör</option><option value="owner">Proje sahibi</option></select></div>
          </div>
        </ActionForm>
        <Explain title="Davet nasıl işler?">
          <p>Kullanıcıya 7 gün geçerli bir bağlantı gönderilir; parolasını kendisi belirler. Aynı e-postayla kayıtlı bir hesap varsa yalnızca projeye eklenir ve bilgilendirilir. Daveti yeniden göndermek için aynı formu tekrar kullanın.</p>
        </Explain>
        <details className="details"><summary>Parola belirleyerek ekle ya da rol değiştir</summary>
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
        </details>
      </div>
    </section>

    <section className="surface" id="keys">
      <div className="surface-head"><div><h2>API anahtarları</h2><p>Kaynak sistemler anahtarı X-API-Key başlığında kullanır. İptal edilen anahtar hemen geçersiz olur.</p></div>
        <span className="count">{activeKeys.length} etkin</span></div>
      <PagedTable unit="anahtar"
        head={<tr><th>Ad</th><th>Yetki</th><th>Önek</th><th>Durum</th><th></th></tr>}
        rows={[...activeKeys, ...revokedKeys].map(k => <tr key={k.id}><td>{k.name}</td><td>{scopeNames[k.scope] ?? k.scope}</td>
          <td><code>{k.key_prefix}…</code></td>
          <td>{k.revoked_at ? <span className="badge">İptal edildi</span> : <span className="badge good">Etkin</span>}</td>
          <td><div className="cell-actions">{!k.revoked_at && <ActionForm action={adminAction} label="İptal et" compact tone="danger"><Op value="key-revoke" id={k.id} /></ActionForm>}</div></td></tr>)}
        empty={<Empty icon="key" title="Henüz anahtar yok">Bir kaynak sistemi bağlamak için aşağıdan anahtar oluşturun.</Empty>} />
      <div className="surface-body">
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
      <div className="surface-head"><div><h2>Bildirim kanalları</h2><p>Etkin kanallara açılan ve kapanan alarmlar gönderilir.</p></div>
        <span className="count">{channels.filter(c => c.enabled).length} etkin</span></div>
      <PagedTable unit="kanal"
        head={<tr><th>Kanal</th><th>Tür</th><th>Hedef</th><th>Durum</th><th></th></tr>}
        rows={channels.map(c => <tr key={c.id}><td>{c.name}</td><td>{channelKindNames[c.kind] ?? c.kind}</td>
          <td className="mono" style={{ overflowWrap: "anywhere", fontSize: 12.5 }}>{c.target}</td>
          <td>{c.enabled ? <span className="badge good">Etkin</span> : <span className="badge">Kapalı</span>}</td>
          <td><div className="cell-actions"><ActionForm action={adminAction} label={c.enabled ? "Kapat" : "Etkinleştir"} compact tone="secondary">
            <Op value="channel-toggle" id={c.id} /><input type="hidden" name="enabled" value={String(!c.enabled)} /></ActionForm></div></td></tr>)}
        empty={<Empty icon="mail" title="Henüz kanal yok">Alarmların e-posta, Slack, Microsoft Teams ya da webhook ile iletilmesi için kanal ekleyin.</Empty>} />
      <div className="surface-body">
        <h3>Yeni kanal</h3>
        <ActionForm action={adminAction} label="Kanal oluştur"><Op value="channel-create" />
          <div className="form-grid">
            <div className="field"><label htmlFor="c-name">Kanal adı</label><input id="c-name" name="name" required maxLength={120} placeholder="Operasyon ekibi" /></div>
            <div className="field"><label htmlFor="c-kind">Tür</label><select id="c-kind" name="kind" defaultValue="email">
              <option value="email">E-posta</option><option value="slack">Slack</option><option value="teams">Microsoft Teams</option><option value="webhook">Webhook (imzalı JSON)</option></select></div>
            <div className="field"><label htmlFor="c-target">Hedef: e-posta adresi ya da webhook adresi</label><input id="c-target" name="target" required maxLength={2048} placeholder="ops@sirket.com ya da https://hooks.slack.com/services/…" />
              <span className="hint">Slack için kanalın Incoming Webhook adresi, Teams için Incoming Webhook ya da Workflows adresi.</span></div>
            <div className="field"><label htmlFor="c-secret">Webhook ortak sırrı</label>
              <input id="c-secret" name="secret" type="password" minLength={32} maxLength={128} autoComplete="off" />
              <span className="hint">Yalnızca imzalı webhook için. Boş bırakılırsa üretilir ve bir kez gösterilir.</span></div>
          </div>
        </ActionForm>
        <Explain title="Kanal kuralları ve Slack / Teams kurulumu">
          <p>Hedef sonradan düzenlenmez; değişecekse eski kanalı kapatıp yeni bir kanal oluşturun. Kanalı kapatmak bekleyen gönderimleri iptal eder. İmzalı webhook adresleri sunucudaki izin listesine uymalıdır.</p>
          <p><strong>Slack:</strong> Slack'te uygulama oluşturup <em>Incoming Webhooks</em> özelliğini açın, bir kanal seçin ve üretilen <code>https://hooks.slack.com/services/…</code> adresini buraya yapıştırın. İleti alarmın nedenini, çalışma talimatını, kaynak sistem ve panel bağlantısını taşır.</p>
          <p><strong>Microsoft Teams:</strong> Kanalda <em>Workflows</em> ile "Webhook isteği alındığında Teams'e gönder" şablonunu ya da <em>Incoming Webhook</em> bağlayıcısını kurun; verilen adresi yapıştırın. İleti Adaptive Card olarak gelir.</p>
        </Explain>
      </div>
    </section>

    <section className="surface" id="calendars">
      <div className="surface-head"><div><h2>Çalışma takvimleri</h2><p>Takvim bağlı akışlarda süreler yalnızca çalışma günü ve saatlerinde işler; tatiller atlanır.</p></div>
        <span className="count">{calendars.length} takvim</span></div>
      <div className="surface-body">
        {calendars.map(c => <details key={c.id} className="details calendar-card"><summary><Icon name="calendar" size={14} /> <strong>{c.name}</strong>, {c.timezone}, {c.start_time}–{c.end_time}, {c.work_days.map(d => dayNames[d]).join(", ")}{c.version_count > 0 && `, ${c.version_count} sürümde kullanılıyor`}</summary>
          <CalendarForm calendar={c} />
          {c.version_count === 0 && <ActionForm action={adminAction} label="Takvimi sil" compact tone="danger"><Op value="calendar-delete" id={c.id} /></ActionForm>}
        </details>)}
        <details className="details"><summary><Icon name="plus" size={14} /> Yeni takvim</summary><CalendarForm /></details>
      </div>
    </section>

    <section className="surface" id="digest">
      <div className="surface-head"><div><h2>Rapor özeti</h2><p>Günlük ya da haftalık özet, etkin e-posta kanallarına seçilen saatte gönderilir. Haftalık özet pazartesi gider.</p></div>
        {digest.settings.updated_at && <span className="count">Son güncelleme <Time value={digest.settings.updated_at} /></span>}</div>
      <div className="surface-body">
        <ActionForm action={adminAction} label="Özet ayarlarını kaydet"><Op value="digest-settings" />
          <div className="form-grid">
            <div className="field"><label htmlFor="dg-period">Dönem</label><select id="dg-period" name="period" defaultValue={digest.settings.digest_period}>
              <option value="none">Gönderme</option><option value="daily">Günlük</option><option value="weekly">Haftalık</option></select></div>
            <div className="field"><label htmlFor="dg-hour">Saat</label><input id="dg-hour" name="hour" type="number" min={0} max={23} defaultValue={digest.settings.digest_hour} /></div>
            <div className="field"><label htmlFor="dg-tz">Saat dilimi</label><input id="dg-tz" name="timezone" maxLength={64} defaultValue={digest.settings.digest_timezone} /></div>
          </div>
        </ActionForm>
        <div className="assign-row">
          <ActionForm action={adminAction} label="Günlük özeti şimdi gönder" compact tone="secondary"><Op value="digest-send" /><input type="hidden" name="period" value="daily" /></ActionForm>
          <ActionForm action={adminAction} label="Haftalık özeti şimdi gönder" compact tone="secondary"><Op value="digest-send" /><input type="hidden" name="period" value="weekly" /></ActionForm>
        </div>
      </div>
      {digest.sent.length > 0 && <PagedTable pageSize={10} unit="gönderim"
        head={<tr><th>Gönderim</th><th>Dönem</th><th>Aralık</th><th className="right">Alıcı</th><th>Tetikleyen</th></tr>}
        rows={digest.sent.map((d, i) => <tr key={i}><td className="nowrap"><Time value={d.sent_at} /></td><td>{d.period === "daily" ? "Günlük" : "Haftalık"}</td>
          <td className="nowrap"><Time value={d.period_from} /> – <Time value={d.period_to} /></td><td className="right num">{d.recipients}</td>
          <td>{d.triggered_by === "schedule" ? "Zamanlayıcı" : d.triggered_by === "manual" ? "Panel" : "API"}</td></tr>)} />}
    </section>

    <section className="surface" id="retention">
      <div className="surface-head"><div><h2>Veri saklama</h2><p>Süresi dolan kayıtlar günde bir silinir. Boş alan süresiz saklama demektir.</p></div>
        {p.updated_at && <span className="count">Son güncelleme <Time value={p.updated_at} /></span>}</div>
      <div className="surface-body">
        <ActionForm action={adminAction} label="Kuralları kaydet"><Op value="retention" />
          <div className="form-grid">
            <div className="field"><label htmlFor="r-tx">Tamamlanan ve hatalı işlemler (gün)</label>
              <input id="r-tx" name="transactions" type="number" min={7} max={3650} defaultValue={p.finished_transaction_days ?? ""} placeholder="süresiz" />
              <span className="hint">İşlemle birlikte olayları, alarm geçmişi ve notları silinir. En az 7 gün.</span></div>
            <div className="field"><label htmlFor="r-nt">Gönderilmiş bildirim kayıtları (gün)</label>
              <input id="r-nt" name="notifications" type="number" min={7} max={3650} defaultValue={p.notification_days ?? ""} placeholder="süresiz" />
              <span className="hint">E-posta ve webhook gönderim denemeleri. En az 7 gün.</span></div>
            <div className="field"><label htmlFor="r-au">Yönetim geçmişi (gün)</label>
              <input id="r-au" name="audit" type="number" min={30} max={3650} defaultValue={p.audit_days ?? ""} placeholder="süresiz" />
              <span className="hint">Sistem ekranındaki denetim kayıtları. En az 30 gün.</span></div>
          </div>
        </ActionForm>
        <Explain title="Silme kuralları">
          <p>Açık alarmı ya da işlenmemiş bildirimi olan işlemler silinmez. Silme işi worker tarafından günde bir çalışır; son çalışmalar aşağıda listelenir.</p>
        </Explain>
      </div>
      {retention.runs.length > 0 && <PagedTable pageSize={10} unit="çalışma"
        head={<tr><th>Çalışma</th><th className="right">İşlem</th><th className="right">Bildirim</th><th className="right">Denetim</th></tr>}
        rows={retention.runs.map((r, i) => <tr key={i}><td className="nowrap"><Time value={r.ran_at} /></td>
          <td className="right num">{number(r.transactions_deleted)}</td><td className="right num">{number(r.deliveries_deleted)}</td><td className="right num">{number(r.audit_deleted)}</td></tr>)} />}
    </section>
  </>;
}
