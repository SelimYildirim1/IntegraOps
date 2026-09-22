import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getTransaction } from "@/lib/api";
import type { DelayInfo, TimelineStep, TransactionDetail } from "@/lib/types";
import { stepKindLabels } from "@/lib/types";
import { Badge, DeadlineMeter, Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";
import { elapsedBetween, spanText, stepTiming } from "@/lib/time";

export const dynamic = "force-dynamic";

export default async function Detail({ params }: {
  params: Promise<{ flow: string; transactionId: string }>;
}) {
  const { flow, transactionId } = await params;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow) || !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(transactionId)) notFound();
  let item: TransactionDetail;
  try { item = await getTransaction(flow, transactionId); }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="İşlem yüklenemedi" description={apiMessage(error)} />;
  }
  const delay: DelayInfo = item.delay ?? {
    state: "pending_evaluation", step: null, startedAt: null, deadlineAt: null, checkedAt: null,
  };
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  const expected = steps.find(step => step.code === delay.step);
  const waiting = delay.state === "waiting" || delay.state === "overdue";
  const unprocessed = item.acceptedEventCount - item.processedEventCount;
  // Stages: steps sharing a stage run in parallel; the next stage starts when every expected step of the previous one is done.
  const stages = new Map<number, TimelineStep[]>();
  for (const s of steps) stages.set(s.stage ?? s.position, [...(stages.get(s.stage ?? s.position) ?? []), s]);
  const stageList = [...stages.entries()].sort((a, b) => a[0] - b[0]);
  const isExpectedStep = (s: TimelineStep) => s.kind !== "optional" && s.observation !== "not_applicable";
  const stageEnd = (index: number) => {
    for (let i = index - 1; i >= 0; i--) {
      const members = stageList[i][1].filter(isExpectedStep);
      if (members.length === 0) continue;
      if (members.some(m => m.observation !== "completed" && m.observation !== "skipped")) return null;
      return members.map(m => m.occurredAt ?? "").sort().at(-1) || null;
    }
    return null;
  };
  const parallel = stageList.some(([, members]) => members.length > 1);
  const cancelledStep = steps.find(s => s.observation === "cancelled");

  return <>
    <PageHead crumb={{ href: `/?${new URLSearchParams({ flow })}`, label: "İşlem listesi" }}
      title={<span className="mono">{item.transactionId}</span>}
      lede={<>{item.flow} akışı, {item.flowVersion}. sürüm{item.calendar ? `, ${item.calendar} takvimi` : ""}{item.group ? <>, grup <span className="mono">{item.group}</span></> : ""}. {number(item.acceptedEventCount)} bildirim kabul edildi, {number(item.processedEventCount)} işlendi.</>}>
      <Badge value={item.status} /><RefreshControls />
    </PageHead>

    {unprocessed > 0 && <p className="callout" style={{ marginBottom: 20 }}>
      <strong>{number(unprocessed)} bildirim işlenmeyi bekliyor.</strong> Son durum bu görünüme henüz yansımamış olabilir; birkaç saniye sonra yenileyin.
    </p>}
    {item.status === "cancelled" && <p className="callout" style={{ marginBottom: 20 }}>
      <strong>İşlem kaynak sistem tarafından iptal edildi{cancelledStep ? ` (${cancelledStep.name} adımında)` : ""}.</strong> Süre takibi durdu; açık alarmlar iptal nedeniyle kapatıldı.{cancelledStep?.error && <> Neden: {cancelledStep.error}</>}
    </p>}
    {delay.state === "overdue" && <p className="callout warn" style={{ marginBottom: 20 }}>
      <strong>{expected?.name ?? delay.step} adımı tanınan sürede bildirilmedi.</strong> Bu bir gecikmedir; kaynak sistem hata bildirmediği sürece işlem başarısız sayılmaz.
    </p>}

    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Adımlar</h2><p>{parallel ? "Aynı aşamadaki adımlar paralel ilerler; sonraki aşama hepsini bekler. Süreler önceki aşamanın bittiği andan başlar." : "Her adımın süresi bir önceki adımın gerçekleşme anından başlar."}</p></div><span className="count">{steps.length} adım{parallel ? `, ${stageList.length} aşama` : ""}</span></div>
        <div className="surface-body">
          <ol className="rail">{stageList.map(([stage, members], stageIndex) => {
            const start = stageEnd(stageIndex);
            return members.map((step, memberIndex) => {
              const isExpected = step.code === delay.step && waiting;
              const pendingBranch = waiting && step.observation === "not_observed" && isExpectedStep(step) && step.stage === expected?.stage && step.code !== delay.step;
              const dot = step.observation === "completed" ? "completed" : step.observation === "failed" ? "failed" : step.observation === "skipped" ? "skipped"
                : step.observation === "not_applicable" ? "muted" : isExpected ? `expected ${delay.state}` : pendingBranch ? "expected waiting" : "";
              const line = step.observation === "completed" || step.observation === "skipped" ? "done" : isExpected || pendingBranch ? "dashed" : "";
              const took = start && step.occurredAt ? elapsedBetween(start, step.occurredAt) : null;
              const timing = took !== null && step.maxDelaySeconds !== null && step.observation === "completed" ? stepTiming(took, step.maxDelaySeconds) : null;
              return <li key={step.code} className={members.length > 1 ? "parallel" : ""}>
                <div className="mark"><span className={`dot ${dot}`}>{step.observation === "completed" ? "✓" : step.observation === "failed" ? "!" : step.observation === "skipped" ? "–" : stage + 1}</span><span className={`line ${line}`} /></div>
                <div className="step">
                  <div className="step-head"><h3>{step.name}{members.length > 1 && <small className="muted"> · {stage + 1}. aşama, {memberIndex + 1}/{members.length}</small>}</h3><Badge value={step.observation} /></div>
                  <div className="step-meta"><span className="mono">{step.code}</span>
                    {step.kind && step.kind !== "required" && <span className="badge test">{stepKindLabels[step.kind]}</span>}
                    {step.occurredAt && <Time value={step.occurredAt} />}</div>
                  {step.maxDelaySeconds === null
                    ? <span className="gap">{step.kind === "optional" ? "İsteğe bağlı adım, süresi ölçülmez" : "Başlangıç adımı"}</span>
                    : timing
                      ? <span className={`gap ${timing.tone}`}>{timing.text}</span>
                      : isExpected || pendingBranch
                        ? <DeadlineMeter startedAt={delay.startedAt} deadlineAt={isExpected ? delay.deadlineAt : (delay.startedAt ? new Date(Date.parse(delay.startedAt) + step.maxDelaySeconds * 1000).toISOString() : null)} state={delay.state} />
                        : <span className="gap">{spanText(step.maxDelaySeconds)} tanınan süre</span>}
                  {step.error && <pre className="error-content" style={{ marginTop: 10 }}>{step.error}</pre>}
                </div>
              </li>;
            });
          })}</ol>
        </div>
      </section>

      <section className="surface">
        <div className="surface-head"><h2>Süre takibi</h2><Badge value={delay.state} delay /></div>
        <div className="surface-body">
          <dl className="facts">
            <div><dt>Beklenen adım</dt><dd>{expected?.name || delay.step || "—"}</dd></div>
            <div><dt>Bekleme başlangıcı</dt><dd className="time">{delay.startedAt ? <Time value={delay.startedAt} /> : "—"}</dd></div>
            <div><dt>Beklenen son zaman</dt><dd className="time">{delay.deadlineAt ? <Time value={delay.deadlineAt} /> : "—"}</dd></div>
            <div><dt>Son kontrol</dt><dd className="time">{delay.checkedAt ? <Time value={delay.checkedAt} /> : "—"}</dd></div>
          </dl>
          {waiting && <DeadlineMeter startedAt={delay.startedAt} deadlineAt={delay.deadlineAt} state={delay.state} />}
          <p className="help">Süre kontrolü, worker tarafından düzenli aralıklarla yapılır. Süre dolduğunda alarm açılır; eksik adım bildirilince ya da atlandığı bildirilince alarm kendiliğinden kapanır. Paralel dallarda en yakın son zaman gösterilir; her geciken dal ayrı alarm açar.</p>
        </div>
      </section>
    </div>

    <section className="surface">
      <div className="surface-head"><div><h2>Gelen bildirimler</h2><p>En yeni bildirim en üstte.</p></div>
        <span className="count">{number(item.events.length)} bildirim</span></div>
      <div className="table-wrap"><table><thead><tr><th>Adım</th><th>Bildirim kimliği</th><th>Sonuç</th><th>Gerçekleşme</th><th>Ulaşma</th><th>Açıklama</th></tr></thead>
        <tbody>{[...item.events].reverse().map(event => <tr key={event.eventId}>
          <td>{steps.find(s => s.code === event.step)?.name ?? event.step}<small className="mono">{event.step}</small></td>
          <td className="mono" style={{ fontSize: 12 }}>{event.eventId}</td>
          <td><Badge value={event.status} /></td>
          <td className="nowrap"><Time value={event.occurredAt} /></td><td className="nowrap"><Time value={event.receivedAt} /></td>
          <td>{event.error ? <span className="error-content" style={{ display: "inline-block", padding: "4px 8px" }}>{event.error}</span> : <span className="muted">—</span>}</td>
        </tr>)}</tbody></table></div>
      {item.events.length === 0 && <div className="empty"><h3>Henüz işlenmiş bildirim yok</h3><p>İlk bildirim işlendiğinde burada listelenir.</p></div>}
    </section>
    <p className="help"><Link href={`/flows/view/${encodeURIComponent(item.flow)}`} prefetch={false}>Bu akışın tanımını görüntüle</Link></p>
  </>;
}
