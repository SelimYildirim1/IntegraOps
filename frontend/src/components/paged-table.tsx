"use client";

import { useState } from "react";
import Pager from "@/components/pager";

// Sunucudan tamamı gelen listeleri sayfa sayfa gösterir; satırlar hazır <tr> öğeleri olarak verilir.
export default function PagedTable({ head, rows, pageSize = 20, unit = "kayıt", empty = null, className }: {
  head: React.ReactNode; rows: React.ReactNode[]; pageSize?: number; unit?: string; empty?: React.ReactNode; className?: string;
}) {
  const [page, setPage] = useState(1);
  const totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
  const current = Math.min(page, totalPages);
  if (rows.length === 0) return <>{empty}</>;
  return <>
    <div className={className ? `table-wrap ${className}` : "table-wrap"}>
      <table><thead>{head}</thead><tbody>{rows.slice((current - 1) * pageSize, current * pageSize)}</tbody></table>
    </div>
    {rows.length > pageSize && <Pager page={current} totalPages={totalPages} total={rows.length} pageSize={pageSize} unit={unit} onPage={setPage} />}
  </>;
}
