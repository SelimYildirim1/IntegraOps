export type FlowDetails = {
  id: string;
  code: string;
  name: string;
  steps: { code: string; name: string; position: number; maxDelaySeconds: number | null }[];
};

export type TestEventPayload = {
  eventId: string; flow: string; transactionId: string;
  step: string; occurredAt: string;
};

export type TestEventResult =
  | { ok: true; duplicate: boolean }
  | { ok: false; message: string; retry: boolean };

export function duration(seconds: number | null) {
  if (seconds === null) return "Başlangıç adımı";
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;
  return [hours && `${hours} saat`, minutes && `${minutes} dakika`, rest && `${rest} saniye`].filter(Boolean).join(" ");
}

export function delayDraft(seconds: number | null) {
  if (seconds === null) return { delayValue: "", delayUnit: "minutes" };
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
