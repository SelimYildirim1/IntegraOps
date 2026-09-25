import ActionForm from "@/components/action-form";
import { Explain, Time } from "@/components/ui";
import Icon from "@/components/icons";
import { adminAction } from "@/app/admin-actions";
import type { AiRating, AiStatus, AiThresholdAnalysis, ThresholdAdvice } from "@/lib/pilot-types";
import { aiRatingNames, thresholdRecommendationNames } from "@/lib/pilot-types";
import { duration } from "@/components/alarm-memory";

const tone: Record<string, string> = { raise: "warn", lower: "info", calendar: "warn", keep: "good", wait: "" };

/** Allowance review per step: the numbers and the suggestion come from the API; the model only orders and explains them. */
export default function ThresholdAdvicePanel({ advice, status, analyses, canAct, days, test }: {
  advice: ThresholdAdvice; status: AiStatus | null; analyses: AiThresholdAnalysis[]; canAct: boolean; days: number; test: string;
}) {
  const actionable = advice.steps.filter(s => (s.recommendation === "raise" || s.recommendation === "lower") && s.suggestedSeconds);
  const calendar = advice.steps.filter(s => s.recommendation === "calendar");
  const waiting = advice.steps.filter(s => s.recommendation === "wait");
  const latest = analyses.find(a => a.status === "completed");
  const pending = analyses.find(a => a.status === "pending");
  const reasonByStep = new Map<string, { recommendation: string; reason: string; grounded: boolean }>();
  const stepByEvidence = new Map((latest?.evidence ?? []).filter(e => e.type === "step_stats").map(e => [e.id, String(e.step)]));
  for (const s of latest?.result?.steps ?? []) for (const id of s.evidence) { const code = stepByEvidence.get(id); if (code && !reasonByStep.has(code)) reasonByStep.set(code, s); }
  const hasSignal = advice.steps.some(s => s.recommendation !== "wait" && s.recommendation !== "keep");
  const quotaLeft = status ? Math.max(0, status.dailyLimit - status.usedToday) : 0;
  const blockedByTestMode = Boolean(status?.testMode && test !== "only");
  const canNarrate = Boolean(status?.enabled && canAct && hasSignal && quotaLeft > 0 && !pending && !blockedByTestMode);
  const totalAvoided = actionable.filter(s => s.recommendation === "raise").reduce((a, s) => a + s.impact, 0);

  return <section className="surface">
    <div className="surface-head">
      <div><h2><Icon name="sliders" size={15} /> Süre sınırı önerileri</h2>
        <p>Son {days} günde gerçekleşen adım süreleri ile sınırlar karşılaştırıldı. Sahte alarm üreten sınırlar ve gerçek gecikmeyi geç yakalayan sınırlar veriyle gösterilir.</p></div>
      <div className="assign-row">
        {canAct && actionable.length > 0 && <ActionForm action={adminAction} label={`Öneriyi taslağa uygula (${actionable.length} adım)`} compact busy="Uygulanıyor…">
          <input type="hidden" name="op" value="threshold-apply" /><input type="hidden" name="flow" value={advice.flow} />
          <input type="hidden" name="steps" value={JSON.stringify(Object.fromEntries(actionable.map(s => [s.code, s.suggestedSeconds])))} /></ActionForm>}
        {canNarrate && <ActionForm action={adminAction} label={latest ? "Yeniden anlat" : "Yapay zekâ ile anlat"} compact tone="secondary" busy="Hazırlanıyor…">
          <input type="hidden" name="op" value="ai-threshold" /><input type="hidden" name="flow" value={advice.flow} />
          <input type="hidden" name="days" value={String(days)} /><input type="hidden" name="test" value={test} /></ActionForm>}
      </div>
    </div>
    <div className="surface-body">
      {advice.steps.length === 0 && <p className="muted">Yayında bir sürüm yok ya da süresi ölçülen adım yok.</p>}
      {advice.steps.length > 0 && !hasSignal && waiting.length === advice.steps.length && <p className="muted">Veri birikiyor: öneri için her adımda en az 20 tamamlanma gerekir.</p>}
      {advice.steps.length > 0 && !hasSignal && waiting.length < advice.steps.length && <p className="callout good">Sınırlar gerçekleşen sürelerle uyumlu; değişiklik önerilmiyor.</p>}
      {totalAvoided > 0 && <p className="callout warn"><strong>Son {days} günde {totalAvoided} gecikme alarmı önlenebilirdi.</strong> Öneri taslağa uygulanır; yayınlama size kalır.</p>}
      {calendar.length > 0 && <p className="callout warn"><strong>Çalışma takvimi eksik:</strong> {calendar.map(s => s.name).join(", ")} adımlarındaki aşımlar mesai dışında yoğunlaşıyor. Akışa takvim bağlamak bu alarmların çoğunu kaldırır (Ayarlar → Takvimler).</p>}
      {latest?.result && <>
        {latest.stale && <p className="callout warn">Bu anlatım daha eski verilere ait; sayılar o zamandan beri değişti.</p>}
        <p className="callout">{latest.result.summary}</p>
        <p className="help"><Time value={latest.created_at} />{latest.requested_by && `, ${latest.requested_by}`}</p>
      </>}
      {pending && <p className="muted">Bir anlatım hazırlanıyor; birkaç saniye sonra sayfayı yenileyin.</p>}
      {advice.steps.length > 0 && <div className="table-wrap"><table><thead><tr>
        <th>Adım</th><th className="right">Tamamlanma</th><th>Sınır</th><th>p50 / p95</th><th className="right">Aşım</th><th className="right">Gecikme alarmı</th><th>Öneri</th></tr></thead><tbody>
        {advice.steps.map(s => { const m = reasonByStep.get(s.code); return <tr key={s.code}>
          <td><strong>{s.name}</strong><br /><span className="mono" style={{ fontSize: 12 }}>{s.code}</span></td>
          <td className="right num">{s.n}</td>
          <td className="nowrap">{s.allowanceSeconds === null ? "—" : duration(s.allowanceSeconds)}</td>
          <td className="nowrap">{s.n === 0 ? "—" : `${duration(s.p50Seconds)} / ${duration(s.p95Seconds)}`}</td>
          <td className="right num">{s.n === 0 ? "—" : `%${Math.round(s.breachRate * 100)}`}{s.offHoursShare > 0.6 && s.breached > 0 && <small> (%{Math.round(s.offHoursShare * 100)} mesai dışı)</small>}</td>
          <td className="right num">{s.overdueAlarms}{s.overdueAlarms > 0 && <small> ({s.overdueResolvedByCompletion} sonradan tamamlandı)</small>}</td>
          <td><span className={`badge ${tone[s.recommendation] ?? ""}`}>{thresholdRecommendationNames[s.recommendation]}{s.suggestedSeconds ? ` → ${duration(s.suggestedSeconds)}` : ""}</span>
            <br /><small>{m?.grounded ? m.reason : s.reason}</small></td>
        </tr>; })}
      </tbody></table></div>}
      {latest?.result && <div className="assign-row" style={{ marginTop: 12 }}>
        <span className="help">Bu anlatım işinize yaradı mı?{latest.my_rating && ` Değerlendirmeniz: ${aiRatingNames[latest.my_rating]}.`}</span>
        {(Object.keys(aiRatingNames) as AiRating[]).map(r => <ActionForm key={r} action={adminAction} label={aiRatingNames[r]} compact tone={latest.my_rating === r ? "secondary" : "quiet"}>
          <input type="hidden" name="op" value="ai-feedback" /><input type="hidden" name="id" value={latest.id} /><input type="hidden" name="rating" value={r} /></ActionForm>)}
      </div>}
      <Explain title="Öneri nasıl hesaplanır?">
        <p><strong>Yükselt:</strong> tamamlanmaların en az %20'si sınırı aştıktan sonra bitiyor ve p95 sınırın üç katını geçmiyorsa sınır, p95'in 1,2 katına en yakın "yuvarlak" değere çekilir; etki, o sınırla açılmayacak alarm sayısıdır.</p>
        <p><strong>Düşür:</strong> aşım %5'in altında ve p95 sınırın üçte birinden kısaysa sınır daraltılır; etki, daha erken yakalanacak yavaş işlem sayısıdır. <strong>Takvim:</strong> aşımların %60'tan fazlası mesai dışındaysa (takvim bağlı değilken) süre yerine çalışma takvimi önerilir.</p>
        <p>En az 20 tamamlanma olmadan öneri verilmez. Uygulama taslağa yazar; yayınlama ve etkinleşme zamanı her zaman sizdedir.</p>
      </Explain>
    </div>
  </section>;
}
