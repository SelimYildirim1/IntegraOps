"use client";

import { useRef, useState, useTransition } from "react";
import Link from "next/link";
import { sendTestEvent } from "@/app/flows/test-actions";
import type { FlowDetails, TestEventPayload } from "@/lib/flow-details";

// Akış tanımını gerçek bildirimlerle dener. TEST- kimlikli işlemler oluşturur.
export default function FlowTest({ flow }: { flow: FlowDetails }) {
  // Optional steps are never expected, so the trial reports only the steps that complete the flow.
  const steps = [...flow.steps].sort((a, b) => a.position - b.position).filter(s => s.kind !== "optional" && s.kind !== "conditional");
  const [run, setRun] = useState<{ id: string; accepted: number } | null>(null);
  const [attempt, setAttempt] = useState<TestEventPayload | null>(null);
  const [lastPayload, setLastPayload] = useState<TestEventPayload | null>(null);
  const [message, setMessage] = useState<{ text: string; ok: boolean } | null>(null);
  const [retry, setRetry] = useState(false);
  const [pending, startTransition] = useTransition();
  const inFlight = useRef(false);

  function send(payload: TestEventPayload) {
    if (inFlight.current) return;
    inFlight.current = true;
    setAttempt(payload); setLastPayload(payload); setMessage(null); setRetry(false);
    startTransition(async () => {
      try {
        const result = await sendTestEvent(payload);
        if (result.ok) {
          setRun(current => current?.id === payload.transactionId ? { ...current, accepted: current.accepted + 1 } : current);
          setAttempt(null);
          setMessage({ ok: true, text: result.duplicate ? "Bu bildirim daha önce kabul edilmişti; yeni kayıt oluşmadı." : "Bildirim kabul edildi. Worker işledikten sonra işlem detayına yansır." });
        } else { setMessage({ ok: false, text: result.message }); setRetry(result.retry); }
      } catch {
        setMessage({ ok: false, text: "Panele ulaşılamadı. Aynı bildirimi tekrar gönderebilirsiniz." }); setRetry(true);
      } finally { inFlight.current = false; }
    });
  }

  function sendStep(transactionId: string, index: number) {
    send({ eventId: crypto.randomUUID(), flow: flow.code, transactionId,
      step: steps[index].code, occurredAt: new Date().toISOString() });
  }

  return <section className="surface">
    <div className="surface-head"><div><h2>Akışı deneyin</h2><p>Her düğme bir adımın tamamlanma bildirimini gönderir. Oluşan TEST- kimlikli işlemler gerçek kayıtlardır ve sayılara dahil edilir.</p></div></div>
    <div className="surface-body">
      <div className="form-actions" style={{ marginTop: 0 }}>
        <button className={`button ${run ? "secondary" : ""}`} type="button" disabled={pending || steps.length === 0} onClick={() => {
          if (inFlight.current) return;
          const id = `TEST-${crypto.randomUUID()}`;
          setRun({ id, accepted: 0 }); sendStep(id, 0);
        }}>{run ? "Yeni deneme başlat" : "Denemeyi başlat"}</button>
        {run && !attempt && run.accepted < steps.length && <button className="button" type="button" disabled={pending}
          onClick={() => sendStep(run.id, run.accepted)}>Bildir: {steps[run.accepted].name}</button>}
        {attempt && retry && <button className="button" type="button" disabled={pending}
          onClick={() => send(attempt)}>Aynı bildirimi tekrar gönder</button>}
      </div>
      <p className={`form-status ${message ? (message.ok ? "ok" : "err") : ""}`} role="status" aria-live="polite"
        style={message || pending ? undefined : { display: "none" }}>{pending ? "Bildirim gönderiliyor…" : message?.text}</p>
      {run && <div className="test-progress">
        <p><strong>{run.accepted} / {steps.length}</strong> bildirim kabul edildi.</p>
        <p className="mono">{run.id}</p>
        <Link href={`/transactions/${encodeURIComponent(flow.code)}/${encodeURIComponent(run.id)}`}
          target="_blank" rel="noopener noreferrer" prefetch={false}>İşlem detayını yeni sekmede aç</Link>
        {run.accepted === steps.length && <p>Bütün adımlar bildirildi. İşlenme sonucunu işlem detayında izleyebilirsiniz.</p>}
      </div>}
      {lastPayload && <details className="event-example"><summary>Son bildirimin JSON içeriği</summary>
        <pre>{JSON.stringify({ ...lastPayload, status: "completed" }, null, 2)}</pre>
      </details>}
    </div>
  </section>;
}
