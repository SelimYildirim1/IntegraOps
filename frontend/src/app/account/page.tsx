import { api, apiMessage } from "@/lib/api";
import type { SecurityInfo } from "@/lib/pilot-types";
import { securityKinds } from "@/lib/pilot-types";
import { Message, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { mfaAction, passwordAction } from "@/app/account-actions";

export const dynamic = "force-dynamic";

export default async function AccountPage() {
  let security: SecurityInfo;
  try { security = await api<SecurityInfo>("/me/security", "GET", undefined, false); }
  catch (e) { return <Message title="Hesap yüklenemedi" description={apiMessage(e)} />; }
  return <>
    <PageHead title="Hesabım" lede="Parola, iki adımlı doğrulama ve hesabınızdaki güvenlik olayları." />
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Parola</h2></div>
        <div className="surface-body">
          <p className="help">Değişiklikten sonra bütün oturumlarınız kapanır ve yeniden giriş yapmanız istenir.</p>
          <ActionForm action={passwordAction} label="Parolayı değiştir" busy="Kaydediliyor…">
            <div className="field"><label htmlFor="cur">Mevcut parola</label>
              <input id="cur" type="password" name="currentPassword" required maxLength={128} autoComplete="current-password" /></div>
            <div className="field"><label htmlFor="new">Yeni parola</label>
              <input id="new" type="password" name="newPassword" required minLength={12} maxLength={128} autoComplete="new-password" />
              <span className="hint">En az 12 karakter.</span></div>
          </ActionForm>
        </div>
      </section>
      <section className="surface" id="mfa">
        <div className="surface-head"><div><h2>İki adımlı doğrulama</h2><p>Girişte parolaya ek olarak doğrulayıcı uygulamadan alınan kod istenir.</p></div>
          <span className={`badge ${security.mfaEnabled ? "good" : "warn"}`}>{security.mfaEnabled ? "Açık" : "Kapalı"}</span></div>
        <div className="surface-body">
          {security.mfaEnabled
            ? <ActionForm action={mfaAction} label="İki adımlı doğrulamayı kapat" tone="danger"><input type="hidden" name="op" value="disable" />
              <div className="field"><label htmlFor="d-pass">Parola</label><input id="d-pass" name="password" type="password" required maxLength={128} autoComplete="current-password" /></div>
              <div className="field"><label htmlFor="d-code">Uygulamadaki kod</label><input id="d-code" name="code" inputMode="numeric" required minLength={6} maxLength={6} autoComplete="one-time-code" /></div>
              <p className="help">Kapatınca bütün oturumlar kapanır.</p></ActionForm>
            : <>
              <p className="help">1. Gizli anahtarı üretin ve doğrulayıcı uygulamanıza ekleyin. 2. Uygulamanın ürettiği 6 haneli kodu girerek doğrulayın. Kurtarma kodları bir kez gösterilir.</p>
              <ActionForm action={mfaAction} label="Gizli anahtar üret" tone="secondary" compact><input type="hidden" name="op" value="begin" /></ActionForm>
              <ActionForm action={mfaAction} label="Kodu doğrula ve aç"><input type="hidden" name="op" value="enable" />
                <div className="field"><label htmlFor="e-code">Uygulamadaki kod</label><input id="e-code" name="code" inputMode="numeric" required minLength={6} maxLength={6} autoComplete="one-time-code" /></div></ActionForm>
            </>}
        </div>
      </section>
    </div>
    <section className="surface">
      <div className="surface-head"><div><h2>Güvenlik olayları</h2><p>Hesabınızla ilgili son 100 olay. Tanımadığınız bir giriş görürseniz parolanızı değiştirin.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Olay</th><th>IP</th><th>Tarayıcı</th><th>Ayrıntı</th></tr></thead><tbody>
        {security.events.map(e => <tr key={e.id}><td><Time value={e.created_at} /></td><td>{securityKinds[e.kind] ?? e.kind}</td><td className="mono">{e.ip ?? "-"}</td>
          <td className="muted" title={e.user_agent ?? ""}>{(e.user_agent ?? "-").slice(0, 40)}</td><td className="muted">{e.detail ?? ""}</td></tr>)}
        {security.events.length === 0 && <tr><td colSpan={5} className="muted">Kayıt yok.</td></tr>}
      </tbody></table></div>
    </section>
  </>;
}
