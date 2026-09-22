"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { api, apiMessage, ApiError, completeToken, getProjects, login, requestPasswordReset } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

const cookieOptions = () => ({ httpOnly: true, sameSite: "lax" as const,
  secure: process.env.PANEL_COOKIE_SECURE === "true", path: "/", maxAge: 28800 });

export async function loginAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const email = String(data.get("email") ?? ""); const password = String(data.get("password") ?? "");
  if (email.length > 254 || password.length > 128) return { message: "Giriş bilgilerini kontrol edin." };
  let result;
  try { result = await login(email, password); }
  catch (e) { return { message: e instanceof ApiError && e.status === 401 ? "E-posta veya parola doğrulanamadı." : apiMessage(e) }; }
  const jar = await cookies();
  jar.set("io_session", result.token, cookieOptions()); jar.delete("io_project");
  redirect("/projects");
}
export async function logoutAction() {
  try { await api("/me/logout", "POST", undefined, false); } catch { /* Clear this browser even if the service is unavailable. */ }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project");
  redirect("/login");
}
export async function switchProject(data: FormData) {
  const id = String(data.get("project") ?? ""); const projects = await getProjects();
  if (!projects.some(p => p.id === id)) throw new Error("Proje erişimi bulunamadı.");
  (await cookies()).set("io_project", id, cookieOptions());
  revalidatePath("/", "layout"); redirect("/");
}
export async function createProjectAction(_state: ActionState, data: FormData): Promise<ActionState> {
  try {
    await api("/me/projects", "POST", { name: String(data.get("name") ?? "") }, false);
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
