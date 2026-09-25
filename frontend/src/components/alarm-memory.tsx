import Link from "next/link";
import ActionForm from "@/components/action-form";
import { Time } from "@/components/ui";
import Icon from "@/components/icons";
import { adminAction } from "@/app/admin-actions";
import type { Alarm, AlarmCause, AlarmMemory } from "@/lib/pilot-types";
import { alarmCauseHints, alarmCauseNames } from "@/lib/pilot-types";

export function duration(seconds: number | null) {
  if (seconds === null) return "açık";
  if (seconds < 3600) return `${Math.max(1, Math.round(seconds / 60))} dk`;
  if (seconds < 86400) return `${(seconds / 3600).toFixed(seconds < 36000 ? 1 : 0)} sa`;
  return `${Math.round(seconds / 86400)} gün`;
}

/** Operator's root cause and fix; recorded once, recalled on every later alarm of the same flow and step. */
export function CauseForm({ alarm, canAct }: { alarm: Alarm; canAct: boolean }) {
  const recorded = Boolean(alarm.cause);
  return <section className="surface">
    <div className="surface-head">
      <div><h2><Icon name="check" size={15} /> Neden ve çözüm</h2>
        <p>{recorded ? "Kaydedilen neden, bu adımdaki sonraki alarmlarda hatırlatılır." : alarm.resolved_at
          ? "Bu alarm nasıl çözüldü? Bir kez yazın; aynı adımda bir daha yaşandığında ekip ve yapay zekâ buradan başlar."
          : "Nedeni bulduğunuzda kaydedin; alarm kapanmasını beklemeniz gerekmez."}</p></div>
      {recorded && <span className="badge good">{alarmCauseNames[alarm.cause as AlarmCause]}</span>}
    </div>
    <div className="surface-body">
      {recorded && <dl className="facts">
        <div><dt>Neden</dt><dd>{alarmCauseNames[alarm.cause as AlarmCause]}<br /><span className="help">{alarmCauseHints[alarm.cause as AlarmCause]}</span></dd></div>
        <div><dt>Çözüm</dt><dd>{alarm.fix || <span className="muted">Yazılmadı</span>}</dd></div>
        <div><dt>Kaydeden</dt><dd className="time">{alarm.cause_set_by_name ?? "—"}{alarm.cause_set_at && <>, <Time value={alarm.cause_set_at} /></>}</dd></div>
      </dl>}
      {canAct && <ActionForm action={adminAction} label={recorded ? "Güncelle" : "Kaydet"} busy="Kaydediliyor…" tone={recorded ? "secondary" : undefined}>
        <input type="hidden" name="op" value="alarm-cause" /><input type="hidden" name="id" value={alarm.id} />
        <div className="field"><label htmlFor="cause">Neden</label>
          <select id="cause" name="cause" required defaultValue={alarm.cause ?? ""}>
            <option value="" disabled>Neden sınıfı seçin</option>
            {(Object.keys(alarmCauseNames) as AlarmCause[]).map(c => <option key={c} value={c}>{alarmCauseNames[c]} — {alarmCauseHints[c]}</option>)}
          </select></div>
        <div className="field"><label htmlFor="fix">Çözüm (ne yapıldı, kim yaptı)</label>
          <textarea id="fix" name="fix" maxLength={1000} rows={2} defaultValue={alarm.fix ?? ""} placeholder="Örn. ERP outbox servisi yeniden başlatıldı; olaylar 15 dakika içinde geldi." /></div>
      </ActionForm>}
      {!canAct && !recorded && <p className="muted">Neden henüz kaydedilmedi.</p>}
    </div>
  </section>;
}

/** What happened the last times this flow and step raised the same alarm: computed by the API, no model involved. */
export function MemoryPanel({ memory, flow, step }: { memory: AlarmMemory; flow: string; step: string }) {
  const total = Number(memory.total);
  const listHref = `/alarms?${new URLSearchParams({ state: "all", flow, step })}`;
  return <section className="surface">
    <div className="surface-head">
      <div><h2><Icon name="history" size={15} /> Bu adımda daha önce</h2>
        <p>Son {memory.window_days} günde aynı akış ve adımda açılan aynı türden alarmlar.</p></div>
      <span className="count">{total} alarm</span>
    </div>
    <div className="surface-body">
      {total === 0 && <p className="muted">Bu adım için geçmiş kayıt yok; ilk vaka bu. Nedenini kaydederseniz sonraki sefer buradan başlanır.</p>}
      {total > 0 && <>
        {memory.causes.length > 0
          ? <p className="callout">{memory.causes.map(c => `${alarmCauseNames[c.cause]}: ${c.count}`).join(" · ")}
            {Number(memory.with_cause) < total && <span className="help"> ({total - Number(memory.with_cause)} alarmın nedeni girilmemiş)</span>}</p>
          : <p className="callout warn">{total} geçmiş alarmın hiçbirine neden girilmemiş; bu yüzden geçmiş yol göstermiyor.</p>}
        <ol className="history">
          {memory.recent.map(r => <li key={r.id}>
            <span className="time"><Time value={r.opened_at} /></span>
            <span><Link className="ident" href={`/alarms/${r.id}`} prefetch={false}>{r.business_id}</Link>
              {" "}<span className="help">{duration(r.resolved_after_seconds)}</span>
              {r.cause && <> · <span className="badge">{alarmCauseNames[r.cause]}</span></>}
              {r.fix && <><br /><strong>Çözüm:</strong> {r.fix}</>}
              {!r.fix && r.note && <><br /><strong>Son not:</strong> {r.note}</>}
            </span></li>)}
        </ol>
        <p className="help"><Link href={listHref} prefetch={false}>Bu adımın bütün alarmlarını listele</Link></p>
      </>}
    </div>
  </section>;
}
