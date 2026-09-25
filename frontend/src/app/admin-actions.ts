"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

export async function adminAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "");
  const op = field("op"); const id = field("id");
  const withId = ["member-remove", "key-revoke", "channel-toggle", "ack", "retry", "assign", "note", "view-delete", "calendar-delete", "calendar-update", "connection-delete", "key-rotate", "ai-analyze", "ai-feedback"].includes(op);
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
      case "key-create": result = await api("/keys", "POST", { name: field("name"), scope: field("scope"), connectionId: field("connectionId") || null }); break;
      case "key-rotate": {
        const grace = field("graceHours").trim();
        result = await api("/keys/" + id + "/rotate", "POST", { graceHours: grace === "" ? 24 : Number(grace) });
        message = "Yeni anahtar üretildi. Eski anahtar geçiş süresi boyunca çalışmaya devam eder."; break;
      }
      case "connection-create": {
        let template: unknown = null;
        const raw = field("template").trim();
        if (raw) { try { template = JSON.parse(raw); } catch { return { message: "Şablon geçerli JSON değil." }; } }
        await api("/connections", "POST", { name: field("name"), kind: field("kind"), description: field("description"), template, templateCode: field("templateCode") || null });
        message = "Bağlantı oluşturuldu."; break;
      }
      case "connection-delete": await api(`/connections/${id}`, "DELETE"); message = "Bağlantı silindi."; break;
      case "key-revoke": await api(`/keys/${id}`, "DELETE"); break;
      case "template-create": {
        const flowCode = field("code").trim();
        if (flowCode && !/^[a-z][a-z0-9-]{0,63}$/.test(flowCode)) return { message: "Akış kodu küçük harfle başlamalı; harf, rakam ve tire kullanın." };
        const created = await api<{ code: string }>("/flows/from-template", "POST", { template: field("template"), code: flowCode || null, name: field("name").trim() || null });
        revalidatePath("/flows"); revalidatePath("/");
        redirect(`/flows/view/${encodeURIComponent(created.code)}?template=1`);
      }
      case "demo-generate": {
        const flowCode = field("flow");
        if (!/^[a-z][a-z0-9-]{0,63}$/.test(flowCode)) return { message: "Akış kodu geçersiz." };
        const r = await api<{ transactions: number; events: number }>(`/flows/${flowCode}/demo`, "POST", { count: Number(field("count") || 30), days: Number(field("days") || 14) });
        revalidatePath("/"); revalidatePath("/alarms"); revalidatePath("/reports"); revalidatePath(`/flows/view/${flowCode}`);
        message = `${r.transactions} örnek işlem ve ${r.events} bildirim üretildi. Worker işledikçe listeye düşer; süresi geçen adımlar için alarmlar birkaç dakika içinde açılır.`; break;
      }
      case "demo-delete": {
        const flowCode = field("flow");
        if (!/^[a-z][a-z0-9-]{0,63}$/.test(flowCode)) return { message: "Akış kodu geçersiz." };
        const r = await api<{ transactions: number }>(`/flows/${flowCode}/demo`, "DELETE");
        revalidatePath("/"); revalidatePath("/alarms"); revalidatePath("/reports"); revalidatePath(`/flows/view/${flowCode}`);
        message = r.transactions ? `${r.transactions} örnek işlem ve bağlı kayıtları silindi.` : "Silinecek örnek veri yoktu."; break;
      }
      case "channel-create": result = await api("/channels", "POST", { name: field("name"), kind: field("kind"), target: field("target"), secret: field("secret") || null }); break;
      case "channel-toggle": await api(`/channels/${id}`, "PUT", { enabled: field("enabled") === "true" }); break;
      case "ack": await api(`/alarms/${id}/acknowledge`, "POST"); break;
      case "retry": await api(`/deliveries/${id}/retry`, "POST"); break;
      case "assign": await api(`/alarms/${id}/assign`, "POST", { userId: field("userId") }); message = field("userId") ? "Sorumlu güncellendi." : "Sorumlu kaldırıldı."; break;
      case "note": await api(`/alarms/${id}/notes`, "POST", { body: field("body") }); message = "Not eklendi."; break;
      case "ai-analyze": await api(`/alarms/${id}/ai-analyses`, "POST"); message = "İnceleme hazırlandı."; break;
      case "ai-analyze-transaction": {
        const flow = field("flow"), transactionId = field("transactionId");
        if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow) || !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(transactionId)) return { message: "İşlem kimliği geçersiz." };
        await api(`/flows/${encodeURIComponent(flow)}/transactions/${encodeURIComponent(transactionId)}/ai-analyses`, "POST"); message = "İnceleme hazırlandı."; break;
      }
      case "ai-feedback": await api(`/ai/analyses/${id}/feedback`, "PUT", { rating: field("rating"), comment: field("comment") || null }); message = "Geri bildiriminiz kaydedildi."; break;
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
      case "digest-settings": await api("/reports/digest", "PUT", { period: field("period"), hour: Number(field("hour")), timeZone: field("timezone") }); message = "Özet ayarları kaydedildi."; break;
      case "digest-send": { const r = await api<{ recipients: number }>("/reports/digest", "POST", { period: field("period") });
        message = r.recipients > 0 ? `Özet ${r.recipients} e-posta kanalına kuyruğa alındı.` : "Etkin e-posta kanalı yok; özet gönderilmedi."; break; }
      case "retention": {
        const values = { finishedTransactionDays: days("transactions"), notificationDays: days("notifications"), auditDays: days("audit") };
        if (Object.values(values).some(Number.isNaN)) return { message: "Gün değerleri tam sayı olmalı." };
        await api("/retention", "PUT", values); message = "Saklama kuralları kaydedildi.";
        break;
      }
      default: return { message: "İşlem tanınmadı." };
    }
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/settings"); revalidatePath("/alarms", "layout"); revalidatePath("/operations"); revalidatePath("/connections");
  const path = field("path"); if (path === "/" || path === "/alarms") revalidatePath(path);
  return { ok: true, message, secret: result?.key || result?.secret };
}
