import Link from "next/link";
import Icon from "@/components/icons";

const fmt = (value: number) => new Intl.NumberFormat("tr-TR").format(value);

// Gösterilecek sayfa numaraları: uçlar her zaman, aradakiler etkin sayfanın çevresinde; boşluklar "…" ile.
export function pageWindow(page: number, totalPages: number): (number | "gap")[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1);
  const near = new Set([1, totalPages, page - 1, page, page + 1]);
  if (page <= 3) [2, 3, 4].forEach(n => near.add(n));
  if (page >= totalPages - 2) [totalPages - 3, totalPages - 2, totalPages - 1].forEach(n => near.add(n));
  const list = [...near].filter(n => n >= 1 && n <= totalPages).sort((a, b) => a - b);
  const out: (number | "gap")[] = [];
  list.forEach((n, i) => { if (i > 0 && n - (list[i - 1] as number) > 1) out.push("gap"); out.push(n); });
  return out;
}

// Sayfalayıcı: bağlantılı (sunucu listeleri) ya da düğmeli (istemci tarafında dilimlenen listeler).
export default function Pager({ page, totalPages, total, pageSize, unit = "kayıt", href, onPage, sizes }: {
  page: number; totalPages: number; total: number; pageSize: number; unit?: string;
  href?: (page: number) => string; onPage?: (page: number) => void;
  sizes?: { value: number; href: string }[];
}) {
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  const item = (target: number, label: React.ReactNode, aria: string, current = false, disabled = false) => {
    const cls = `pager-item${current ? " current" : ""}`;
    if (disabled) return <span className={`${cls} disabled`} aria-hidden="true">{label}</span>;
    if (href) return <Link className={cls} href={href(target)} prefetch={false} aria-label={aria} aria-current={current ? "page" : undefined}>{label}</Link>;
    return <button type="button" className={cls} onClick={() => onPage?.(target)} aria-label={aria} aria-current={current ? "page" : undefined}>{label}</button>;
  };
  return <div className="pager">
    <span className="pager-summary">{total === 0 ? `0 ${unit}` : `${fmt(from)}–${fmt(to)} / ${fmt(total)} ${unit}`}
      {sizes && <span className="pager-sizes">Sayfada
        {sizes.map(s => s.value === pageSize ? <b key={s.value}>{s.value}</b> : <Link key={s.value} href={s.href} prefetch={false}>{s.value}</Link>)}</span>}
    </span>
    {totalPages > 1 && <nav className="pager-nav" aria-label="Sayfalama">
      {item(1, <Icon name="first" />, "İlk sayfa", false, page <= 1)}
      {item(page - 1, <Icon name="left" />, "Önceki sayfa", false, page <= 1)}
      {pageWindow(page, totalPages).map((n, i) => n === "gap"
        ? <span key={`gap-${i}`} className="pager-gap">…</span>
        : item(n, fmt(n), `${n}. sayfa`, n === page))}
      {item(page + 1, <Icon name="right" />, "Sonraki sayfa", false, page >= totalPages)}
      {item(totalPages, <Icon name="last" />, "Son sayfa", false, page >= totalPages)}
    </nav>}
  </div>;
}
