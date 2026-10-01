import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import { duration, type FlowDetails } from "@/lib/flow-details";
import { Explain, Message, number, PageHead, Time } from "@/components/ui";
import { adminAction } from "@/app/admin-actions";
import Icon from "@/components/icons";
import FlowTest from "@/components/flow-test";
import ActionForm from "@/components/action-form";
import { createDraftAction } from "@/app/flows/versions-actions";
import ThresholdAdvicePanel from "@/components/threshold-advice";
import type { AiStatus, AiThresholdAnalysis, ThresholdAdvice } from "@/lib/pilot-types";
import { api } from "@/lib/api";

export const dynamic = "force-dynamic";

export default async function FlowDetailPage({ params, searchParams }: { params: Promise<{ flow: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { flow } = await params;
  const query = await searchParams;
  const justPublished = query.published;
  const fromTemplate = query.template === "1";
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow)) notFound();
  let item: FlowDetails;
  let canWrite = false; let owner = false;
  const days = /^(7|14|30|60|90)$/.test(String(query.days)) ? Number(query.days) : 30;
  const test = ["exclude", "include", "only"].includes(String(query.test)) ? String(query.test) : "exclude";
  let advice: ThresholdAdvice | null = null; let aiStatus: AiStatus | null = null; let thresholdAnalyses: AiThresholdAnalysis[] = [];
  try {
    item = await getFlow(flow); const role = (await getAccess()).role; canWrite = role !== "viewer"; owner = role === "owner";
    // The allowance review is optional: a failure here must not hide the flow itself.
    [advice, aiStatus, thresholdAnalyses] = await Promise.all([
      api<ThresholdAdvice>(`/flows/${flow}/threshold-advice?` + new URLSearchParams({ days: String(days), test })).catch(() => null),
      api<AiStatus>("/ai/status").catch(() => null),
      api<AiThresholdAnalysis[]>(`/flows/${flow}/ai-threshold?` + new URLSearchParams({ days: String(days), test })).catch(() => [])]);
  }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="Akış yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" icon="alert" />;
  }
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  const draft = item.versions.find(v => v.state === "draft");
  const published = item.versions.filter(v => v.state === "published");
  return <>
    <PageHead crumb={{ href: "/flows", label: "Akışlar" }} icon="workflow" title={item.name}
      lede={<>Akış kodu <span className="mono">{item.code}</span>. Etkin sürüm {item.version ?? "—"}{item.calendar ? `, takvim: ${item.calendar}` : ", 7/24"}.</>}>
      <Link className="button secondary" href={`/?${new URLSearchParams({ flow: item.code })}`} prefetch={false}><Icon name="activity" />İşlemleri göster</Link>
      {canWrite && (draft
        ? <Link className="button" href={`/flows/view/${encodeURIComponent(item.code)}/draft`} prefetch={false}><Icon name="note" />Taslağı düzenle ({draft.version}. sürüm)</Link>
        : <ActionForm action={createDraftAction} label="Yeni sürüm taslağı" compact><input type="hidden" name="flow" value={item.code} /></ActionForm>)}
      {canWrite && <Link className="button quiet" href={`/flows/new?${new URLSearchParams({ copyFrom: item.code })}`} prefetch={false}>Kopyasından yeni akış</Link>}
    </PageHead>
    {fromTemplate && <p className="callout good" style={{ marginBottom: 20 }}>
      <strong>Akış şablondan oluşturuldu.</strong> Adım kodları, süreler ve çalışma talimatları hazır. Süreleri kendi iş sürelerinize göre bir sürüm taslağında düzenleyin; kaynak sistem bağlantı şablonlarını (ör. <span className="mono">{"https://erp.sirket.com/orders/{transactionId}"}</span>) ekleyin. Paneli dolu görmek için aşağıdan örnek veri üretin.</p>}
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
              {(step.instructions || step.link) && <div className="runbook">
                {step.instructions && <p><strong>Ne yapılmalı:</strong> {step.instructions}</p>}
                {step.link && <p className="mono" style={{ fontSize: 12 }}><Icon name="open" size={12} /> {step.link}</p>}
              </div>}
            </div>
          </li>)}</ol>
        </div>
      </section>
      <div className="stack">
        <section className="surface">
          <div className="surface-head"><div><h2>Sürümler</h2><p>Yayınlanan sürüm değişmez. Yeni işlemler etkin sürümü, devam edenler başladıkları sürümü kullanır.</p></div></div>
          <ul className="version-list">
            {item.versions.map(v => <li key={v.id}>
              <div className="version-row">
                <strong>{v.version}. sürüm</strong>
                {v.state === "draft" ? <span className="badge info">Taslak</span> : v.effective ? <span className="badge good">Etkin</span>
                  : v.effective_from && Date.parse(v.effective_from) > Date.now() ? <span className="badge warn">Planlandı</span> : <span className="badge">Eski</span>}
                <span className="count">{v.transaction_count} işlem</span>
              </div>
              <div className="version-meta">
                {v.effective_from ? <Time value={v.effective_from} /> : <span className="muted">Henüz yayınlanmadı</span>}
                {v.note && <span className="muted">{v.note}</span>}
                {v.state === "published" && published.length > 1 && v.version !== published[published.length - 1].version
                  && <Link href={`/flows/view/${encodeURIComponent(item.code)}/diff?${new URLSearchParams({ from: String(v.version - 1), to: String(v.version) })}`} prefetch={false}>Önceki sürümle fark</Link>}
              </div>
            </li>)}
          </ul>
        </section>
        <section className="surface">
          <div className="surface-head"><h2>Sistem bağlantısı</h2></div>
          <Explain title="Bildirim nasıl gönderilir?">
            <p>Sistemler <code>POST /api/v1/events</code> adresine, <code>X-API-Key</code> başlığında bildirim yetkili proje anahtarıyla bildirim gönderir.</p>
            <p>Aynı işleme ait adımlarda <code>transactionId</code> aynı kalır; her bildirimin <code>eventId</code> değeri farklıdır. İsteğe bağlı <code>group</code> alanı grup sürelerini seçer ve işlemin ilk bildirimiyle sabitlenir.</p>
          </Explain>
        </section>
      </div>
    </div>
    {advice && <ThresholdAdvicePanel advice={advice} status={aiStatus} analyses={thresholdAnalyses} canAct={canWrite} days={days} test={test} />}
    {canWrite && <FlowTest key={item.id} flow={item} />}
    {canWrite && <section className="surface">
      <div className="surface-head"><div><h2>Örnek veri</h2><p>Kaynak sistem bağlanmadan paneli, alarmları ve raporları dolu görmek için gerçekçi örnek işlemler üretir. Örnek işlemler <span className="mono">DEMO-</span> ile başlar ve gerçek veri gibi görünür.</p></div>
        <span className="count">{number(item.demoCount ?? 0)} örnek işlem</span></div>
      <div className="surface-body">
        <div className="assign-row">
          <ActionForm action={adminAction} label="Örnek veri üret" compact busy="Üretiliyor…">
            <input type="hidden" name="op" value="demo-generate" /><input type="hidden" name="flow" value={item.code} />
            <select name="count" aria-label="İşlem sayısı" className="chip-input" defaultValue="30">
              <option value="10">10 işlem</option><option value="30">30 işlem</option><option value="60">60 işlem</option><option value="100">100 işlem</option></select>
            <select name="days" aria-label="Dönem" className="chip-input" defaultValue="14">
              <option value="3">son 3 gün</option><option value="7">son 7 gün</option><option value="14">son 14 gün</option><option value="30">son 30 gün</option></select>
          </ActionForm>
          {(item.demoCount ?? 0) > 0 && <ActionForm action={adminAction} label="Örnek veriyi sil" compact tone="danger" busy="Siliniyor…">
            <input type="hidden" name="op" value="demo-delete" /><input type="hidden" name="flow" value={item.code} /></ActionForm>}
        </div>
        <Explain title="Örnek veri nasıl üretilir?">
          <p>Her örnek işlem gerçek kabul yolundan geçer: yaklaşık %60 zamanında tamamlanır, bir kısmı süreyi aşar, bir kısmı bir adımda takılır ya da hata bildirir, birkaçı iptal edilir. Süresi geçen adımlar için süre aşımı alarmları, hata bildirenler için hata alarmları açılır ve etkin bildirim kanallarına gider. Oluşturma zamanı seçilen döneme yayılır; raporlar ve eğilim grafikleri dolu görünür. Gerçek kaynak bağlandığında tek düğmeyle silinir.</p>
        </Explain>
      </div>
    </section>}
    {owner && <section className="surface">
      <div className="surface-head"><div><h2>Akışı sil</h2><p>Artık izlemek istemediğiniz akışı kaldırır. Akışın sürümleri, {number(item.transactionCount ?? 0)} işlemi, bildirimleri, alarmları ve mutabakat geçmişi kalıcı olarak silinir; geri alınamaz.</p></div></div>
      <div className="surface-body">
        <ActionForm action={adminAction} label="Akışı kalıcı olarak sil" tone="danger" busy="Siliniyor…">
          <input type="hidden" name="op" value="flow-delete" /><input type="hidden" name="flow" value={item.code} />
          <div className="field"><label htmlFor="flow-delete-confirm">Onay için akış kodunu yazın: <span className="mono">{item.code}</span></label>
            <input id="flow-delete-confirm" name="confirm" required maxLength={64} autoComplete="off" spellCheck={false} /></div>
        </ActionForm>
        <Explain title="Silmeden önce">
          <p>Kaynak sistem bu akışa bildirim göndermeye devam ediyorsa önce gönderimi durdurun; silinen koda gelen bildirimler bilinmeyen akış olarak reddedilir. Kayıtları saklamak istiyorsanız silmeden önce organizasyon sayfasından dışa aktarım alın. Aynı kodla sonradan yeni bir akış oluşturabilirsiniz; eski kayıtlar geri gelmez.</p>
        </Explain>
      </div>
    </section>}
  </>;
}
