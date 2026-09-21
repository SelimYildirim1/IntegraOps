import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import { duration, type FlowDetails } from "@/lib/flow-details";
import { Message, PageHead } from "@/components/ui";
import FlowTest from "@/components/flow-test";

export const dynamic = "force-dynamic";

export default async function FlowDetailPage({ params }: { params: Promise<{ flow: string }> }) {
  const { flow } = await params;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow)) notFound();
  let item: FlowDetails;
  let canWrite = false;
  try { item = await getFlow(flow); canWrite = (await getAccess()).role !== "viewer"; }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="Akış yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
  }
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  return <>
    <PageHead crumb={{ href: "/flows", label: "Akışlar" }} title={item.name} lede={<>Akış kodu <span className="mono">{item.code}</span>. Kaynak sistemler bildirimlerinde bu kodu kullanır.</>}>
      <Link className="button secondary" href={`/?${new URLSearchParams({ flow: item.code })}`} prefetch={false}>İşlemleri göster</Link>
      {canWrite && <Link className="button" href={`/flows/new?${new URLSearchParams({ copyFrom: item.code })}`} prefetch={false}>Kopyasından yeni akış</Link>}
    </PageHead>
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><div><h2>Adımlar ve süreler</h2><p>Her süre bir önceki adımın gerçekleşme anından başlar.</p></div><span className="count">{steps.length} adım</span></div>
        <div className="surface-body">
          <ol className="rail">{steps.map((step, index) => <li key={step.code}>
            <div className="mark"><span className="dot">{index + 1}</span><span className="line" /></div>
            <div className="step">
              <div className="step-head"><h3>{step.name}</h3></div>
              <div className="step-meta"><span className="mono">{step.code}</span></div>
              <span className="gap">{index === 0 ? "Başlangıç adımı, süresi ölçülmez" : `${steps[index - 1].name} tamamlandıktan sonra ${duration(step.maxDelaySeconds)} içinde beklenir`}</span>
            </div>
          </li>)}</ol>
        </div>
      </section>
      <div className="stack">
        <section className="surface">
          <div className="surface-head"><h2>Süreleri değiştirmek</h2></div>
          <div className="surface-body">
            <p className="help">Kaydedilmiş bir akışın süreleri değiştirilmez; devam eden işlemlerin kuralları böylece sabit kalır. Yeni süreler için bu akışın kopyasından yeni bir akış oluşturun ve bildirim gönderen sistemlerde yeni kodu kullanın.</p>
          </div>
        </section>
        <section className="surface">
          <div className="surface-head"><h2>Sistem bağlantısı</h2></div>
          <div className="surface-body">
            <p className="help">Sistemler <code>POST /api/v1/events</code> adresine, <code>X-API-Key</code> başlığında bildirim yetkili proje anahtarıyla bildirim gönderir.</p>
            <p className="help">Aynı işleme ait adımlarda <code>transactionId</code> aynı kalır; her bildirimin <code>eventId</code> değeri farklıdır. Bir bildirim yeniden gönderilirken kimliği ve içeriği korunur.</p>
          </div>
        </section>
      </div>
    </div>
    {canWrite && <FlowTest key={item.id} flow={item} />}
  </>;
}
