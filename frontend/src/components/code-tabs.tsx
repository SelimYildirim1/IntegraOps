"use client";

import { useId, useState } from "react";

// Aynı isteğin farklı dillerdeki hali. Tek kod alanı, sekmeyle değişir; sayfa uzamaz.
export default function CodeTabs({ tabs }: { tabs: { label: string; code: string; note?: string }[] }) {
  const [active, setActive] = useState(0);
  const id = useId();
  const current = tabs[active];
  return <div className="code-tabs">
    <div className="code-tabs-bar" role="tablist" aria-label="Örnek dili">
      {tabs.map((t, i) => <button key={t.label} role="tab" id={`${id}-tab-${i}`} aria-selected={i === active} aria-controls={`${id}-panel`}
        type="button" onClick={() => setActive(i)}>{t.label}</button>)}
    </div>
    <pre id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-tab-${active}`} className="code-block" tabIndex={0}>{current.code}</pre>
    {current.note && <p className="code-note">{current.note}</p>}
  </div>;
}
