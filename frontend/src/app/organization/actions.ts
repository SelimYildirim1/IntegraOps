"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

const uuid = /^[0-9a-f-]{36}$/i;

/// Organization management: members, groups, name, SSO, exports, deletion. Roles are checked by the API.
export async function orgAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "").trim();
  const op = field("op"); const org = field("org"); const id = field("id");
  if (!uuid.test(org)) return { message: "Organizasyon kimliği geçersiz." };
  if (["member-remove", "group-update", "group-delete"].includes(op) && !uuid.test(id)) return { message: "Kayıt kimliği geçersiz." };
  const base = `/organizations/${org}`;
  let message = "İşlem tamamlandı.";
  try {
    switch (op) {
      case "rename": await api(base, "PUT", { name: field("name") }, false); message = "Organizasyon adı güncellendi."; break;
      case "member-put": await api(base + "/members", "POST", { email: field("email"), role: field("role") }, false); message = "Organizasyon üyeliği kaydedildi."; break;
      case "member-remove": await api(`${base}/members/${id}`, "DELETE", undefined, false); message = "Üye organizasyondan çıkarıldı."; break;
      case "group-create": case "group-update": {
        const members = field("members").split(/\r?\n/).map(l => l.trim()).filter(Boolean).map(line => {
          const [email, role] = line.split(/[\s,;]+/); return { email, role: role || "viewer" };
        });
        const body = { name: field("name"), projects: data.getAll("projects").map(String).filter(p => uuid.test(p)), members };
        if (op === "group-create") await api(base + "/groups", "POST", body, false); else await api(`${base}/groups/${id}`, "PUT", body, false);
        message = "Proje grubu kaydedildi."; break;
      }
      case "group-delete": await api(`${base}/groups/${id}`, "DELETE", undefined, false); message = "Grup silindi."; break;
      case "sso-save":
        await api(base + "/sso", "PUT", { issuer: field("issuer"), clientId: field("clientId"), clientSecret: field("clientSecret") || null, domain: field("domain"), required: field("required") === "on" }, false);
        message = field("issuer") ? "Tek oturum açma ayarı kaydedildi. Sağlayıcıdaki yönlendirme adresi API adresinizin /api/v1/auth/sso/callback yolu olmalıdır." : "Tek oturum açma kaldırıldı."; break;
      case "export": await api(base + "/exports", "POST", undefined, false); message = "Dışa aktarım kuyruğa alındı. Hazır olunca bu sayfada indirme bağlantısı görünür."; break;
      case "delete-request": {
        await api(base + "/deletion", "POST", { password: field("password") }, false);
        message = "Silme isteği alındı. Hesap askıya alındı; 7 gün sonra bütün veriler kalıcı olarak silinir. Bu süre içinde iptal edebilirsiniz."; break;
      }
      case "delete-cancel": await api(base + "/deletion", "POST", { cancel: true }, false); message = "Silme isteği iptal edildi. Planın yeniden etkinleştirilmesi için destekle iletişime geçin."; break;
      default: return { message: "İşlem tanınmadı." };
    }
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/organization"); revalidatePath("/projects");
  return { ok: true, message };
}

export async function switchOrganization(data: FormData) {
  const id = String(data.get("organization") ?? "");
  if (!uuid.test(id)) return;
  (await cookies()).set("io_org", id, { httpOnly: true, sameSite: "lax", secure: process.env.PANEL_COOKIE_SECURE === "true", path: "/", maxAge: 28800 });
  redirect("/organization");
}

/// System administration: plan and limit overrides per customer.
export async function planAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "").trim();
  const id = field("id");
  if (!uuid.test(id)) return { message: "Organizasyon kimliği geçersiz." };
  const limit = (name: string) => { const v = field(name); return v === "" ? null : /^\d{1,9}$/.test(v) ? Number(v) : NaN; };
  const limits = { maxProjects: limit("maxProjects"), maxUsers: limit("maxUsers"), maxEventsPerDay: limit("maxEventsPerDay"), maxRetentionDays: limit("maxRetentionDays") };
  if (Object.values(limits).some(Number.isNaN)) return { message: "Sınırlar tam sayı olmalı; boş bırakılan alan plan varsayılanını kullanır." };
  try { await api(`/admin/organizations/${id}/plan`, "PUT", { plan: field("plan") || null, note: field("note") || null, ...limits }, false); }
  catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/admin");
  return { ok: true, message: "Plan ve sınırlar kaydedildi." };
}
