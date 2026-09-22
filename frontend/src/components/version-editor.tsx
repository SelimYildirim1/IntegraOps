"use client";

import { useActionState, useRef, useState } from "react";
import Link from "next/link";
import { draftAction, type DraftState } from "@/app/flows/versions-actions";
import { delayDraft, type FlowVersionDetails } from "@/lib/flow-details";
import type { Calendar } from "@/lib/pilot-types";

type StepDraft = { id: number; code: string; name: string; delayValue: string; delayUnit: string; overrides: string; parallel: boolean; kind: string; groups: string };
const units: Record<string, number> = { seconds: 1, minutes: 60, hours: 3600, days: 86400 };
const blank = (id: number): StepDraft => ({ id, code: "", name: "", delayValue: "", delayUnit: "minutes", overrides: "", parallel: false, kind: "required", groups: "" });

// "vip=30m, standard=2h" biçimindeki grup sürelerini saniyeye çevirir.
export function parseOverrides(text: string): { ok: true; value: Record<string, number> } | { ok: false; error: string } {
  const value: Record<string, number> = {};
  for (const part of text.split(",").map(p => p.trim()).filter(Boolean)) {
    const m = /^([a-z][a-z0-9-]{0,63})\s*=\s*(\d+)\s*(s|sn|m|dk|h|sa|d|g)$/i.exec(part);
    if (!m) return { ok: false, error: `Grup süresi anlaşılmadı: "${part}". Örnek: vip=30dk, kurumsal=2sa` };
    const unit = m[3].toLowerCase(); const mult = unit === "s" || unit === "sn" ? 1 : unit === "m" || unit === "dk" ? 60 : unit === "h" || unit === "sa" ? 3600 : 86400;
    const seconds = Number(m[2]) * mult;
    if (seconds < 1 || seconds > 2592000) return { ok: false, error: `${m[1]} grubunun süresi 1 saniye–30 gün arasında olmalı.` };
    value[m[1]] = seconds;
  }
  return { ok: true, value };
}
function overridesText(overrides?: Record<string, number>) {
  return Object.entries(overrides ?? {}).map(([g, s]) => {
    const d = delayDraft(s); const unit = d.delayUnit === "days" ? "g" : d.delayUnit === "hours" ? "sa" : d.delayUnit === "minutes" ? "dk" : "sn";
    return `${g}=${d.delayValue}${unit}`;
  }).join(", ");
}

export default function VersionEditor({ draft, calendars }: { draft: FlowVersionDetails; calendars: Calendar[] }) {
  const [state, action, pending] = useActionState(draftAction, { message: "" } as DraftState);
  const [name, setName] = useState(draft.name);
  const [note, setNote] = useState(draft.note);
  const [calendarId, setCalendarId] = useState(draft.calendarId ?? "");
  const [effectiveFrom, setEffectiveFrom] = useState("");
  const [steps, setSteps] = useState<StepDraft[]>(() => {
    const sorted = [...draft.steps].sort((a, b) => a.position - b.position);
    return sorted.map((s, i) => ({ id: i, code: s.code, name: s.name, ...delayDraft(s.maxDelaySeconds), overrides: overridesText(s.overrides),
      parallel: i > 0 && (s.stage ?? i) === (sorted[i - 1].stage ?? i - 1), kind: s.kind ?? "required", groups: (s.groups ?? []).join(", ") }));
  });
  const [error, setError] = useState("");
  const nextId = useRef(draft.steps.length);
  const update = (id: number, change: Partial<StepDraft>) => setSteps(cur => cur.map(s => s.id === id ? { ...s, ...change } : s));
  const move = (index: number, delta: number) => setSteps(cur => {
    const next = [...cur]; const target = index + delta;
    if (target < 0 || target >= next.length) return cur;
    [next[index], next[target]] = [next[target], next[index]]; return next;
  });

  function payload(): string | null {
    const out = [];
    let stage = 0;
    for (let i = 0; i < steps.length; i++) {
      const s = steps[i];
      const parsed = parseOverrides(s.overrides);
      if (!parsed.ok) { setError(parsed.error); return null; }
      if (i > 0) stage = s.parallel ? stage : stage + 1;
      const optional = s.kind === "optional";
      const seconds = i === 0 || (optional && s.delayValue.trim() === "") ? null : Number(s.delayValue) * (units[s.delayUnit] ?? 0);
      if (i > 0 && seconds !== null && (!/^\d+$/.test(s.delayValue) || seconds < 1 || seconds > 2592000)) { setError(`${i + 1}. adımın süresi 1 saniye–30 gün arasında olmalı.`); return null; }
      const groups = s.groups.split(",").map(g => g.trim()).filter(Boolean);
      if (s.kind === "conditional" && groups.length === 0) { setError(`${i + 1}. koşullu adım için en az bir grup yazın.`); return null; }
      out.push({ code: s.code.trim(), name: s.name.trim(), maxDelaySeconds: seconds, overrides: parsed.value, stage, kind: s.kind, groups: s.kind === "conditional" ? groups : [] });
    }
    setError("");
    return JSON.stringify({ name: name.trim(), note: note.trim(), calendarId, steps: out });
  }

  return <form action={action} noValidate className="surface flow-editor" onSubmit={e => { const p = payload(); if (!p) e.preventDefault(); else (e.currentTarget.elements.namedItem("payload") as HTMLInputElement).value = p; }}>
    <input type="hidden" name="flow" value={draft.flow} /><input type="hidden" name="version" value={draft.version} />
    <input type="hidden" name="payload" defaultValue="" />
    <fieldset disabled={pending}>
      <div className="surface-body">
        <p className="legend">Sürüm bilgileri</p>
        <div className="form-grid">
          <div className="field"><label htmlFor="v-name">Akış adı</label><input id="v-name" value={name} maxLength={160} onChange={e => setName(e.target.value)} /></div>
          <div className="field"><label htmlFor="v-cal">Çalışma takvimi</label>
            <select id="v-cal" value={calendarId} onChange={e => setCalendarId(e.target.value)}>
              <option value="">7/24, takvim yok</option>
              {calendars.map(c => <option key={c.id} value={c.id}>{c.name} ({c.timezone}, {c.start_time}–{c.end_time})</option>)}
            </select>
            <span className="hint">Takvim seçilirse süreler yalnızca çalışma saatlerinde işler; takvimler Ayarlar ekranında tanımlanır.</span></div>
          <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor="v-note">Sürüm notu</label>
            <input id="v-note" value={note} maxLength={500} placeholder="Neden değişti? Örnek: fatura süresi 4 saate çıkarıldı" onChange={e => setNote(e.target.value)} /></div>
        </div>
      </div>
      <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <div className="step-card-head" style={{ marginBottom: 4 }}><p className="legend" style={{ margin: 0 }}>Adımlar</p><span className="count">{steps.length} / 30 adım</span></div>
        <p className="help">Adım kodları kaynak sistemlerin gönderdiği kodlardır. "Önceki adımla paralel" işaretlenen adım aynı aşamada ilerler; sonraki aşama hepsini bekler. İsteğe bağlı adım beklenmez ve ölçülmez; koşullu adım yalnızca yazılan gruplardaki işlemler için beklenir. Grup süreleri, işlemi <code>group</code> alanıyla gönderen kaynaklar için farklı süre tanır; örnek: <code>vip=30dk, kurumsal=2sa</code>.</p>
        <ol className="step-list">{steps.map((s, i) => <li key={s.id} className="step-card">
          <div className="step-card-head"><h3><span className="idx">{i + 1}</span>{i === 0 ? "Başlangıç adımı" : `${i + 1}. adım`}</h3>
            <div className="cell-actions">
              <button className="button quiet small" type="button" disabled={i === 0} onClick={() => move(i, -1)}>Yukarı</button>
              <button className="button quiet small" type="button" disabled={i === steps.length - 1 || i === 0} onClick={() => move(i, 1)}>Aşağı</button>
              <button className="button quiet small" type="button" disabled={steps.length <= 2 || i === 0} onClick={() => setSteps(cur => cur.filter(x => x.id !== s.id))}>Kaldır</button>
            </div></div>
          <div className="form-grid">
            <div className="field"><label htmlFor={`s-${s.id}-name`}>Adım adı</label><input id={`s-${s.id}-name`} value={s.name} maxLength={160} onChange={e => update(s.id, { name: e.target.value })} /></div>
            <div className="field"><label htmlFor={`s-${s.id}-code`}>Adım kodu</label><input id={`s-${s.id}-code`} value={s.code} maxLength={64} autoCapitalize="none" spellCheck={false} onChange={e => update(s.id, { code: e.target.value })} /></div>
          </div>
          {i > 0 && <div className="form-grid">
            <div className="field"><label htmlFor={`s-${s.id}-kind`}>Tür</label>
              <select id={`s-${s.id}-kind`} value={s.kind} onChange={e => update(s.id, { kind: e.target.value })}>
                <option value="required">Zorunlu</option><option value="optional">İsteğe bağlı</option><option value="conditional">Koşullu (gruba göre)</option></select></div>
            <div className="field"><span className="label">Aşama</span>
              <label className="toggle"><input type="checkbox" checked={s.parallel} onChange={e => update(s.id, { parallel: e.target.checked })} />Önceki adımla paralel</label></div>
            {s.kind === "conditional" && <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor={`s-${s.id}-groups`}>Beklendiği gruplar (virgülle)</label>
              <input id={`s-${s.id}-groups`} value={s.groups} placeholder="export, kurumsal" onChange={e => update(s.id, { groups: e.target.value })} /></div>}
            <div className="field"><label htmlFor={`s-${s.id}-delay`}>{s.parallel ? "Aşama başlangıcından" : `${steps[i - 1].name || "Önceki adım"} sonrası`} tanınan süre{s.kind === "optional" ? " (isteğe bağlı)" : ""}</label>
              <div className="inline-fields">
                <input id={`s-${s.id}-delay`} type="number" min={1} step={1} value={s.delayValue} inputMode="numeric" onChange={e => update(s.id, { delayValue: e.target.value })} />
                <select aria-label="Birim" value={s.delayUnit} onChange={e => update(s.id, { delayUnit: e.target.value })}>
                  <option value="seconds">Saniye</option><option value="minutes">Dakika</option><option value="hours">Saat</option><option value="days">Gün</option></select>
              </div></div>
            <div className="field"><label htmlFor={`s-${s.id}-ov`}>Grup süreleri</label>
              <input id={`s-${s.id}-ov`} value={s.overrides} placeholder="vip=30dk, kurumsal=2sa" onChange={e => update(s.id, { overrides: e.target.value })} /></div>
          </div>}
        </li>)}</ol>
        <button className="button secondary" type="button" disabled={steps.length >= 30} onClick={() => setSteps(cur => [...cur, blank(nextId.current++)])}>Adım ekle</button>
        {(error || state.message) && <p className={`form-status ${state.ok && !error ? "ok" : "err"}`} role="alert">{error || state.message}</p>}
        <div className="form-actions">
          <button className="button secondary" type="submit" name="op" value="save">{pending ? "Kaydediliyor…" : "Taslağı kaydet"}</button>
          <div className="field"><label htmlFor="v-eff">Etkinleşme zamanı (boşsa hemen)</label>
            <input id="v-eff" type="datetime-local" name="effectiveFrom" value={effectiveFrom} onChange={e => setEffectiveFrom(e.target.value)} /></div>
          <button className="button" type="submit" name="op" value="publish">Yayınla</button>
          <button className="button danger" type="submit" name="op" value="delete" formNoValidate onClick={e => { if (!confirm("Taslak silinsin mi?")) e.preventDefault(); }}>Taslağı sil</button>
          <Link href={`/flows/view/${encodeURIComponent(draft.flow)}`} prefetch={false}>Vazgeç</Link>
        </div>
      </div>
    </fieldset>
  </form>;
}
