import Link from "next/link";
import { api, apiMessage, getAccess, getAssignable } from "@/lib/api";
import type { AiStatus, AiTriageAnalysis, Assignee, TriageGroup, TriageResult } from "@/lib/pilot-types";
import { aiRatingNames, alarmCauseNames, triagePriorityNames, triageSignalNames } from "@/lib/pilot-types";
import type { AiRating } from "@/lib/pilot-types";
import { Empty, Explain, Message, PageHead, Time, number } from "@/components/ui";
import Icon from "@/components/icons";
import ActionForm from "@/components/action-form";
import RefreshControls from "@/components/refresh-controls";
import { adminAction } from "@/app/admin-actions";
import { alarmKind } from "@/lib/alarms";
import { duration } from "@/components/alarm-memory";
import SourceError from "@/components/source-error";

export const dynamic = "force-dynamic";

type ModelGroup = NonNullable<AiTriageAnalysis["result"]>["groups"][number];

const priorityTone = (p: number) => p >= 70 ? "bad" : p >= 45 ? "warn" : "";
const modelPriorityTone: Record<string, string> = { critical: "bad", high: "warn", normal: "info", low: "" };

export default async function TriagePage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const test = ["exclude", "include", "only"].includes(String(params.test)) ? String(params.test) : "exclude";
  let triage: TriageResult; let canAct = false; let assignees: Assignee[] = []; let aiStatus: AiStatus | null = null; let analyses: AiTriageAnalysis[] = [];
  try {
    const access = await getAccess(); canAct = access.role !== "viewer";
    [triage, assignees, aiStatus, analyses] = await Promise.all([
      api<TriageResult>("/alarms/triage?" + new URLSearchParams({ test })), canAct ? getAssignable() : Promise.resolve([]),
      api<AiStatus>("/ai/status").catch(() => null), api<AiTriageAnalysis[]>("/ai/triage?" + new URLSearchParams({ test })).catch(() => []),
    ]);
  } catch (e) { return <Message title="Sorun grupları yüklenemedi" description={apiMessage(e)} href="/alarms" action="Alarmlara dön" icon="alert" />; }

  const latest = analyses.find(a => a.status === "completed");
  const pending = analyses.find(a => a.status === "pending");
  const evidenceKey = new Map((latest?.evidence ?? []).filter(e => e.type === "group").map(e => [e.id, String(e.key)]));
  const modelByKey = new Map<string, ModelGroup>();
  for (const g of latest?.result?.groups ?? []) for (const id of g.evidence) { const key = evidenceKey.get(id); if (key && !modelByKey.has(key)) modelByKey.set(key, g); }
  const silent = triage.sources.filter(s => s.silent);
  const top = triage.groups[0];
  const blockedByTestMode = Boolean(aiStatus?.testMode && test !== "only");
  const quotaLeft = aiStatus ? Math.max(0, aiStatus.dailyLimit - aiStatus.usedToday) : 0;
  const canEvaluate = Boolean(aiStatus?.enabled && canAct && !blockedByTestMode && quotaLeft > 0 && !pending && triage.groups.length > 0);

  return <>
    <PageHead crumb={{ href: "/alarms", label: "Alarmlar" }} icon="layers" title="Sorun grupları"
      lede="Açık alarmlar ortak nedene göre gruplandı ve öncelik sırasına dizildi. Tek tek alarm yerine ortak sorunu araştırın.">
      <form action="/alarms/triage" className="filters" style={{ margin: 0 }}>
        <div className="field"><label htmlFor="t-test">Test verisi</label>
          <select id="t-test" name="test" defaultValue={test}><option value="exclude">Test hariç</option><option value="include">Test dahil</option><option value="only">Yalnızca test</option></select></div>
        <button className="button secondary" type="submit"><Icon name="filter" />Uygula</button>
      </form>
      <RefreshControls />
    </PageHead>

    <div className="stat-grid">
      <div className="stat"><p>Açık alarm</p><strong>{number(triage.openAlarms)}</strong></div>
      <div className="stat"><p>Sorun grubu</p><strong>{number(triage.groups.length)}</strong><span className="hint">Aynı akış, adım, tür ve hata deseni</span></div>
      <div className={`stat ${silent.length ? "attention" : ""}`}><p>Sessiz kaynak</p><strong>{silent.length}</strong>
        <span className="hint">{silent.length ? silent.map(s => s.name).join(", ") : "Bağlantı ve ajanlar olay gönderiyor"}</span></div>
      <div className={`stat ${top && top.priority >= 70 ? "attention" : ""}`}><p>En yüksek öncelik</p>
        <strong>{top ? top.priority : "—"}</strong><span className="hint">{top ? `${top.flowName} · ${top.step}` : "Açık alarm yok"}</span></div>
    </div>

    <section className="surface">
      <div className="surface-head">
        <div><h2><Icon name="sparkles" size={15} /> Yapay zekâ değerlendirmesi</h2>
          <p>Gruplar, kaynak sağlığı ve geçmiş çözümler birlikte okunur; her grup için kök neden hipotezi ve ilk iş önerilir. Sayılar ve öncelik puanı sistemindir, model yorumlar.</p></div>
        {canEvaluate && <ActionForm action={adminAction} label={latest ? "Yeniden değerlendir" : "Değerlendir"} compact tone="secondary" busy="Değerlendiriliyor…">
          <input type="hidden" name="op" value="ai-triage" /><input type="hidden" name="test" value={test} /></ActionForm>}
      </div>
      <div className="surface-body">
        {aiStatus && !aiStatus.planEnabled && <p className="muted">Yapay zekâ asistanı bu organizasyonun planında yok; gruplar ve toplu işlemler bundan etkilenmez.</p>}
        {(!aiStatus || (aiStatus.planEnabled && !aiStatus.configured)) && <p className="muted">Yapay zekâ şu anda kullanılamıyor. {aiStatus?.reason ?? ""}</p>}
        {aiStatus?.enabled && blockedByTestMode && <p className="muted">Deneme profili yalnızca test verisini sağlayıcıya gönderir; "Yalnızca test" filtresini seçin.</p>}
        {aiStatus?.enabled && quotaLeft === 0 && <p className="muted">Bu proje için son 24 saatteki çağrı sınırı doldu.</p>}
        {pending && <p className="muted">Bir değerlendirme sürüyor; birkaç saniye sonra sayfayı yenileyin.</p>}
        {aiStatus?.enabled && !latest && !pending && triage.groups.length === 0 && <p className="muted">Açık alarm yok; değerlendirilecek bir şey yok.</p>}
        {aiStatus?.enabled && !latest && !pending && triage.groups.length > 0 && !blockedByTestMode && <p className="muted">Henüz değerlendirme yok.{canAct ? "" : " Başlatmak için operatör yetkisi gerekir."}</p>}
        {latest?.result && <>
          {latest.stale && <p className="callout warn">Bu değerlendirme daha eski bir alarm listesine ait; sonrasında alarm açıldı ya da kapandı. Güncel durum için yeniden değerlendirin.</p>}
          <p className="callout">{latest.result.summary}</p>
          <p className="help"><Time value={latest.created_at} />{latest.requested_by && `, ${latest.requested_by}`} · Grup başına hipotez ve ilk iş aşağıdaki kartlarda.</p>
          {latest.result.unknowns.length > 0 && <><h3>Mevcut verilerle bilinmeyenler</h3><ul className="ai-list muted">{latest.result.unknowns.map((u, i) => <li key={i}>{u}</li>)}</ul></>}
          <div className="assign-row">
            <span className="help">Bu değerlendirme işinize yaradı mı?{latest.my_rating && ` Değerlendirmeniz: ${aiRatingNames[latest.my_rating]}.`}</span>
            {(Object.keys(aiRatingNames) as AiRating[]).map(r => <ActionForm key={r} action={adminAction} label={aiRatingNames[r]} compact tone={latest.my_rating === r ? "secondary" : "quiet"}>
              <input type="hidden" name="op" value="ai-feedback" /><input type="hidden" name="id" value={latest.id} /><input type="hidden" name="rating" value={r} /></ActionForm>)}
          </div>
        </>}
        {analyses.some(a => a.status === "failed") && !latest && !pending && <p className="callout bad">{analyses.find(a => a.status === "failed")?.error ?? "Son değerlendirme tamamlanamadı."}</p>}
      </div>
    </section>

    {triage.groups.length === 0 && <Empty icon="check" title="Açık alarm yok">Bütün beklenen adımlar zamanında bildirilmiş. Sorun oluşursa gruplar burada görünür.</Empty>}
    {triage.groups.map(g => <GroupCard key={g.key} group={g} model={modelByKey.get(g.key)} canAct={canAct} assignees={assignees} />)}

    <Explain title="Öncelik ve sinyaller nasıl hesaplanır?">
      <p>Grup anahtarı akış, adım, alarm türü ve hata metninin sayılar maskelenmiş ilk 80 karakteridir. <strong>Ani artış</strong>, grubun alarm sayısının önceki haftadaki altı saatlik ortalamanın en az üç katı olmasıdır.</p>
      <p><strong>Kaynak sessiz</strong>: bir bağlantı 15 dakikadır ya da bir ajan 5 dakikadır olay göndermiyorsa gecikme grupları bu işareti alır. <strong>Sınır şüphesi</strong>: adım son iki haftada en az 20 kez tamamlanmış ve bunların %20'sinden fazlası sınırı aştıktan sonra tamamlanmışsa alarmın sahte olma olasılığı yüksektir.</p>
      <p>Puan: hata alarmı +30, ani artış +25, 10 ve üzeri alarm +15, iki saatten eski +10, çoğunluğu sahipsiz +10, sınır şüphesi −20; 20'den başlar. Yapay zekâ bu puanı değiştirmez, yorumlar.</p>
    </Explain>
  </>;
}

function GroupCard({ group: g, model, canAct, assignees }: {
  group: TriageGroup; model?: ModelGroup; canAct: boolean; assignees: Assignee[];
}) {
  const listHref = "/alarms?" + new URLSearchParams({ state: "open", flow: g.flow, step: g.step, test: "include" });
  const ids = g.ids.join(",");
  return <section className="surface">
    <div className="surface-head">
      <div><h2><span className={`badge ${priorityTone(g.priority)}`}>{g.priority}</span> {g.flowName} · <span className="mono">{g.step}</span> · {alarmKind(g.kind)}</h2>
        <p>{number(g.count)} alarm · ilk <Time value={g.firstOpenedAt} /> · son <Time value={g.lastOpenedAt} /> · {g.unassigned} sahipsiz · {g.unacknowledged} görülmemiş
          {g.ratio >= 1 && g.baseline6h > 0 && <> · önceki haftanın {g.ratio}×</>}</p></div>
      <div className="assign-row">{g.signals.map(s => <span key={s} className={`badge ${s === "threshold_suspect" ? "info" : s === "spike" || s === "source_silent" ? "bad" : "warn"}`}>{triageSignalNames[s]}</span>)}</div>
    </div>
    <div className="surface-body">
      {model && <div className={`callout ${model.grounded ? "" : "warn"}`}>
        <strong>{model.grounded ? "Hipotez" : "Hipotez (kanıta bağlanmadı)"}:</strong> {model.hypothesis}
        {model.action && <><br /><strong>İlk iş:</strong> {model.action}</>}
        <br /><span className="badge" style={{ marginTop: 6 }}>{alarmCauseNames[model.likelyCause] ?? model.likelyCause}</span>{" "}
        <span className={`badge ${modelPriorityTone[model.priority] ?? ""}`}>{triagePriorityNames[model.priority as keyof typeof triagePriorityNames] ?? model.priority}</span>
      </div>}
      <dl className="facts">
        {g.error && <div style={{ gridColumn: "1 / -1" }}><dt>Bildirilen hata</dt><dd><SourceError error={g.error} /></dd></div>}
        <div><dt>Örnek işlemler</dt><dd className="mono">{g.samples.join(", ")}{g.count > g.samples.length && ` +${g.count - g.samples.length}`}</dd></div>
        {g.statsCount > 0 && <div><dt>Adım süresi (14 gün, {g.statsCount} işlem)</dt>
          <dd>p50 {duration(g.p50Seconds)} · p95 {duration(g.p95Seconds)} · sınır {duration(g.allowanceSeconds)} · %{Math.round(g.breachRate * 100)} sınırı aşıp tamamlandı</dd></div>}
        {g.memoryTotal > 0 && <div><dt>Geçmiş (90 gün)</dt><dd>{g.memoryTotal} kapanmış alarm{g.dominantCause && <>; baskın neden <span className="badge">{alarmCauseNames[g.dominantCause]}</span> ({g.dominantCauseCount})</>}
          {g.lastFix && <><br /><strong>Son çözüm:</strong> {g.lastFix}</>}</dd></div>}
      </dl>
      <div className="assign-row" style={{ marginTop: 12 }}>
        <Link className="button secondary small" href={listHref} prefetch={false}><Icon name="bell" />Alarmları listele</Link>
        {canAct && <>
          <ActionForm action={adminAction} label="Hepsini üstlen" compact tone="secondary">
            <input type="hidden" name="op" value="alarm-bulk" /><input type="hidden" name="bulkOp" value="assign" /><input type="hidden" name="ids" value={ids} /><input type="hidden" name="userId" value="me" /></ActionForm>
          <ActionForm action={adminAction} label="Ata" compact tone="secondary">
            <input type="hidden" name="op" value="alarm-bulk" /><input type="hidden" name="bulkOp" value="assign" /><input type="hidden" name="ids" value={ids} />
            <select name="userId" aria-label="Sorumlu seç" className="chip-input" defaultValue=""><option value="">Sorumlu seçin</option>{assignees.map(u => <option key={u.id} value={u.id}>{u.name}</option>)}</select></ActionForm>
          {g.unacknowledged > 0 && <ActionForm action={adminAction} label="Hepsini gördüm" compact tone="quiet">
            <input type="hidden" name="op" value="alarm-bulk" /><input type="hidden" name="bulkOp" value="acknowledge" /><input type="hidden" name="ids" value={ids} /></ActionForm>}
        </>}
      </div>
      <details style={{ marginTop: 12 }}>
        <summary className="help">Kaynak sistem ekibine mesaj taslağı</summary>
        <pre className="error-content" style={{ marginTop: 8, whiteSpace: "pre-wrap" }}>{g.draft}</pre>
      </details>
    </div>
  </section>;
}
