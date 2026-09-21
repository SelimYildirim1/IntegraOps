import Link from "next/link";
import type { Filters } from "@/lib/types";
import { deadline } from "@/lib/time";

export const statusLabels: Record<string, string> = {
  pending: "İşlenmeyi bekliyor", inProgress: "Devam ediyor",
  completed: "Tamamlandı", failed: "Hata bildirildi",
};
export const delayLabels: Record<string, string> = {
  none: "Yok", waiting: "Bekleniyor", overdue: "Gecikmiş",
  pending_processing: "Bildirim işleniyor", pending_evaluation: "Kontrol bekleniyor",
};

export function Badge({ value, delay = false }: { value: string; delay?: boolean }) {
  const tone = value === "completed" ? "good" : value === "failed" ? "bad"
    : value === "overdue" ? "warn" : ["waiting", "inProgress"].includes(value) ? "info" : "";
  const label = value === "not_observed" ? "Bildirim yok"
    : (delay ? delayLabels[value] : statusLabels[value]) ?? value;
  return <span className={`badge ${tone}`}>{label}</span>;
}

// Bekleyen bir adım için son zamana kalan süreyi çubukla gösterir.
export function DeadlineMeter({ startedAt, deadlineAt, state }: {
  startedAt: string | null; deadlineAt: string | null; state: string;
}) {
  const d = deadline(startedAt, deadlineAt);
  if (!d || (state !== "waiting" && state !== "overdue")) return null;
  const tone = state === "overdue" || d.late ? "warn" : d.ratio > 0.8 ? "warn" : "";
  return <div className={`meter ${tone}`} aria-label={`Tanınan süre ${d.allowance}, ${d.text}`}>
    <span className="meter-text"><span>{d.text}</span><span>{d.allowance}</span></span>
    <span className="track"><span className="fill" style={{ width: `${Math.round(d.ratio * 100)}%` }} /></span>
  </div>;
}

export function date(value: string | null | undefined) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "short", timeStyle: "medium", timeZone: "Europe/Istanbul",
  }).format(new Date(value));
}
export function Time({ value }: { value: string | null | undefined }) {
  return <span className="time">{date(value)}</span>;
}
export const number = (value: number) => new Intl.NumberFormat("tr-TR").format(value);

export function listHref(filters: Filters, changes: Partial<Filters> = {}) {
  const next = { ...filters, ...changes };
  return `/?${new URLSearchParams({ ...next, page: String(next.page), pageSize: String(next.pageSize) })}`;
}

export function Message({ title, description, href = "/", action = "İşlem listesine dön" }: {
  title: string; description: string; href?: string; action?: string;
}) {
  return <section className="surface message" role="status">
    <h1>{title}</h1><p>{description}</p>
    <Link className="button secondary" href={href} prefetch={false}>{action}</Link>
  </section>;
}

export function PageHead({ title, lede, crumb, children }: {
  title: React.ReactNode; lede?: React.ReactNode; crumb?: { href: string; label: string }; children?: React.ReactNode;
}) {
  return <>
    {crumb && <Link className="crumb" href={crumb.href} prefetch={false}>{crumb.label}</Link>}
    <div className="page-head">
      <div><h1>{title}</h1>{lede && <p className="lede">{lede}</p>}</div>
      {children && <div className="head-actions">{children}</div>}
    </div>
  </>;
}
