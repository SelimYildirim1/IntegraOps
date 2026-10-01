import Link from "next/link";
import type { Filters } from "@/lib/types";
import { deadline } from "@/lib/time";
import Icon, { type IconName } from "@/components/icons";

export const statusLabels: Record<string, string> = {
  pending: "İşlenmeyi bekliyor", inProgress: "Devam ediyor",
  completed: "Tamamlandı", failed: "Hata bildirildi", cancelled: "İptal edildi",
};
export const observationLabels: Record<string, string> = {
  not_observed: "Bildirim yok", completed: "Tamamlandı", failed: "Hata bildirildi", skipped: "Atlandı", cancelled: "İptal", not_applicable: "Bu işlem için beklenmiyor",
};
export const delayLabels: Record<string, string> = {
  none: "Yok", waiting: "Bekleniyor", overdue: "Gecikmiş",
  pending_processing: "Bildirim işleniyor", pending_evaluation: "Kontrol bekleniyor", catching_up: "Geç teslim; kaynak bekleniyor",
};

export function Badge({ value, delay = false }: { value: string; delay?: boolean }) {
  const tone = value === "completed" ? "good" : value === "failed" ? "bad"
    : value === "overdue" ? "warn" : ["waiting", "inProgress"].includes(value) ? "info" : "";
  const label = observationLabels[value] && !statusLabels[value] ? observationLabels[value]
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
  return `/?${filterQuery(next)}`;
}
// Query string without empty values, so saved views and links stay short.
export function filterQuery(filters: Filters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, page: String(filters.page), pageSize: String(filters.pageSize) }))
    if (value !== "" && !(key === "page" && value === "1") && !(key === "pageSize" && value === "20") && !(key === "test" && value === "exclude"))
      params.set(key, value);
  return params.toString();
}
// Dates come from <input type="date">; the panel sends them as plain days (UTC midnight on the API side).
export const dateInput = /^\d{4}-\d{2}-\d{2}$/;

export function Message({ title, description, href = "/", action = "İşlem listesine dön", icon = "info" }: {
  title: string; description: string; href?: string; action?: string; icon?: IconName;
}) {
  return <section className="surface message" role="status">
    <span className="message-icon"><Icon name={icon} size={22} /></span>
    <h1>{title}</h1><p>{description}</p>
    <Link className="button secondary" href={href} prefetch={false}>{action}</Link>
  </section>;
}

export function PageHead({ title, lede, crumb, icon, children }: {
  title: React.ReactNode; lede?: React.ReactNode; crumb?: { href: string; label: string }; icon?: IconName; children?: React.ReactNode;
}) {
  return <>
    {crumb && <Link className="crumb" href={crumb.href} prefetch={false}><Icon name="back" size={14} />{crumb.label}</Link>}
    <div className="page-head">
      <div className="page-title">
        {icon && <span className="page-icon"><Icon name={icon} size={20} /></span>}
        <div><h1>{title}</h1>{lede && <p className="lede">{lede}</p>}</div>
      </div>
      {children && <div className="head-actions">{children}</div>}
    </div>
  </>;
}

// Açıklama metnini varsayılan olarak katlar; ekran veriyi ve düğmeleri gösterir, anlatım istenince açılır.
export function Explain({ title = "Nasıl çalışır?", children }: { title?: string; children: React.ReactNode }) {
  return <details className="explain">
    <summary><Icon name="info" size={15} />{title}</summary>
    <div className="explain-body">{children}</div>
  </details>;
}

// Boş liste durumu: ikon, başlık, tek satır yönlendirme ve gerekiyorsa bir eylem.
export function Empty({ icon = "inbox", title, children, action }: {
  icon?: IconName; title: string; children?: React.ReactNode; action?: React.ReactNode;
}) {
  return <div className="empty">
    <span className="empty-icon"><Icon name={icon} size={22} /></span>
    <h3>{title}</h3>
    {children && <p>{children}</p>}
    {action}
  </div>;
}
