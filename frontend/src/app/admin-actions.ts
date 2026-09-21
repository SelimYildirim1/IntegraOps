"use server";

import { revalidatePath } from "next/cache";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

export async function adminAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "");
  const op = field("op"); const id = field("id");
  const withId = ["member-remove", "key-revoke", "channel-toggle", "ack", "retry"].includes(op);
  if (withId && !/^[0-9a-f-]{36}$/i.test(id)) return { message: "Kayıt kimliği geçersiz." };
  let result: { key?: string; secret?: string } | undefined;
  try {
    switch (op) {
      case "member-save": await api("/members", "POST", { email: field("email"), name: field("name"), role: field("role"), password: field("password") || null }); break;
      case "member-remove": await api(`/members/${id}`, "DELETE"); break;
      case "key-create": result = await api("/keys", "POST", { name: field("name"), scope: field("scope") }); break;
      case "key-revoke": await api(`/keys/${id}`, "DELETE"); break;
      case "channel-create": result = await api("/channels", "POST", { name: field("name"), kind: field("kind"), target: field("target"), secret: field("secret") || null }); break;
      case "channel-toggle": await api(`/channels/${id}`, "PUT", { enabled: field("enabled") === "true" }); break;
      case "ack": await api(`/alarms/${id}/acknowledge`, "POST"); break;
      case "retry": await api(`/deliveries/${id}/retry`, "POST"); break;
      default: return { message: "İşlem tanınmadı." };
    }
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/settings"); revalidatePath("/alarms", "layout"); revalidatePath("/operations");
  return { ok: true, message: "İşlem tamamlandı.", secret: result?.key || result?.secret };
}
