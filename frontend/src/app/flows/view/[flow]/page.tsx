import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import { duration, type FlowDetails } from "@/lib/flow-details";
import { Message, PageHead, Time } from "@/components/ui";
import FlowTest from "@/components/flow-test";
import ActionForm from "@/components/action-form";
import { createDraftAction } from "@/app/flows/versions-actions";

export const dynamic = "force-dynamic";

export default async function FlowDetailPage({ params, searchParams }: { params: Promise<{ flow: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { flow } = await params;
  const justPublished = (await searchParams).published;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow)) notFound();
  let item: FlowDetails;
  let canWrite = false;
  try { item = await getFlow(flow); canWrite = (await getAccess()).role !== "viewer"; }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="Akış yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
  }
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  const draft = item.versions.find(v => v.state === "draft");
  const published = item.versions.filter(v => v.state === "published");
  return <>
    <PageHead crumb={{ href: "/flows", label: "Akışlar" }} title={item.name}
      lede={<>Akış kodu <span className="mono">{item.code}</span>. Etkin sürüm {item.version ?? "—"}{item.calendar ? `, takvim: ${item.calendar}` : ", 7/24"}.</>}>
      <Link className="button secondary" href={`/?${new URLSearchParams({ flow: item.code })}`} prefetch={false}>İşlemleri göster</Link>
      {canWrite && (draft
        ? <Link className="button" href={`/flows/view/${encodeURIComponent(item.code)}/draft`} prefetch={false}>Taslağı düzenle ({draft.version}. sürüm)</Link>
        : <ActionForm action={createDraftAction} label="Yeni sürüm taslağı" compact><input type="hidden" name="flow" value={item.code} /></ActionForm>)}
      {canWrite && <Link className="button quiet" href={`/flows/new?${new URLSearchParams({ copyFrom: item.code })}`} prefetch={false}>Kopyasından yeni akış</Link>}
    </PageHead>
    {typeof justPublished === "string" && /^\d{1,4}$/.test(justPublished) && <p className="callout good" style={{ marginBottom: 20 }}>
      <strong>{justPublished}. sürüm yayınlandı.</strong> Etkinleşme zamanından sonra başlayan işlemler yeni sürümü kullanır; devam edenler başladıkları sürümde kalır.</p>}
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Etkin sürümün adımları</h2><p>Her süre bir önceki adımın gerçekleşme anından başlar{item.calendar ? " ve yalnızca çalışma saatlerinde işler" : ""}.</p></div><span className="count">{steps.length} adım</span></div>
        <div className="surface-body">
          <ol className="rail">{steps.map((step, index) => <li key={step.code}>
            <div className="mark"><span className="dot">{index + 1}</span><span className="line" /></div>
            <div className="step">
              <div className="step-head"><h3>{step.name}</h3></div>
              <div className="step-meta"><span className="mono">{step.code}</span>
                {step.kind && step.kind !== "required" && <span className="badge test">{step.kind === "optional" ? "İsteğe bağlı" : `Koşullu: ${(step.groups ?? []).join(", ")}`}</span>}
                {index > 0 && (step.stage ?? index) === (steps[index - 1].stage ?? index - 1) && <span className="badge info">Paralel</span>}</div>
              <span className="gap">{index === 0 ? "Başlangıç adımı, süresi ölçülmez" : step.maxDelaySeconds === null ? "Süresi ölçülmez"
                : `${(step.stage ?? index) === (steps[index - 1].stage ?? index - 1) ? "Aşama başladıktan" : `${steps[index - 1].name} tamamlandıktan`} sonra ${duration(step.maxDelaySeconds)} içinde beklenir`}</span>
              {step.overrides && Object.keys(step.overrides).length > 0 && <span className="gap">Grup süreleri: {Object.entries(step.overrides).map(([g, s]) => `${g} ${duration(s)}`).join(", ")}</span>}
            </div>
          </li>)}</ol>
        </div>
      </section>
      <div className="stack">
        <section className="surface">
          <div className="surface-head"><div><h2>Sürümler</h2><p>Yayınlanan sürüm değişmez. Yeni işlemler etkin sürümü, devam edenler başladıkları sürümü kullanır.</p></div></div>
          <div className="table-wrap"><table><thead><tr><th>Sürüm</th><th>Durum</th><th>Etkinleşme</th><th className="right">İşlem</th><th></th></tr></thead><tbody>
            {item.versions.map(v => <tr key={v.id}>
              <td>{v.version}{v.note && <small>{v.note}</small>}</td>
              <td>{v.state === "draft" ? <span className="badge info">Taslak</span> : v.effective ? <span className="badge good">Etkin</span>
                : v.effective_from && Date.parse(v.effective_from) > Date.now() ? <span className="badge warn">Planlandı</span> : <span className="badge">Eski</span>}</td>
              <td className="nowrap"><Time value={v.effective_from} /></td>
              <td className="right num">{v.transaction_count}</td>
              <td><div className="cell-actions">
                {v.state === "published" && published.length > 1 && v.version !== published[published.length - 1].version
                  && <Link className="button quiet small" href={`/flows/view/${encodeURIComponent(item.code)}/diff?${new URLSearchParams({ from: String(v.version - 1), to: String(v.version) })}`} prefetch={false}>Önceki sürümle fark</Link>}
              </div></td>
            </tr>)}
          </tbody></table></div>
        </section>
        <section className="surface">
          <div className="surface-head"><h2>Sistem bağlantısı</h2></div>
          <div className="surface-body">
            <p className="help">Sistemler <code>POST /api/v1/events</code> adresine, <code>X-API-Key</code> başlığında bildirim yetkili proje anahtarıyla bildirim gönderir.</p>
            <p className="help">Aynı işleme ait adımlarda <code>transactionId</code> aynı kalır; her bildirimin <code>eventId</code> değeri farklıdır. İsteğe bağlı <code>group</code> alanı, grup sürelerini seçer ve işlemin ilk bildirimiyle sabitlenir.</p>
          </div>
        </section>
      </div>
    </div>
    {canWrite && <FlowTest key={item.id} flow={item} />}
  </>;
}
