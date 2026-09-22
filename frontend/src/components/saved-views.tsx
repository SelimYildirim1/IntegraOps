import Link from "next/link";
import type { SavedView } from "@/lib/pilot-types";
import ActionForm from "@/components/action-form";
import { adminAction } from "@/app/admin-actions";

// Kullanıcının kendi kayıtlı görünümleri: seçmek için bağlantı, silmek için küçük buton, kaydetmek için ad alanı.
export default function SavedViews({ kind, views, basePath, currentQuery }: {
  kind: "transactions" | "alarms"; views: SavedView[]; basePath: string; currentQuery: string;
}) {
  return <div className="saved-views">
    <div className="chips">
      {views.length === 0 && <span className="muted">Kayıtlı görünüm yok.</span>}
      {views.map(v => <span key={v.id} className={`chip ${v.query === currentQuery ? "active" : ""}`}>
        <Link href={v.query ? `${basePath}?${v.query}` : basePath} prefetch={false}>{v.name}</Link>
        <ActionForm action={adminAction} label="×" compact tone="quiet" busy="…">
          <input type="hidden" name="op" value="view-delete" /><input type="hidden" name="id" value={v.id} />
          <input type="hidden" name="path" value={basePath} />
        </ActionForm>
      </span>)}
    </div>
    <ActionForm action={adminAction} label="Görünümü kaydet" compact tone="secondary">
      <input type="hidden" name="op" value="view-save" /><input type="hidden" name="kind" value={kind} />
      <input type="hidden" name="query" value={currentQuery} /><input type="hidden" name="path" value={basePath} />
      <input name="name" required maxLength={80} placeholder="Görünüm adı" aria-label="Görünüm adı" className="chip-input" />
    </ActionForm>
  </div>;
}
