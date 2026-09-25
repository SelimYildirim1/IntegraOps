import ActionForm from "@/components/action-form";
import { Time } from "@/components/ui";
import { adminAction } from "@/app/admin-actions";
import type { AiAnalysis, AiEvidence, AiRating, AiStatus, AiSubject, AlarmCause, AlarmContext, AlarmMemory } from "@/lib/pilot-types";
import { aiCauseNames, aiRatingNames, alarmCauseNames } from "@/lib/pilot-types";

const evidenceNames: Record<string, string> = {
  alarm: "Alarm", transaction: "İşlem", step: "Adım", event: "Olay", delivery: "Bildirim", note: "Not", similar_alarms: "Benzer alarmlar",
  memory: "Geçmiş özeti", history: "Geçmiş alarm", connection: "Bağlantı", agent: "Ajan",
};

/** The card is worth showing only when the assistant can add something the alarm page does not already say. */
export type AiSignalCounts = { memory_total: number; similar_recent: number; silent_sources: number; dead_deliveries: number };
export function aiSignalsFrom(s: AiSignalCounts | null) {
  return s ? aiSignals({ total: s.memory_total } as AlarmMemory, s) : undefined;
}
export function aiSignals(memory: AlarmMemory | undefined, context: AlarmContext | undefined) {
  const list: string[] = [];
  if (memory && Number(memory.total) > 0) list.push(`${memory.total} geçmiş vaka`);
  if (context && Number(context.similar_recent) > 0) list.push(`${context.similar_recent} eş zamanlı benzer alarm`);
  if (context && Number(context.silent_sources) > 0) list.push("sessiz kaynak");
  if (context && Number(context.dead_deliveries) > 0) list.push("ulaşmayan bildirim");
  return list;
}

// Kanıtın kısa, okunabilir etiketi: model yalnızca bu kayıtlara atıf yapabilir.
function evidenceLabel(e: AiEvidence) {
  const detail = e.type === "step" || e.type === "event" ? String(e.code ?? e.step ?? "")
    : e.type === "alarm" ? String(e.step ?? "")
    : e.type === "delivery" ? `${e.channel_kind} · ${e.state}`
    : e.type === "similar_alarms" ? `${e.count} alarm / ${e.window_hours} saat`
    : e.type === "history" ? `${e.transaction_id ?? ""}${e.cause ? " · " + (alarmCauseNames[e.cause as AlarmCause] ?? e.cause) : ""}`
    : e.type === "memory" ? `${e.total} vaka / ${e.window_days} gün`
    : e.type === "connection" || e.type === "agent" ? `${e.name}${e.silent_now ? " · sessiz" : ""}` : "";
  return `${evidenceNames[e.type] ?? e.type}${detail ? `: ${detail}` : ""}`;
}

// İstemci yalnızca konuyu seçer; model, adres, istem ve kanıtlar sunucuda belirlenir.
function subjectFields(subject: AiSubject) {
  return subject.kind === "alarm"
    ? <><input type="hidden" name="op" value="ai-analyze" /><input type="hidden" name="id" value={subject.id} /></>
    : <><input type="hidden" name="op" value="ai-analyze-transaction" /><input type="hidden" name="flow" value={subject.flow} /><input type="hidden" name="transactionId" value={subject.transactionId} /></>;
}

export default function AiAnalysisPanel({ subject, status, analyses, canAct, isTest, signals, currentCause }: {
  subject: AiSubject; status: AiStatus | null; analyses: AiAnalysis[]; canAct: boolean; isTest?: boolean;
  signals?: string[]; currentCause?: AlarmCause | null;
}) {
  const latest = analyses.find(a => a.status === "completed");
  // Nothing beyond the alarm text to add: say so in one line instead of offering a model call that would restate it.
  if (signals && signals.length === 0 && !latest && status?.enabled) return <section className="surface">
    <div className="surface-body"><p className="muted"><strong>Yapay zekâ incelemesi önerilmiyor.</strong> Bu {subject.kind === "alarm" ? "alarm" : "işlem"} için geçmiş vaka, eş zamanlı benzer alarm, sessiz kaynak ya da ulaşmayan bildirim yok; model {subject.kind === "alarm" ? "alarm metnini" : "zaman çizelgesini"} yinelemekten fazlasını söyleyemezdi.</p></div>
  </section>;
  const pending = analyses.find(a => a.status === "pending");
  const byId = new Map((latest?.evidence ?? []).map(e => [e.id, e]));
  const blockedByTestMode = status?.testMode && isTest === false;
  const quotaLeft = status ? Math.max(0, status.dailyLimit - status.usedToday) : 0;
  const waitUntil = status?.nextAllowedAt && Date.parse(status.nextAllowedAt) > Date.now() ? status.nextAllowedAt : null;
  const canStart = Boolean(status?.enabled && canAct && !blockedByTestMode && quotaLeft > 0 && !pending);
  const lede = subject.kind === "alarm"
    ? signals && signals.length > 0 ? `Ek bağlam var: ${signals.join(", ")}. Geçmiş çözümler, eş zamanlı alarmlar ve kaynak sağlığı birlikte değerlendirilir.`
      : "Alarm, işlem geçmişi, bildirimler ve benzer alarmlardan kanıta dayalı açıklama."
    : signals && signals.length > 0 ? `Ek bağlam var: ${signals.join(", ")}. Geçmiş çözümler, eş zamanlı alarmlar ve kaynak sağlığı birlikte değerlendirilir.`
      : "İşlemin adımları, gelen bildirimler ve açık alarmlardan kanıta dayalı açıklama.";
  const likely = latest?.result?.likelyCause && latest.result.likelyCause !== "unknown" ? latest.result.likelyCause : null;
  return <section className="surface">
    <div className="surface-head">
      <div><h2>Yapay zekâ ile inceleme</h2>
        <p>{lede} Süreç durumunu çekirdek sistem belirler; bu alan yalnızca kanıtları açıklar ve öneri verir.</p></div>
      {canStart && <ActionForm action={adminAction} label={latest ? "Yeniden incele" : "Yapay zekâ ile incele"} compact tone="secondary" busy="İnceleniyor…">
        {subjectFields(subject)}</ActionForm>}
    </div>
    <div className="surface-body">
      {status && !status.planEnabled && <p className="muted">Yapay zekâ asistanı bu organizasyonun planında yok. Alarm takibi, bildirimler ve raporlar bundan etkilenmez; asistan plana eklendiğinde bu alanda açıklama ve inceleme önerileri görünür.</p>}
      {(!status || (status.planEnabled && !status.configured)) && <p className="muted">
        Yapay zekâ şu anda kullanılamıyor. {status?.reason ?? "Model yapılandırıldığında bu alanda açıklama ve inceleme önerileri görünecek."}
      </p>}
      {status?.enabled && <p className="help">Kalan inceleme hakkı: {quotaLeft}/{status.dailyLimit} (24 saat).
        {status.testMode && <> Deneme profili: yalnızca <span className="mono">TEST-</span> ile başlayan sentetik işlemler incelenir.</>}</p>}
      {status?.enabled && blockedByTestMode && <p className="muted">Bu işlem gerçek veri olarak işaretli; deneme profili yalnızca <span className="mono">TEST-</span> ile başlayan sentetik işlemleri sağlayıcıya gönderir.</p>}
      {status?.enabled && quotaLeft === 0 && <p className="muted">Bu proje için son 24 saatteki çağrı sınırı doldu. Ücretli katmana geçilmez, otomatik tekrar yapılmaz; sınır kendiliğinden açılır.</p>}
      {status?.enabled && waitUntil && quotaLeft > 0 && <p className="muted">Bir sonraki çağrı için bekleme süresi var: <Time value={waitUntil} /> sonrasında tekrar deneyin.</p>}
      {pending && <p className="muted">Bir inceleme sürüyor{pending.requested_by ? ` (${pending.requested_by})` : ""}; birkaç saniye sonra sayfayı yenileyin.</p>}
      {status?.enabled && !latest && !pending && !blockedByTestMode && <p className="muted">Bu {subject.kind === "alarm" ? "alarm" : "işlem"} için henüz inceleme yok.{canAct ? "" : " İnceleme başlatmak için operatör yetkisi gerekir."}</p>}
      {latest?.result && <>
        {latest.stale && <p className="callout warn">Bu açıklama daha eski bir veri anına ait: sonrasında yeni bildirim veya durum değişikliği geldi. Güncel durum için yeniden inceleyin.</p>}
        <p className="callout">{latest.result.summary}</p>
        {likely && <div className="callout good assign-row">
          <span><strong>Geçmişe göre olası neden:</strong> {alarmCauseNames[likely]}{" "}
            {(latest.result.likelyCauseEvidence ?? []).map(id => <span key={id} className="badge" title={byId.get(id) ? JSON.stringify(byId.get(id)) : id}>{byId.get(id) ? evidenceLabel(byId.get(id)!) : id}</span>)}</span>
          {subject.kind === "alarm" && canAct && currentCause !== likely && <ActionForm action={adminAction} label="Bu nedeni alarma kaydet" compact tone="secondary">
            <input type="hidden" name="op" value="alarm-cause" /><input type="hidden" name="id" value={subject.id} /><input type="hidden" name="cause" value={likely} />
          </ActionForm>}
          {currentCause === likely && <span className="help">Alarmda kayıtlı nedenle aynı.</span>}
        </div>}
        <dl className="facts">
          <div><dt>Bulgu sınıfı</dt><dd><span className="badge info">{aiCauseNames[latest.result.cause] ?? latest.result.cause}</span></dd></div>
          <div><dt>Hazırlanma</dt><dd className="time"><Time value={latest.created_at} />{latest.requested_by && `, ${latest.requested_by}`}</dd></div>
        </dl>
        {latest.result.findings.length > 0 && <><h3>Bulgular</h3><ul className="ai-list">
          {latest.result.findings.map((f, i) => <li key={i}>{f.text}{" "}
            {f.grounded ? f.evidence.map(id => <span key={id} className="badge" title={byId.get(id) ? JSON.stringify(byId.get(id)) : id}>
              {byId.get(id) ? evidenceLabel(byId.get(id)!) : id}</span>)
              : <span className="badge warn">Kanıta bağlanmadı</span>}</li>)}
        </ul></>}
        {latest.result.checks.length > 0 && <><h3>Kontrol edilecekler</h3><ol className="ai-list">
          {latest.result.checks.map((c, i) => <li key={i}>{c}</li>)}</ol></>}
        {latest.result.unknowns.length > 0 && <><h3>Mevcut verilerle bilinmeyenler</h3><ul className="ai-list muted">
          {latest.result.unknowns.map((u, i) => <li key={i}>{u}</li>)}</ul></>}
        <div className="assign-row">
          <span className="help">Bu inceleme işinize yaradı mı?{latest.my_rating && ` Değerlendirmeniz: ${aiRatingNames[latest.my_rating]}.`}</span>
          {(Object.keys(aiRatingNames) as AiRating[]).map(r => <ActionForm key={r} action={adminAction} label={aiRatingNames[r]} compact
            tone={latest.my_rating === r ? "secondary" : "quiet"}>
            <input type="hidden" name="op" value="ai-feedback" /><input type="hidden" name="id" value={latest.id} /><input type="hidden" name="rating" value={r} />
          </ActionForm>)}
        </div>
      </>}
      {analyses.some(a => a.status === "failed") && !latest && !pending && <p className="callout bad">{analyses.find(a => a.status === "failed")?.error ?? "Son inceleme tamamlanamadı."} Süreç, alarmlar ve bildirimler bundan etkilenmez.</p>}
    </div>
  </section>;
}
