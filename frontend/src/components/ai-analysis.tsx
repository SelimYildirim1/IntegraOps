import ActionForm from "@/components/action-form";
import { Time } from "@/components/ui";
import { adminAction } from "@/app/admin-actions";
import type { AiAnalysis, AiEvidence, AiRating, AiStatus } from "@/lib/pilot-types";
import { aiCauseNames, aiRatingNames } from "@/lib/pilot-types";

const evidenceNames: Record<string, string> = {
  alarm: "Alarm", transaction: "İşlem", step: "Adım", event: "Olay", delivery: "Bildirim", note: "Not", similar_alarms: "Benzer alarmlar",
};

// Kanıtın kısa, okunabilir etiketi: model yalnızca bu kayıtlara atıf yapabilir.
function evidenceLabel(e: AiEvidence) {
  const detail = e.type === "step" || e.type === "event" ? String(e.code ?? e.step ?? "")
    : e.type === "delivery" ? `${e.channel_kind} · ${e.state}`
    : e.type === "similar_alarms" ? `${e.count} alarm / ${e.window_hours} saat` : "";
  return `${evidenceNames[e.type] ?? e.type}${detail ? `: ${detail}` : ""}`;
}

export default function AiAnalysisPanel({ alarmId, status, analyses, canAct }: {
  alarmId: string; status: AiStatus | null; analyses: AiAnalysis[]; canAct: boolean;
}) {
  const latest = analyses.find(a => a.status === "completed");
  const byId = new Map((latest?.evidence ?? []).map(e => [e.id, e]));
  return <section className="surface">
    <div className="surface-head">
      <div><h2>Yapay zekâ ile inceleme</h2>
        <p>Alarm, işlem geçmişi, bildirimler ve benzer alarmlardan kanıta dayalı açıklama. Sonuç öneridir; kararı operatör verir.</p></div>
      {status?.enabled && canAct && <ActionForm action={adminAction} label={latest ? "Yeniden incele" : "Yapay zekâ ile incele"} compact tone="secondary" busy="İnceleniyor…">
        <input type="hidden" name="op" value="ai-analyze" /><input type="hidden" name="id" value={alarmId} /></ActionForm>}
    </div>
    <div className="surface-body">
      {status && !status.planEnabled && <p className="muted">Yapay zekâ asistanı bu organizasyonun planında yok. Alarm takibi, bildirimler ve raporlar bundan etkilenmez; asistan plana eklendiğinde bu alanda açıklama ve inceleme önerileri görünür.</p>}
      {(!status || (status.planEnabled && !status.configured)) && <p className="muted">Yapay zekâ henüz yapılandırılmadı. Model seçildiğinde bu alanda alarm açıklaması ve inceleme önerileri görünecek.</p>}
      {status?.enabled && !latest && <p className="muted">Bu alarm için henüz inceleme yok.{canAct ? "" : " İnceleme başlatmak için operatör yetkisi gerekir."}</p>}
      {latest?.result && <>
        <p className="callout">{latest.result.summary}</p>
        <dl className="facts">
          <div><dt>Olası neden sınıfı</dt><dd><span className="badge info">{aiCauseNames[latest.result.cause] ?? latest.result.cause}</span></dd></div>
          <div><dt>Hazırlanma</dt><dd className="time"><Time value={latest.created_at} />{latest.requested_by && `, ${latest.requested_by}`}</dd></div>
          <div><dt>Model</dt><dd className="mono">{latest.model}</dd></div>
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
      {analyses.some(a => a.status === "failed") && !latest && <p className="callout bad">{analyses[0].error ?? "Son inceleme tamamlanamadı."}</p>}
    </div>
  </section>;
}
