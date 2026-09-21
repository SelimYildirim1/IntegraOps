import ActionForm from "@/components/action-form";
import { loginAction } from "@/app/account-actions";

export default function LoginPage() {
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      <h1>Giriş yapın</h1>
      <p className="lede">Projelerinize, işlem akışlarına ve alarmlara erişin.</p>
      <ActionForm action={loginAction} label="Giriş yap" busy="Doğrulanıyor…">
        <div className="field"><label htmlFor="email">E-posta</label>
          <input id="email" name="email" type="email" required maxLength={254} autoComplete="username" autoFocus /></div>
        <div className="field"><label htmlFor="password">Parola</label>
          <input id="password" name="password" type="password" required maxLength={128} autoComplete="current-password" /></div>
      </ActionForm>
    </div>
  </section>;
}
