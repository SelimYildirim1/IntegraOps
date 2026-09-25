"use client";

import { useState } from "react";
import Pager from "@/components/pager";

// Tablo olmayan uzun listeleri (kartlar, satırlar) sayfa sayfa gösterir; öğeler hazır React düğümleri olarak verilir.
export default function PagedList({ items, pageSize = 10, unit = "kayıt", className, empty = null }: {
  items: React.ReactNode[]; pageSize?: number; unit?: string; className?: string; empty?: React.ReactNode;
}) {
  const [page, setPage] = useState(1);
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const current = Math.min(page, totalPages);
  if (items.length === 0) return <>{empty}</>;
  return <>
    <div className={className}>{items.slice((current - 1) * pageSize, current * pageSize)}</div>
    {items.length > pageSize && <Pager page={current} totalPages={totalPages} total={items.length} pageSize={pageSize} unit={unit} onPage={setPage} />}
  </>;
}
