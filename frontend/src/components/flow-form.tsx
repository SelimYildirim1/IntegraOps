"use client";

import { useActionState, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { createFlowAction } from "@/app/flows/new/actions";
import { initialFlowState } from "@/lib/flow-form";
import { delayDraft, type FlowDetails } from "@/lib/flow-details";

type StepDraft = {
  id: number; code: string; name: string; delayValue: string; delayUnit: string; instructions: string; link: string;
};
const blankStep = (id: number): StepDraft => ({ id, code: "", name: "", delayValue: "", delayUnit: "minutes", instructions: "", link: "" });

function Field({ id, label, error, hint, children }: {
  id: string; label: string; error?: string; hint?: string; children: ReactNode;
}) {
  return <div className="field"><label htmlFor={id}>{label}</label>{children}
    {error ? <span id={`${id}-error`} className="error">{error}</span> : hint ? <span className="hint">{hint}</span> : null}
  </div>;
}

export default function FlowForm({ initialFlow }: { initialFlow?: FlowDetails }) {
  const [state, action, pending] = useActionState(createFlowAction, initialFlowState);
  const [code, setCode] = useState("");
  const [name, setName] = useState(initialFlow?.name ?? "");
  const [steps, setSteps] = useState<StepDraft[]>(() => initialFlow
    ? [...initialFlow.steps].sort((a, b) => a.position - b.position).map((step, index) => ({
      id: index, code: step.code, name: step.name, ...delayDraft(step.maxDelaySeconds), instructions: step.instructions ?? "", link: step.link ?? "",
    })) : [blankStep(0), blankStep(1)]);
  const [edited, setEdited] = useState(false);
  const nextId = useRef(initialFlow?.steps.length ?? 2);
  const errors = edited ? {} : state.errors;
  const accessibility = (id: string) => ({
    "aria-invalid": Boolean(errors[id]),
    "aria-describedby": errors[id] ? `${id}-error` : undefined,
  });
  const updateStep = (id: number, change: Partial<StepDraft>) => {
    setEdited(true);
    setSteps(current => current.map(step => step.id === id ? { ...step, ...change } : step));
  };

  if (state.createdCode) return <section className="surface message" role="status">
    <h1>Akış oluşturuldu</h1><p><span className="mono">{state.createdCode}</span> kaydedildi. İlk bildirim geldiğinde bu akışın işlemleri listelenmeye başlar.</p>
    <div className="form-actions" style={{ justifyContent: "center" }}>
      <Link className="button" href={`/flows/view/${encodeURIComponent(state.createdCode)}`} prefetch={false}>Akışı görüntüle</Link>
      <Link className="button secondary" href="/flows" prefetch={false}>Akışlara dön</Link>
    </div>
  </section>;

  return <form action={action} noValidate onSubmit={() => setEdited(false)} className="surface flow-editor">
    <input type="hidden" name="payload" value={JSON.stringify({ code, name, steps })} />
    <fieldset disabled={pending}>
      <div className="surface-body">
        <p className="legend">Akış bilgileri</p>
        <div className="form-grid">
          <Field id="flow-name" label="Akış adı" error={errors["flow-name"]}>
            <input id="flow-name" value={name} maxLength={160} required {...accessibility("flow-name")}
              placeholder="Örnek: Sipariş ve fatura takibi" onChange={event => { setName(event.target.value); setEdited(true); }} />
          </Field>
          <Field id="flow-code" label="Akış kodu" error={errors["flow-code"]} hint="Küçük harf, rakam ve tire. Bildirim gönderen sistemler bu kodu kullanır.">
            <input id="flow-code" value={code} maxLength={64} required {...accessibility("flow-code")}
              placeholder="Örnek: siparis-fatura" autoCapitalize="none" spellCheck={false}
              onChange={event => { setCode(event.target.value); setEdited(true); }} />
          </Field>
        </div>
      </div>
      <div className="surface-body" style={{ borderTop: "1px solid var(--line-2)" }}>
        <div className="step-card-head" style={{ marginBottom: 4 }}><p className="legend" style={{ margin: 0 }}>Adımlar</p><span className="count">{steps.length} / 30 adım</span></div>
        <p className="help">Adımları gerçekleşmesini beklediğiniz sırayla ekleyin. Her süre, bir önceki adımın gerçekleşme anından başlar; en az 1 saniye, en fazla 30 gün.</p>
        {errors.steps && <p className="form-status err" role="alert">{errors.steps}</p>}

        <ol className="step-list">{steps.map((step, index) => {
          const prefix = `step-${index}`;
          return <li key={step.id} className="step-card">
            <div className="step-card-head"><h3><span className="idx">{index + 1}</span>{index === 0 ? "Başlangıç adımı" : `${index + 1}. adım`}</h3>
              <button className="button quiet small" type="button" disabled={steps.length <= 2 || index === 0}
                aria-label={`${index + 1}. adımı kaldır`} onClick={() => {
                  setSteps(current => current.filter(item => item.id !== step.id)); setEdited(true);
                }}>Kaldır</button>
            </div>
            <div className="form-grid">
              <Field id={`${prefix}-name`} label="Adım adı" error={errors[`${prefix}-name`]}>
                <input id={`${prefix}-name`} value={step.name} maxLength={160} required {...accessibility(`${prefix}-name`)}
                  placeholder={index === 0 ? "Örnek: ERP kaydı" : "Örnek: Fatura"}
                  onChange={event => updateStep(step.id, { name: event.target.value })} />
              </Field>
              <Field id={`${prefix}-code`} label="Adım kodu" error={errors[`${prefix}-code`]}>
                <input id={`${prefix}-code`} value={step.code} maxLength={64} required {...accessibility(`${prefix}-code`)}
                  placeholder={index === 0 ? "Örnek: erp-created" : "Örnek: invoice-created"} autoCapitalize="none" spellCheck={false}
                  onChange={event => updateStep(step.id, { code: event.target.value })} />
              </Field>
            </div>
            {index === 0 ? <p className="hint">İlk adım saati başlatır; kendisi için süre tanımlanmaz.</p>
              : <div className="form-grid">
                <Field id={`${prefix}-delay`} label={`${steps[index - 1].name || "Önceki adım"} sonrası tanınan süre`} error={errors[`${prefix}-delay`]}>
                  <input id={`${prefix}-delay`} type="number" min={1} step={1} required value={step.delayValue} inputMode="numeric"
                    {...accessibility(`${prefix}-delay`)} onChange={event => updateStep(step.id, { delayValue: event.target.value })} />
                </Field>
                <Field id={`${prefix}-unit`} label="Birim">
                  <select id={`${prefix}-unit`} value={step.delayUnit} onChange={event => updateStep(step.id, { delayUnit: event.target.value })}>
                    <option value="seconds">Saniye</option><option value="minutes">Dakika</option><option value="hours">Saat</option><option value="days">Gün</option>
                  </select>
                </Field>
              </div>}
            <details className="details runbook-fields" open={Boolean(step.instructions || step.link)}><summary>Çalışma talimatı ve kaynak sistem bağlantısı (isteğe bağlı)</summary>
              <div className="form-grid">
                <Field id={`${prefix}-instructions`} label="Bu adım geciktiğinde ya da hata verdiğinde ne yapılmalı?" error={errors[`${prefix}-instructions`]} hint="Alarm iletisinde ve alarm sayfasında gösterilir.">
                  <textarea id={`${prefix}-instructions`} rows={3} maxLength={2000} value={step.instructions} {...accessibility(`${prefix}-instructions`)}
                    onChange={event => updateStep(step.id, { instructions: event.target.value })} />
                </Field>
                <Field id={`${prefix}-link`} label="Kaynak sistemde aç (bağlantı şablonu)" error={errors[`${prefix}-link`]} hint="{transactionId} işlem numarasıyla doldurulur.">
                  <input id={`${prefix}-link`} value={step.link} maxLength={1024} placeholder="https://erp.sirket.com/orders/{transactionId}" autoCapitalize="none" spellCheck={false}
                    {...accessibility(`${prefix}-link`)} onChange={event => updateStep(step.id, { link: event.target.value })} />
                </Field>
              </div>
            </details>
          </li>;
        })}</ol>

        <button className="button secondary" type="button" disabled={steps.length >= 30} onClick={() => {
          const id = nextId.current++; setSteps(current => [...current, blankStep(id)]); setEdited(true);
        }}>Adım ekle</button>
        {!edited && state.message && <p className="form-status err" role="alert">{state.message}</p>}
        <div className="form-actions">
          <button className="button" type="submit">{pending ? "Kaydediliyor…" : "Akışı oluştur"}</button>
          <Link href="/flows" prefetch={false}>Vazgeç</Link>
        </div>
      </div>
    </fieldset>
  </form>;
}
