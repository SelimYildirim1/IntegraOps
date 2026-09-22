import Link from "next/link";
import { getTokenInfo } from "@/lib/api";
import ActionForm from "@/components/action-form";
import { tokenPasswordAction } from "@/app/account-actions";

// Davet ve parola sıfırlama sayfalarının ortak gövdesi: bağlantıyı doğrular, parola formunu gösterir.
export default async function TokenForm({ kind, token }: { kind: "invite" | "reset"; token: string }) {
  let info: { email: string; name: string } | null = null;
  if (/^[0-9a-f]{64}$/.test(token)) {
    try { const t = await getTokenInfo(token); if (t.kind === kind) info = t; } catch { /* Invalid or expired; explained below. */ }
  }
  return <section className="surface auth">
    <div className="surface-body">
      <div className="auth-brand"><span className="brand-mark">io</span>IntegraOps</div>
      {!info ? <>
        <h1>Bağlantı geçersiz</h1>
        <p className="lede">{kind === "invite" ? "Davet bağlantısı kullanılmış ya da süresi dolmuş. Proje sahibinden yeni davet isteyin."
          : "Parola sıfırlama bağlantısı kullanılmış ya da süresi dolmuş. Yeni bağlantı isteyebilirsiniz."}</p>
        <p className="help"><Link href={kind === "invite" ? "/login" : "/forgot"} prefetch={false}>{kind === "invite" ? "Girişe dön" : "Yeni bağlantı iste"}</Link></p>
      </> : <>
        <h1>{kind === "invite" ? `Hoş geldiniz, ${info.name}` : "Yeni parola"}</h1>
        <p className="lede">{kind === "invite" ? "Hesabınızı tamamlamak için parolanızı belirleyin." : "Hesabınız için yeni bir parola belirleyin."} <span className="mono">{info.email}</span></p>
        <ActionForm action={tokenPasswordAction} label={kind === "invite" ? "Hesabı tamamla" : "Parolayı kaydet"} busy="Kaydediliyor…">
          <input type="hidden" name="kind" value={kind} /><input type="hidden" name="token" value={token} />
          <div className="field"><label htmlFor="password">Parola</label>
            <input id="password" name="password" type="password" required minLength={12} maxLength={128} autoComplete="new-password" autoFocus />
            <span className="hint">En az 12 karakter.</span></div>
          <div className="field"><label htmlFor="confirm">Parola (tekrar)</label>
            <input id="confirm" name="confirm" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /></div>
        </ActionForm>
      </>}
    </div>
  </section>;
}
