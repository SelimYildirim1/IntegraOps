import Link from "next/link";
import ActionForm from "@/components/action-form";
import { loginAction, ssoStartAction } from "@/app/account-actions";

const ssoMessages: Record<string, string> = {
  invalid: "Kurumsal giriş doğrulanamadı. Tekrar deneyin.", expired: "Kurumsal giriş süresi doldu. Tekrar deneyin.",
  token: "Kimlik sağlayıcı yanıt vermedi. Yöneticinize başvurun.", claims: "Kimlik sağlayıcının verdiği hesap bu organizasyonun e-posta alanında değil.",
  access_denied: "Kimlik sağlayıcı girişi reddetti.",
};

export default async function LoginPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams; const done = params.done; const sso = typeof params.sso === "string" ? params.sso : "";
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      <h1>Giriş yapın</h1>
      <p className="lede">Projelerinize, işlem akışlarına ve alarmlara erişin.</p>
      {done === "invite" && <p className="callout good">Hesabınız hazır. Yeni parolanızla giriş yapın.</p>}
      {done === "reset" && <p className="callout good">Parolanız değişti. Yeni parolanızla giriş yapın.</p>}
      {sso && <p className="callout bad" role="alert">{ssoMessages[sso] ?? "Kurumsal giriş tamamlanamadı."}</p>}
      <ActionForm action={loginAction} label="Giriş yap" busy="Doğrulanıyor…">
        <div className="field"><label htmlFor="email">E-posta</label>
          <input id="email" name="email" type="email" required maxLength={254} autoComplete="username" autoFocus /></div>
        <div className="field"><label htmlFor="password">Parola</label>
          <input id="password" name="password" type="password" required maxLength={128} autoComplete="current-password" /></div>
        <div className="field"><label className="toggle"><input type="checkbox" name="remember" />Beni hatırla (30 gün oturum açık kalır)</label></div>
      </ActionForm>
      <p className="help" style={{ marginTop: 16 }}><Link href="/forgot" prefetch={false}>Parolamı unuttum</Link> · <Link href="/signup" prefetch={false}>Hesap açın</Link></p>
      <details style={{ marginTop: 20 }}>
        <summary className="help">Kurumsal giriş (tek oturum açma)</summary>
        <div style={{ marginTop: 12 }}>
          <ActionForm action={ssoStartAction} label="Kimlik sağlayıcıya git" tone="secondary" busy="Yönlendiriliyor…">
            <div className="field"><label htmlFor="sso-email">İş e-postası</label>
              <input id="sso-email" name="email" type="email" required maxLength={254} autoComplete="username" /></div>
          </ActionForm>
        </div>
      </details>
    </div>
  </section>;
}
