import { PageHead } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { passwordAction } from "@/app/account-actions";

export default function AccountPage() {
  return <>
    <PageHead title="Hesabım" lede="Parolanızı değiştirin. Değişiklikten sonra bütün oturumlarınız kapanır ve yeniden giriş yapmanız istenir." />
    <section className="surface" style={{ maxWidth: 560 }}>
      <div className="surface-head"><h2>Parola</h2></div>
      <div className="surface-body">
        <ActionForm action={passwordAction} label="Parolayı değiştir" busy="Kaydediliyor…">
          <div className="field"><label htmlFor="cur">Mevcut parola</label>
            <input id="cur" type="password" name="currentPassword" required maxLength={128} autoComplete="current-password" /></div>
          <div className="field"><label htmlFor="new">Yeni parola</label>
            <input id="new" type="password" name="newPassword" required minLength={12} maxLength={128} autoComplete="new-password" />
            <span className="hint">En az 12 karakter.</span></div>
        </ActionForm>
      </div>
    </section>
  </>;
}
