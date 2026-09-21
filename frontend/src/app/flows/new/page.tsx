import FlowForm from "@/components/flow-form";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import type { FlowDetails } from "@/lib/flow-details";
import { Message, PageHead } from "@/components/ui";

export const dynamic = "force-dynamic";

export default async function NewFlowPage({ searchParams }: {
  searchParams: Promise<{ copyFrom?: string | string[] }>;
}) {
  const { copyFrom } = await searchParams;
  let source: FlowDetails | undefined;
  try {
    if ((await getAccess()).role === "viewer") return <Message title="Görüntüleme yetkisi" description="Yeni akışı proje sahibi ya da operatör oluşturabilir." href="/flows" action="Akışlara dön" />;
  } catch (e) { return <Message title="Erişim doğrulanamadı" description={apiMessage(e)} />; }
  if (copyFrom !== undefined) {
    if (typeof copyFrom !== "string" || !/^[a-z][a-z0-9-]{0,63}$/.test(copyFrom)) notFound();
    try { source = await getFlow(copyFrom); }
    catch (error) {
      if (error instanceof ApiError && error.status === 404) notFound();
      return <Message title="Kaynak akış yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
    }
  }
  return <>
    <PageHead crumb={{ href: "/flows", label: "Akışlar" }} title={source ? "Kopyadan yeni akış" : "Yeni akış"}
      lede="İzlenecek adımları gerçekleşme sırasıyla ve her adım için tanınan süreyi tanımlayın." />
    {source && <p className="callout" style={{ marginBottom: 20 }}><strong>{source.name}</strong> akışının adımları ve süreleri kopyalandı. Yeni bir akış kodu verin; kaydettiğinizde ayrı bir akış oluşur, kaynak akış değişmez.</p>}
    <FlowForm key={source?.id ?? "new"} initialFlow={source} />
  </>;
}
