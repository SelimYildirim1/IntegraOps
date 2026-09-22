import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { IntegraOpsSender, PermanentRejection, TemporaryFailure, deriveEventId, toPayload } from "../src/index.js";

const KEY = "k".repeat(40);
let script: Array<[number, unknown]> = [];
const seen: Array<{ path: string; ua: string; body: any }> = [];
const server = createServer((req, res) => {
  let raw = "";
  req.on("data", c => raw += c);
  req.on("end", () => {
    const body = raw ? JSON.parse(raw) : {};
    seen.push({ path: req.url ?? "", ua: String(req.headers["user-agent"]), body });
    const [status, reply] = script.shift() ?? [202, { eventId: body.eventId, duplicate: false, acceptedAt: "now" }];
    res.writeHead(status, { "Content-Type": "application/json" });
    res.end(JSON.stringify(reply));
  });
});
await new Promise<void>(r => server.listen(0, "127.0.0.1", r));
const port = (server.address() as any).port;
const sender = new IntegraOpsSender({ baseUrl: `http://127.0.0.1:${port}`, apiKey: KEY, maxAttempts: 3, retryDelayMs: 5 });
const event = { flow: "f", transactionId: "T-1", step: "s", occurredAt: "2026-09-22T10:00:00+00:00" };

test("deterministic ids match the Python SDK and the server", () => {
  assert.equal(toPayload(event).eventId, toPayload(event).eventId);
  assert.equal(deriveEventId("x"), "144d2318-4031-5d31-a0e0-712528b987b7"); // uuid5 of "x" in the shared namespace
});

test("success carries the user agent", async () => {
  script = []; seen.length = 0;
  const receipt = await sender.send(event);
  assert.equal(receipt.duplicate, false);
  assert.ok(seen[0].ua.startsWith("integraops-sdk-node/"));
});

test("transient failure is retried", async () => {
  script = [[503, { title: "down" }]]; seen.length = 0;
  await sender.send(event);
  assert.equal(seen.length, 2);
});

test("permanent rejection is not retried", async () => {
  script = [[400, { title: "step yok" }]]; seen.length = 0;
  await assert.rejects(sender.send(event), (e: unknown) => e instanceof PermanentRejection && e.status === 400);
  assert.equal(seen.length, 1);
});

test("receipt mismatch fails after retries", async () => {
  script = [[202, { eventId: "00000000-0000-4000-8000-000000000000" }], [202, { eventId: "00000000-0000-4000-8000-000000000000" }], [202, { eventId: "00000000-0000-4000-8000-000000000000" }]];
  await assert.rejects(sender.send(event), TemporaryFailure);
});

test("insecure remote url is rejected", () => {
  assert.throws(() => new IntegraOpsSender({ baseUrl: "http://api.example.com", apiKey: KEY }));
  server.close();
});
