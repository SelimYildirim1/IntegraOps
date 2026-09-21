"use client";

import { useActionState } from "react";
import type { ActionState } from "@/lib/pilot-types";

// Sunucu eylemine bağlı form: gönderim sırasında kilitlenir, sonucu ve bir kez gösterilen sırrı gösterir.
export default function ActionForm({ action, children, label, compact = false, tone = "primary", busy = "İşleniyor…" }: {
  action: (state: ActionState, data: FormData) => Promise<ActionState>;
  children?: React.ReactNode; label: string; compact?: boolean;
  tone?: "primary" | "secondary" | "danger" | "quiet"; busy?: string;
}) {
  const [state, submit, pending] = useActionState(action, { message: "" });
  const buttonClass = `button ${tone === "primary" ? "" : tone} ${compact ? "small" : ""}`.trim();
  return <form action={submit} className={compact ? "inline-form" : "admin-form"}>
    <fieldset disabled={pending}>{children}<button className={buttonClass} type="submit">{pending ? busy : label}</button></fieldset>
    {state.message && <p role="status" className={`form-status ${state.ok ? "ok" : "err"}`}>{state.message}</p>}
    {state.secret && <div className="secret-box"><strong>Bu değer yalnızca şimdi gösterilir. Güvenli bir yere kaydedin.</strong>
      <code>{state.secret}</code></div>}
  </form>;
}
