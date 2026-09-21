"use server";

import { revalidatePath } from "next/cache";
import { ApiError, apiMessage, createFlow } from "@/lib/api";
import { validateFlow, type FlowFormState } from "@/lib/flow-form";

export async function createFlowAction(
  _previous: FlowFormState,
  formData: FormData,
): Promise<FlowFormState> {
  const payload = formData.get("payload");
  let raw: unknown;
  try {
    if (typeof payload !== "string" || payload.length > 32768) throw new Error();
    raw = JSON.parse(payload);
  } catch {
    return { message: "Form verisi okunamadı. Sayfayı yenileyip tekrar deneyin.", errors: {} };
  }
  const result = validateFlow(raw);
  if (!result.ok) return { message: "İşaretli alanları kontrol edin.", errors: result.errors };
  try { await createFlow(result.data); }
  catch (error) {
    return {
      message: apiMessage(error),
      errors: error instanceof ApiError && error.status === 409 ? { "flow-code": "Bu kod zaten kullanılıyor." } : {},
    };
  }
  revalidatePath("/flows");
  revalidatePath("/");
  return { message: "", errors: {}, createdCode: result.data.code };
}
