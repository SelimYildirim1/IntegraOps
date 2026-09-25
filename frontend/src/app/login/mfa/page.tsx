import Link from "next/link";
import ActionForm from "@/components/action-form";
import { mfaLoginAction } from "@/app/account-actions";

export default async function MfaPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const challenge = String(params.c ?? ""); const remember = params.r === "1";
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      <h1>İkinci adım</h1>
      <p className="lede">Doğrulayıcı uygulamanızdaki 6 haneli kodu ya da kurtarma kodlarınızdan birini girin. Kod 5 dakika içinde girilmelidir.</p>
      <ActionForm action={mfaLoginAction} label="Doğrula ve giriş yap" busy="Doğrulanıyor…">
        <input type="hidden" name="challenge" value={challenge} />
        {remember && <input type="hidden" name="remember" value="1" />}
        <div className="field"><label htmlFor="code">Doğrulama kodu</label>
          <input id="code" name="code" inputMode="numeric" autoComplete="one-time-code" required minLength={6} maxLength={12} autoFocus /></div>
      </ActionForm>
      <p className="help" style={{ marginTop: 16 }}><Link href="/login" prefetch={false}>Girişe dön</Link></p>
    </div>
  </section>;
}
