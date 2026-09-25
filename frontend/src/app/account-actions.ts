"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { api, apiMessage, ApiError, completeMfa, completeToken, getProjects, login, requestPasswordReset, signup, startSso } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

const sessionSeconds = 28800;
const cookieOptions = (maxAge = sessionSeconds) => ({ httpOnly: true, sameSite: "lax" as const,
  secure: process.env.PANEL_COOKIE_SECURE === "true", path: "/", maxAge });
// The session cookie lives as long as the API session; the project choice may outlive a short session.
const sessionCookie = (expiresIn?: number) => cookieOptions(expiresIn && expiresIn > 0 ? expiresIn : sessionSeconds);
const projectCookie = () => cookieOptions(30 * 24 * 3600);

export async function loginAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const email = String(data.get("email") ?? ""); const password = String(data.get("password") ?? "");
  const remember = data.get("remember") === "on";
  if (email.length > 254 || password.length > 128) return { message: "Giriş bilgilerini kontrol edin." };
  let result;
  try { result = await login(email, password, remember); }
  catch (e) { return { message: e instanceof ApiError && e.status === 401 ? "E-posta veya parola doğrulanamadı." : apiMessage(e) }; }
  if (result.mfaRequired && result.challenge) redirect("/login/mfa?c=" + result.challenge + (remember ? "&r=1" : ""));
  if (!result.token) return { message: "Giriş yanıtı okunamadı." };
  const jar = await cookies();
  jar.set("io_session", result.token, sessionCookie(result.expiresIn)); jar.delete("io_project");
  redirect("/projects");
}
export async function mfaLoginAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const challenge = String(data.get("challenge") ?? ""); const code = String(data.get("code") ?? "").trim();
  const remember = data.get("remember") === "1";
  if (!/^[0-9a-f]{64}$/.test(challenge)) return { message: "Doğrulama isteği geçersiz. Tekrar giriş yapın." };
  if (code.length < 6 || code.length > 12) return { message: "Uygulamadaki 6 haneli kodu ya da 10 karakterli kurtarma kodunu girin." };
  let result;
  try { result = await completeMfa(challenge, code, remember); }
  catch (e) { return { message: e instanceof ApiError && e.status === 401 ? e.message : apiMessage(e) }; }
  const jar = await cookies();
  jar.set("io_session", result.token, sessionCookie(result.expiresIn)); jar.delete("io_project");
  redirect("/projects");
}
export async function signupAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "").trim();
  if (field("password") !== field("confirm")) return { message: "İki parola aynı değil." };
  let result;
  try { result = await signup({ organizationName: field("organizationName"), name: field("name"), email: field("email"), password: field("password") }); }
  catch (e) { return { message: apiMessage(e) }; }
  const jar = await cookies();
  jar.set("io_session", result.token, cookieOptions()); jar.set("io_project", result.projectId, projectCookie());
  redirect("/projects?welcome=1");
}
export async function ssoStartAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const email = String(data.get("email") ?? "").trim();
  if (email.length > 254) return { message: "E-posta adresini kontrol edin." };
  let result;
  try { result = await startSso(email, null); }
  catch (e) { return { message: e instanceof ApiError && e.status === 404 ? "Bu e-posta alanı için kurumsal giriş tanımlı değil." : apiMessage(e) }; }
  redirect(result.url);
}
export async function logoutAction() {
  try { await api("/me/logout", "POST", undefined, false); } catch { /* Clear this browser even if the service is unavailable. */ }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project");
  redirect("/login");
}
export async function switchProject(data: FormData) {
  const id = String(data.get("project") ?? ""); const projects = await getProjects();
  if (!projects.some(p => p.id === id)) throw new Error("Proje erişimi bulunamadı.");
  (await cookies()).set("io_project", id, projectCookie());
  revalidatePath("/", "layout"); redirect("/");
}
export async function createProjectAction(_state: ActionState, data: FormData): Promise<ActionState> {
  try {
    const organizationId = String(data.get("organizationId") ?? "");
    await api("/me/projects", "POST", { name: String(data.get("name") ?? ""), organizationId: /^[0-9a-f-]{36}$/i.test(organizationId) ? organizationId : null }, false);
    revalidatePath("/projects"); return { ok: true, message: "Proje oluşturuldu. Listeden açabilirsiniz." };
  } catch (e) { return { message: apiMessage(e) }; }
}
export async function forgotAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const email = String(data.get("email") ?? "");
  if (email.length > 254) return { message: "E-posta adresini kontrol edin." };
  try { await requestPasswordReset(email); }
  catch (e) { if (!(e instanceof ApiError && e.status === 400)) return { message: apiMessage(e) }; }
  // Same answer whether or not the address exists.
  return { ok: true, message: "Bu adrese kayıtlı bir hesap varsa sıfırlama bağlantısı gönderildi. Gelen kutunuzu kontrol edin." };
}
export async function tokenPasswordAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const kind = String(data.get("kind") ?? ""); const token = String(data.get("token") ?? "");
  const password = String(data.get("password") ?? ""); const confirm = String(data.get("confirm") ?? "");
  if ((kind !== "invite" && kind !== "reset") || !/^[0-9a-f]{64}$/.test(token)) return { message: "Bağlantı geçersiz." };
  if (password !== confirm) return { message: "İki parola aynı değil." };
  try { await completeToken(kind, token, password); }
  catch (e) { return { message: e instanceof ApiError && e.status === 404 ? "Bağlantı geçersiz ya da süresi dolmuş." : apiMessage(e) }; }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project");
  redirect("/login?done=" + kind);
}
export async function passwordAction(_state: ActionState, data: FormData): Promise<ActionState> {
  try {
    await api("/me/password", "POST", { currentPassword: String(data.get("currentPassword") ?? ""),
      newPassword: String(data.get("newPassword") ?? "") }, false);
  } catch (e) { return { message: apiMessage(e) }; }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project"); redirect("/login");
}

// Second factor management on the account page.
export async function mfaAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const op = String(data.get("op") ?? "");
  try {
    if (op === "begin") {
      const r = await api<{ secret: string; uri: string }>("/me/mfa/begin", "POST", undefined, false);
      revalidatePath("/account");
      return { ok: true, message: "Doğrulayıcı uygulamanıza (Google Authenticator, 1Password, Microsoft Authenticator vb.) bu gizli anahtarı ekleyin, sonra ürettiği kodu aşağıya girin. Bağlantı: " + r.uri, secret: r.secret };
    }
    if (op === "enable") {
      const r = await api<{ recoveryCodes: string[] }>("/me/mfa/enable", "POST", { code: String(data.get("code") ?? "") }, false);
      revalidatePath("/account");
      return { ok: true, message: "İki adımlı doğrulama açıldı. Kurtarma kodlarını güvenli bir yere kaydedin; her biri bir kez kullanılır.", secret: r.recoveryCodes.join("  ") };
    }
    if (op === "disable") {
      await api("/me/mfa/disable", "POST", { password: String(data.get("password") ?? ""), code: String(data.get("code") ?? "") }, false);
      const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project"); redirect("/login");
    }
    return { message: "Bilinmeyen işlem." };
  } catch (e) { return { message: apiMessage(e) }; }
}
