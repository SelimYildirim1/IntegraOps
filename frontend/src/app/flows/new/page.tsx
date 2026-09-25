import FlowForm from "@/components/flow-form";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess, getFlowCatalog } from "@/lib/api";
import { duration, type FlowDetails, type FlowTemplate } from "@/lib/flow-details";
import { Message, PageHead } from "@/components/ui";
import ActionForm from "@/components/action-form";
import Icon from "@/components/icons";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";

export default async function NewFlowPage({ searchParams }: {
  searchParams: Promise<{ copyFrom?: string | string[] }>;
}) {
  const { copyFrom } = await searchParams;
  let source: FlowDetails | undefined;
  let templates: FlowTemplate[] = [];
  try {
    if ((await getAccess()).role === "viewer") return <Message title="Görüntüleme yetkisi" description="Yeni akışı proje sahibi ya da operatör oluşturabilir." href="/flows" action="Akışlara dön" />;
  } catch (e) { return <Message title="Erişim doğrulanamadı" description={apiMessage(e)} />; }
  if (copyFrom === undefined) { try { templates = await getFlowCatalog(); } catch { /* the gallery is optional */ } }
  if (copyFrom !== undefined) {
    if (typeof copyFrom !== "string" || !/^[a-z][a-z0-9-]{0,63}$/.test(copyFrom)) notFound();
    try { source = await getFlow(copyFrom); }
    catch (error) {
      if (error instanceof ApiError && error.status === 404) notFound();
      return <Message title="Kaynak akış yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
    }
  }
  return <>
    <PageHead crumb={{ href: "/flows", label: "Akışlar" }} icon="plus" title={source ? "Kopyadan yeni akış" : "Yeni akış"}
      lede="İzlenecek adımları gerçekleşme sırasıyla ve her adım için tanınan süreyi tanımlayın." />
    {source && <p className="callout" style={{ marginBottom: 20 }}><strong>{source.name}</strong> akışının adımları ve süreleri kopyalandı. Yeni bir akış kodu verin; kaydettiğinizde ayrı bir akış oluşur, kaynak akış değişmez.</p>}
    {templates.length > 0 && <section className="surface" style={{ marginBottom: 20 }}>
      <div className="surface-head"><div><h2><Icon name="grid" size={15} /> Sektör şablonundan başlayın</h2><p>Adımlar, süreler ve çalışma talimatları hazır gelir; yayınlanan 1. sürümü sonra bir taslakla kendi sürelerinize uyarlarsınız.</p></div><span className="count">{templates.length} şablon</span></div>
      <div className="template-grid">
        {templates.map(t => <article key={t.code} className="template-card">
          <p className="template-sector">{t.sector}</p>
          <h3>{t.name}</h3>
          <p className="template-promise">{t.promise}</p>
          <p className="help">{t.description}</p>
          <ol className="template-steps">{[...t.steps].sort((a, b) => a.position - b.position).map(st => <li key={st.code}>
            <span>{st.name}</span><span className="muted">{st.kind === "optional" ? "isteğe bağlı" : st.kind === "conditional" ? `yalnızca ${(st.groups ?? []).join(", ")}` : st.maxDelaySeconds === null ? "başlangıç" : duration(st.maxDelaySeconds)}</span>
          </li>)}</ol>
          <p className="template-meta">{t.stepCount} adım, {t.stageCount} aşama{t.groups.length > 0 && `; gruplar: ${t.groups.join(", ")}`}</p>
          <ActionForm action={adminAction} label="Bu şablonla oluştur" compact busy="Oluşturuluyor…">
            <input type="hidden" name="op" value="template-create" /><input type="hidden" name="template" value={t.code} />
            <input name="code" className="chip-input" aria-label="Akış kodu" defaultValue={t.code} maxLength={64} autoCapitalize="none" spellCheck={false} />
          </ActionForm>
        </article>)}
      </div>
    </section>}
    {templates.length > 0 && <p className="legend" style={{ margin: "0 0 10px" }}>Ya da adımları kendiniz tanımlayın</p>}
    <FlowForm key={source?.id ?? "new"} initialFlow={source} />
  </>;
}
