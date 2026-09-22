"use client";

import { useState, useTransition } from "react";
import { testMapping } from "@/app/connections/actions";
import type { ConnectionTemplate, MappingTestResult } from "@/lib/pilot-types";

// Şablonu örnek veriyle dener; hiçbir olay kaydedilmez.
export default function MappingTester({ catalog }: { catalog: ConnectionTemplate[] }) {
  const first = catalog[0];
  const [template, setTemplate] = useState(first ? JSON.stringify(first.template, null, 1) : "{}");
  const [sample, setSample] = useState(first ? JSON.stringify(first.sample, null, 1) : "{}");
  const [result, setResult] = useState<MappingTestResult | { error: string } | null>(null);
  const [pending, start] = useTransition();
  return <section className="surface">
    <div className="surface-head"><div><h2>Şablonu dene</h2><p>Kaynak sistemin ham verisini yapıştırın; şablonun hangi olayları üreteceğini ve sorunları görün. Kayıt oluşmaz.</p></div></div>
    <div className="surface-body">
      <div className="form-grid">
        <div className="field"><label htmlFor="mt-template">Şablon</label>
          <select aria-label="Hazır şablon" onChange={e => { const t = catalog.find(c => c.code === e.target.value); if (t) { setTemplate(JSON.stringify(t.template, null, 1)); setSample(JSON.stringify(t.sample, null, 1)); } }}>
            {catalog.map(t => <option key={t.code} value={t.code}>{t.name}</option>)}</select>
          <textarea id="mt-template" rows={9} value={template} onChange={e => setTemplate(e.target.value)} spellCheck={false} /></div>
        <div className="field"><label htmlFor="mt-sample">Örnek veri</label><textarea id="mt-sample" rows={11} value={sample} onChange={e => setSample(e.target.value)} spellCheck={false} /></div>
      </div>
      <div className="form-actions" style={{ marginTop: 12 }}>
        <button className="button secondary" type="button" disabled={pending} onClick={() => start(async () => setResult(await testMapping(template, sample)))}>{pending ? "Deneniyor…" : "Şablonu dene"}</button>
      </div>
      {result && ("error" in result ? <p className="form-status err" role="alert">{result.error}</p>
        : <div className="table-wrap"><table><thead><tr><th>#</th><th>İşlem</th><th>Adım</th><th>Durum</th><th>Zaman</th><th>Olay kimliği</th><th>Sorun</th></tr></thead><tbody>
          {result.events.map(e => <tr key={e.index}><td>{e.index + 1}</td><td className="mono">{e.event.transactionId}</td><td className="mono">{e.event.step}</td><td>{e.event.status}</td>
            <td className="mono" style={{ fontSize: 12 }}>{e.event.occurredAt}</td><td className="mono" style={{ fontSize: 12 }}>{e.event.eventId}</td>
            <td>{e.problems.length ? <span className="error-content" style={{ display: "inline-block", padding: "2px 6px" }}>{e.problems.join(" · ")}</span> : <span className="badge good">Geçerli</span>}</td></tr>)}
        </tbody></table></div>)}
    </div>
  </section>;
}
