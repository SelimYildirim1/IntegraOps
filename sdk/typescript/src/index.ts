/**
 * IntegraOps sender for Node.js / TypeScript source systems. No dependencies (Node 20+ fetch).
 *
 *   const sender = new IntegraOpsSender({ baseUrl: "https://api.example.com", apiKey: "io_v1_..." });
 *   await sender.send({ flow: "erp-order-invoice", transactionId: "SO-1001", step: "invoice-created", occurredAt: new Date() });
 *
 * Durable delivery belongs to the caller: persist the event with the business record first
 * (outbox), then send from a background job or run the IntegraOps agent against that outbox.
 */
import { createHash } from "node:crypto";

export const VERSION = "0.1.0";
const NAMESPACE = "6f4a3c2e-8b71-4d5e-9a0f-2c1d3e4f5a6b";

export type EventStatus = "completed" | "failed" | "skipped" | "cancelled";
export interface EventInput {
  flow: string; transactionId: string; step: string; occurredAt: Date | string;
  status?: EventStatus; error?: string | null; group?: string | null; eventId?: string;
}
export interface Receipt { eventId: string; duplicate: boolean; acceptedAt: string }

export class PermanentRejection extends Error {
  constructor(public status: number, public title: string) { super(`HTTP ${status}: ${title}`); }
}
export class TemporaryFailure extends Error {}

/** RFC 4122 version 5 UUID of `name` in the IntegraOps namespace (matches the Python SDK and the server mapper). */
export function deriveEventId(name: string): string {
  const ns = Buffer.from(NAMESPACE.replace(/-/g, ""), "hex");
  const hash = createHash("sha1").update(Buffer.concat([ns, Buffer.from(name, "utf8")])).digest();
  const b = Buffer.from(hash.subarray(0, 16));
  b[6] = (b[6] & 0x0f) | 0x50; b[8] = (b[8] & 0x3f) | 0x80;
  const h = b.toString("hex");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

export function toPayload(e: EventInput) {
  const occurredAt = typeof e.occurredAt === "string" ? e.occurredAt : e.occurredAt.toISOString();
  if (!/(Z|[+-]\d{2}:\d{2})$/.test(occurredAt)) throw new Error("occurredAt must carry a time zone");
  const status = e.status ?? "completed";
  const eventId = e.eventId ?? deriveEventId([e.flow, e.transactionId, e.step, status, occurredAt].join("\n"));
  return { eventId, flow: e.flow, transactionId: e.transactionId, step: e.step, status, occurredAt, error: e.error ?? null, group: e.group ?? null };
}

export interface SenderOptions { baseUrl: string; apiKey: string; timeoutMs?: number; maxAttempts?: number; retryDelayMs?: number; clientName?: string; fetch?: typeof fetch }

export class IntegraOpsSender {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  constructor(private readonly options: SenderOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, "");
    if (!/^https:\/\//.test(this.baseUrl) && !/^http:\/\/(localhost|127\.0\.0\.1)/.test(this.baseUrl)) throw new Error("baseUrl must use https (http only for localhost)");
    if (options.apiKey.length < 32 || options.apiKey.length > 128) throw new Error("apiKey length looks wrong");
    this.fetchImpl = options.fetch ?? fetch;
  }

  private async request(method: string, path: string, body: unknown): Promise<{ status: number; json: any }> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.options.timeoutMs ?? 15000);
    try {
      const res = await this.fetchImpl(this.baseUrl + path, {
        method, body: body === undefined ? undefined : JSON.stringify(body), redirect: "manual", signal: controller.signal,
        headers: { "Content-Type": "application/json", "X-API-Key": this.options.apiKey, "User-Agent": `${this.options.clientName ?? "integraops-sdk-node"}/${VERSION}` },
      });
      const text = await res.text();
      return { status: res.status, json: text ? JSON.parse(text) : null };
    } finally { clearTimeout(timer); }
  }

  /** Delivers one event, retrying transient failures. Throws PermanentRejection or TemporaryFailure. */
  async send(event: EventInput): Promise<Receipt> {
    const payload = toPayload(event);
    const attempts = this.options.maxAttempts ?? 5;
    let delay = this.options.retryDelayMs ?? 1000, last = "";
    for (let attempt = 1; attempt <= attempts; attempt++) {
      try {
        const { status, json } = await this.request("POST", "/api/v1/events", payload);
        if (status === 200 || status === 202) {
          if (String(json?.eventId ?? "").toLowerCase() !== payload.eventId.toLowerCase()) throw new TemporaryFailure("receipt eventId mismatch");
          return { eventId: json.eventId, duplicate: Boolean(json.duplicate), acceptedAt: json.acceptedAt ?? "" };
        }
        if (status === 408 || status === 425 || status === 429 || status >= 500) last = `HTTP ${status}`;
        else throw new PermanentRejection(status, json?.title ?? "reddedildi");
      } catch (e) {
        if (e instanceof PermanentRejection) throw e;
        last = e instanceof Error ? e.message : String(e);
      }
      if (attempt < attempts) { await new Promise(r => setTimeout(r, delay)); delay = Math.min(delay * 2, 30000); }
    }
    throw new TemporaryFailure(`${attempts} denemeden sonra teslim edilemedi: ${last}`);
  }

  /** Verifies the key and records this client's version on its connection. */
  async ping(): Promise<{ ok: boolean; serverTime: string }> { return (await this.request("POST", "/api/v1/connections/ping", {})).json; }

  /** Posts a raw payload to a mapped connection; IntegraOps applies the connection's template. */
  async ingest(connectionId: string, payload: unknown): Promise<{ accepted: number; duplicates: number; rejected: number; results: unknown[] }> {
    const { status, json } = await this.request("POST", `/api/v1/connections/${connectionId}/ingest`, payload);
    if (status >= 400) throw new PermanentRejection(status, json?.title ?? "reddedildi");
    return json;
  }
}
