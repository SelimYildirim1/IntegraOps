import { notFound } from "next/navigation";
import { ApiError, apiMessage, getAccess, getCalendars, getFlow, getFlowVersion } from "@/lib/api";
import { Message, PageHead } from "@/components/ui";
import VersionEditor from "@/components/version-editor";

export const dynamic = "force-dynamic";

export default async function DraftPage({ params }: { params: Promise<{ flow: string }> }) {
  const { flow } = await params;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow)) notFound();
  try {
    if ((await getAccess()).role === "viewer") return <Message title="Görüntüleme yetkisi" description="Taslağı proje sahibi ya da operatör düzenler." href={`/flows/view/${flow}`} action="Akışa dön" />;
    const overview = await getFlow(flow);
    const draft = overview.versions.find(v => v.state === "draft");
    if (!draft) return <Message title="Taslak yok" description="Bu akışın düzenlenecek taslağı yok. Akış sayfasından yeni sürüm taslağı oluşturun." href={`/flows/view/${flow}`} action="Akışa dön" />;
    const [version, calendars] = await Promise.all([getFlowVersion(flow, draft.version), getCalendars()]);
    return <>
      <PageHead crumb={{ href: `/flows/view/${flow}`, label: overview.name }} title={`${draft.version}. sürüm taslağı`}
        lede="Adımları, süreleri, grup sürelerini ve takvimi düzenleyin. Yayınlayana kadar hiçbir işlem etkilenmez." />
      <VersionEditor key={version.version} draft={version} calendars={calendars} />
    </>;
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="Taslak yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
  }
}
