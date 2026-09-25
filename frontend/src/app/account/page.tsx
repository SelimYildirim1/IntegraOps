import { api, apiMessage } from "@/lib/api";
import type { SecurityInfo } from "@/lib/pilot-types";
import { securityKinds } from "@/lib/pilot-types";
import { Empty, Message, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import PagedTable from "@/components/paged-table";
import { mfaAction, passwordAction } from "@/app/account-actions";

export const dynamic = "force-dynamic";

export default async function AccountPage() {
  let security: SecurityInfo;
  try { security = await api<SecurityInfo>("/me/security", "GET", undefined, false); }
  catch (e) { return <Message title="Hesap yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  return <>
    <PageHead icon="user" title="Hesabım" lede="Parola, iki adımlı doğrulama ve hesabınızdaki güvenlik olayları." />
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Parola</h2></div>
        <div className="surface-body">
          <ActionForm action={passwordAction} label="Parolayı değiştir" busy="Kaydediliyor…">
            <div className="field"><label htmlFor="cur">Mevcut parola</label>
              <input id="cur" type="password" name="currentPassword" required maxLength={128} autoComplete="current-password" /></div>
            <div className="field"><label htmlFor="new">Yeni parola</label>
              <input id="new" type="password" name="newPassword" required minLength={12} maxLength={128} autoComplete="new-password" />
              <span className="hint">En az 12 karakter. Değişiklikten sonra bütün oturumlar kapanır.</span></div>
          </ActionForm>
        </div>
      </section>
      <section className="surface" id="mfa">
        <div className="surface-head"><div><h2>İki adımlı doğrulama</h2><p>Girişte parolaya ek olarak doğrulayıcı uygulamadan kod istenir.</p></div>
          <span className={`badge ${security.mfaEnabled ? "good" : "warn"}`}>{security.mfaEnabled ? "Açık" : "Kapalı"}</span></div>
        <div className="surface-body">
          {security.mfaEnabled
            ? <ActionForm action={mfaAction} label="İki adımlı doğrulamayı kapat" tone="danger"><input type="hidden" name="op" value="disable" />
              <div className="field"><label htmlFor="d-pass">Parola</label><input id="d-pass" name="password" type="password" required maxLength={128} autoComplete="current-password" /></div>
              <div className="field"><label htmlFor="d-code">Uygulamadaki kod</label><input id="d-code" name="code" inputMode="numeric" required minLength={6} maxLength={6} autoComplete="one-time-code" /></div>
              <p className="help">Kapatınca bütün oturumlar kapanır.</p></ActionForm>
            : <>
              <p className="help">Önce gizli anahtarı üretip doğrulayıcı uygulamanıza ekleyin, sonra uygulamanın ürettiği 6 haneli kodu doğrulayın. Kurtarma kodları bir kez gösterilir.</p>
              <ActionForm action={mfaAction} label="Gizli anahtar üret" tone="secondary" compact><input type="hidden" name="op" value="begin" /></ActionForm>
              <ActionForm action={mfaAction} label="Kodu doğrula ve aç"><input type="hidden" name="op" value="enable" />
                <div className="field"><label htmlFor="e-code">Uygulamadaki kod</label><input id="e-code" name="code" inputMode="numeric" required minLength={6} maxLength={6} autoComplete="one-time-code" /></div></ActionForm>
            </>}
        </div>
      </section>
    </div>
    <section className="surface">
      <div className="surface-head"><div><h2>Güvenlik olayları</h2><p>Tanımadığınız bir giriş görürseniz parolanızı değiştirin.</p></div><span className="count">{security.events.length} kayıt</span></div>
      <PagedTable unit="olay"
        head={<tr><th>Zaman</th><th>Olay</th><th>IP</th><th>Tarayıcı</th><th>Ayrıntı</th></tr>}
        rows={security.events.map(e => <tr key={e.id}><td><Time value={e.created_at} /></td><td>{securityKinds[e.kind] ?? e.kind}</td><td className="mono">{e.ip ?? "-"}</td>
          <td className="muted" title={e.user_agent ?? ""}>{(e.user_agent ?? "-").slice(0, 40)}</td><td className="muted">{e.detail ?? ""}</td></tr>)}
        empty={<Empty icon="shield" title="Kayıt yok" />} />
    </section>
  </>;
}
