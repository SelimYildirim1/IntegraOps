"use server";

import { revalidatePath } from "next/cache";
import { ApiError, apiMessage, getFlow, postTestEvent } from "@/lib/api";
import { parseTestEvent, type TestEventResult } from "@/lib/flow-details";

export async function sendTestEvent(value: unknown): Promise<TestEventResult> {
  const input = parseTestEvent(value);
  if (!input) return { ok: false, message: "Test bildirimi geçerli değil. Yeni test başlatın.", retry: false };
  try {
    const flow = await getFlow(input.flow);
    if (!flow.steps.some(step => step.code === input.step)) {
      return { ok: false, message: "Bu adım seçilen akışta bulunmuyor.", retry: false };
    }
    const result = await postTestEvent(input);
    revalidatePath("/");
    revalidatePath(`/transactions/${input.flow}/${input.transactionId}`);
    return { ok: true, duplicate: result.duplicate };
  } catch (error) {
    return { ok: false, message: apiMessage(error), retry: !(error instanceof ApiError) || error.status >= 500 };
  }
}
