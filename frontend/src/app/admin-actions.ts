"use server";

import { revalidatePath } from "next/cache";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

export async function adminAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "");
  const op = field("op"); const id = field("id");
  const withId = ["member-remove", "key-revoke", "channel-toggle", "ack", "retry", "assign", "note", "view-delete", "calendar-delete", "calendar-update"].includes(op);
  if (withId && !/^[0-9a-f-]{36}$/i.test(id)) return { message: "Kayıt kimliği geçersiz." };
  const days = (name: string) => { const v = field(name).trim(); return v === "" ? null : /^\d{1,4}$/.test(v) ? Number(v) : NaN; };
  let result: { key?: string; secret?: string; invited?: boolean } | undefined;
  let message = "İşlem tamamlandı.";
  try {
    switch (op) {
      case "member-save": await api("/members", "POST", { email: field("email"), name: field("name"), role: field("role"), password: field("password") || null }); break;
      case "member-invite": {
        result = await api("/members/invite", "POST", { email: field("email"), name: field("name"), role: field("role") });
        message = result?.invited ? "Davet e-postası kuyruğa alındı. Kullanıcı bağlantıyla parolasını belirleyince giriş yapabilir."
          : "Mevcut hesap projeye eklendi ve bilgilendirme e-postası kuyruğa alındı.";
        break;
      }
      case "member-remove": await api(`/members/${id}`, "DELETE"); break;
      case "key-create": result = await api("/keys", "POST", { name: field("name"), scope: field("scope") }); break;
      case "key-revoke": await api(`/keys/${id}`, "DELETE"); break;
      case "channel-create": result = await api("/channels", "POST", { name: field("name"), kind: field("kind"), target: field("target"), secret: field("secret") || null }); break;
      case "channel-toggle": await api(`/channels/${id}`, "PUT", { enabled: field("enabled") === "true" }); break;
      case "ack": await api(`/alarms/${id}/acknowledge`, "POST"); break;
      case "retry": await api(`/deliveries/${id}/retry`, "POST"); break;
      case "assign": await api(`/alarms/${id}/assign`, "POST", { userId: field("userId") }); message = field("userId") ? "Sorumlu güncellendi." : "Sorumlu kaldırıldı."; break;
      case "note": await api(`/alarms/${id}/notes`, "POST", { body: field("body") }); message = "Not eklendi."; break;
      case "view-save": await api("/views", "POST", { kind: field("kind"), name: field("name"), query: field("query") }); message = "Görünüm kaydedildi."; break;
      case "view-delete": await api(`/views/${id}`, "DELETE"); message = "Görünüm silindi."; break;
      case "calendar-create": case "calendar-update": {
        const holidays = field("holidays").split(/\r?\n/).map(l => l.trim()).filter(Boolean).map(l => {
          const [day, ...rest] = l.split(/\s+/); return { day, name: rest.join(" ") };
        });
        const body = { name: field("name"), timeZone: field("timezone"), workDays: data.getAll("workDays").map(Number),
          startTime: field("startTime"), endTime: field("endTime"), holidays };
        if (op === "calendar-create") await api("/calendars", "POST", body); else await api(`/calendars/${id}`, "PUT", body);
        message = "Takvim kaydedildi."; break;
      }
      case "calendar-delete": await api(`/calendars/${id}`, "DELETE"); message = "Takvim silindi."; break;
      case "retention": {
        const values = { finishedTransactionDays: days("transactions"), notificationDays: days("notifications"), auditDays: days("audit") };
        if (Object.values(values).some(Number.isNaN)) return { message: "Gün değerleri tam sayı olmalı." };
        await api("/retention", "PUT", values); message = "Saklama kuralları kaydedildi.";
        break;
      }
      default: return { message: "İşlem tanınmadı." };
    }
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/settings"); revalidatePath("/alarms", "layout"); revalidatePath("/operations");
  const path = field("path"); if (path === "/" || path === "/alarms") revalidatePath(path);
  return { ok: true, message, secret: result?.key || result?.secret };
}
