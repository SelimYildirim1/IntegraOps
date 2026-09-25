"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

const code = /^[a-z][a-z0-9-]{0,63}$/;

export type DraftState = { message: string; ok?: boolean };

// Taslak oluşturur ve düzenleyiciye yönlendirir.
export async function createDraftAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const flow = String(data.get("flow") ?? "");
  if (!code.test(flow)) return { message: "Akış kodu geçersiz." };
  try { await api(`/flows/${flow}/versions`, "POST"); }
  catch (e) { return { message: apiMessage(e) }; }
  revalidatePath(`/flows/view/${flow}`, "layout");
  redirect(`/flows/view/${flow}/draft`);
}

export async function draftAction(_state: DraftState, data: FormData): Promise<DraftState> {
  const flow = String(data.get("flow") ?? ""); const op = String(data.get("op") ?? "");
  const version = Number(data.get("version"));
  if (!code.test(flow) || !Number.isInteger(version) || version < 1) return { message: "Sürüm bilgisi geçersiz." };
  const payload = data.get("payload");
  try {
    if (op === "save" || op === "publish") {
      let parsed: unknown;
      try { parsed = JSON.parse(typeof payload === "string" && payload.length <= 65536 ? payload : "null"); }
      catch { return { message: "Form verisi okunamadı." }; }
      const draft = parsed as { name: string; note: string; calendarId: string; steps: { code: string; name: string; maxDelaySeconds: number | null; overrides: Record<string, number>; instructions?: string; link?: string }[] };
      await api(`/flows/${flow}/versions/${version}`, "PUT", {
        name: draft.name, note: draft.note, calendarId: draft.calendarId || null, steps: draft.steps,
      });
      if (op === "publish") {
        const effectiveFrom = String(data.get("effectiveFrom") ?? "").trim();
        const result = await api<{ version: number; effectiveFrom: string }>(`/flows/${flow}/versions/${version}/publish`, "POST",
          { effectiveFrom: effectiveFrom ? new Date(effectiveFrom).toISOString() : null });
        revalidatePath(`/flows/view/${flow}`, "layout"); revalidatePath("/flows");
        redirect(`/flows/view/${flow}?published=${result.version}`);
      }
      revalidatePath(`/flows/view/${flow}`, "layout");
      return { ok: true, message: "Taslak kaydedildi." };
    }
    if (op === "delete") {
      await api(`/flows/${flow}/versions/${version}`, "DELETE");
    } else return { message: "İşlem tanınmadı." };
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath(`/flows/view/${flow}`, "layout");
  redirect(`/flows/view/${flow}`);
}
