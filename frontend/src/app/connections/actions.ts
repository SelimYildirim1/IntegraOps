"use server";

import { api, apiMessage } from "@/lib/api";
import type { MappingTestResult } from "@/lib/pilot-types";

export async function testMapping(template: string, sample: string): Promise<MappingTestResult | { error: string }> {
  let t: unknown, s: unknown;
  try { t = JSON.parse(template); s = JSON.parse(sample); }
  catch { return { error: "Şablon ya da örnek veri geçerli JSON değil." }; }
  if (template.length > 4000 || sample.length > 65536) return { error: "Şablon 4 KB, örnek 64 KB sınırını aşamaz." };
  try { return await api<MappingTestResult>("/connections/test", "POST", { template: t, sample: s }); }
  catch (e) { return { error: apiMessage(e) }; }
}
