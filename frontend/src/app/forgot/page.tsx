import Link from "next/link";
import ActionForm from "@/components/action-form";
import { forgotAction } from "@/app/account-actions";

export default function ForgotPage() {
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      <h1>Parola sıfırlama</h1>
      <p className="lede">E-posta adresinizi yazın. Hesap varsa 1 saat geçerli bir bağlantı gönderilir.</p>
      <ActionForm action={forgotAction} label="Bağlantı gönder" busy="Gönderiliyor…">
        <div className="field"><label htmlFor="email">E-posta</label>
          <input id="email" name="email" type="email" required maxLength={254} autoComplete="username" autoFocus /></div>
      </ActionForm>
      <p className="help" style={{ marginTop: 16 }}><Link href="/login" prefetch={false}>Girişe dön</Link></p>
    </div>
  </section>;
}
