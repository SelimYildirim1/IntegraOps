export type FlowStepDetails = { code: string; name: string; position: number; maxDelaySeconds: number | null; overrides?: Record<string, number>;
  stage?: number; kind?: "required" | "optional" | "conditional"; groups?: string[]; instructions?: string; link?: string };
export type FlowTemplate = { code: string; name: string; sector: string; description: string; promise: string; stepCount: number; stageCount: number; groups: string[]; steps: FlowStepDetails[] };
export type FlowVersionRow = {
  id: string; version: number; state: "draft" | "published"; effective_from: string | null; published_at: string | null;
  note: string; created_at: string; calendar: string | null; created_by: string | null; step_count: number; transaction_count: number; effective: boolean;
};
export type FlowDetails = {
  id: string;
  code: string;
  name: string;
  version: number | null;
  versionId: string | null;
  calendar: string | null;
  steps: FlowStepDetails[];
  versions: FlowVersionRow[];
  hasDraft: boolean;
  demoCount?: number;
  transactionCount?: number;
};
export type FlowVersionDetails = {
  flow: string; name: string; version: number; state: "draft" | "published"; effectiveFrom: string | null; publishedAt: string | null;
  calendarId: string | null; note: string; steps: FlowStepDetails[];
};
export type FlowDiffResult = { from: number; to: number; changes: { kind: string; code: string; detail: string | null }[] };

export type TestEventPayload = {
  eventId: string; flow: string; transactionId: string;
  step: string; occurredAt: string;
};

export type TestEventResult =
  | { ok: true; duplicate: boolean }
  | { ok: false; message: string; retry: boolean };

export function duration(seconds: number | null) {
  if (seconds === null) return "Başlangıç adımı";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;
  return [days && `${days} gün`, hours && `${hours} saat`, minutes && `${minutes} dakika`, rest && `${rest} saniye`].filter(Boolean).join(" ");
}

export function delayDraft(seconds: number | null) {
  if (seconds === null) return { delayValue: "", delayUnit: "minutes" };
  if (seconds % 86400 === 0) return { delayValue: String(seconds / 86400), delayUnit: "days" };
  if (seconds % 3600 === 0) return { delayValue: String(seconds / 3600), delayUnit: "hours" };
  if (seconds % 60 === 0) return { delayValue: String(seconds / 60), delayUnit: "minutes" };
  return { delayValue: String(seconds), delayUnit: "seconds" };
}

export function parseTestEvent(value: unknown): TestEventPayload | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const raw = value as Record<string, unknown>;
  const { eventId, flow, transactionId, step, occurredAt } = raw;
  if (typeof eventId !== "string" || typeof flow !== "string" || typeof transactionId !== "string"
    || typeof step !== "string" || typeof occurredAt !== "string") return null;
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const code = /^[a-z][a-z0-9-]{0,63}$/;
  if (!uuid.test(eventId) || eventId === "00000000-0000-0000-0000-000000000000"
    || !transactionId.startsWith("TEST-") || !uuid.test(transactionId.slice(5))
    || !code.test(flow) || !code.test(step)) return null;
  if (occurredAt.length > 40 || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(occurredAt)
    || !Number.isFinite(Date.parse(occurredAt))) return null;
  return { eventId, flow, transactionId, step, occurredAt };
}
