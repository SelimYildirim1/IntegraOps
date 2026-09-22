import Link from "next/link";
import ActionForm from "@/components/action-form";
import { signupAction } from "@/app/account-actions";

export default function SignupPage() {
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      <h1>Hesap açın</h1>
      <p className="lede">14 günlük deneme hemen başlar: 3 proje, 5 kullanıcı ve günde 5.000 olay. Kart bilgisi istenmez.</p>
      <ActionForm action={signupAction} label="Hesabı aç" busy="Oluşturuluyor…">
        <div className="field"><label htmlFor="org">Şirket / ekip adı</label><input id="org" name="organizationName" required maxLength={160} autoFocus /></div>
        <div className="field"><label htmlFor="name">Adınız</label><input id="name" name="name" required maxLength={120} autoComplete="name" /></div>
        <div className="field"><label htmlFor="email">İş e-postası</label><input id="email" name="email" type="email" required maxLength={254} autoComplete="username" /></div>
        <div className="field"><label htmlFor="password">Parola</label>
          <input id="password" name="password" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /><span className="hint">En az 12 karakter.</span></div>
        <div className="field"><label htmlFor="confirm">Parola (tekrar)</label><input id="confirm" name="confirm" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /></div>
      </ActionForm>
      <p className="help" style={{ marginTop: 16 }}>Zaten hesabınız var mı? <Link href="/login" prefetch={false}>Giriş yapın</Link></p>
    </div>
  </section>;
}
