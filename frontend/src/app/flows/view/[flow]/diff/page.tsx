import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getFlowDiff } from "@/lib/api";
import { Empty, Message, PageHead } from "@/components/ui";

export const dynamic = "force-dynamic";
const kinds: Record<string, string> = {
  added: "Adım eklendi", removed: "Adım kaldırıldı", moved: "Sıra değişti", renamed: "Ad değişti", allowance: "Süre değişti",
  override_added: "Grup süresi eklendi", override_removed: "Grup süresi kaldırıldı", override: "Grup süresi değişti", calendar: "Takvim değişti",
  stage: "Aşama değişti", kind: "Adım türü değişti", groups: "Koşul grupları değişti", instructions: "Çalışma talimatı değişti", link: "Kaynak bağlantısı değişti",
};

export default async function DiffPage({ params, searchParams }: { params: Promise<{ flow: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { flow } = await params; const q = await searchParams;
  const from = Number(q.from), to = Number(q.to);
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow) || !Number.isInteger(from) || !Number.isInteger(to) || from < 1 || to < 1) notFound();
  try {
    const [overview, diff] = await Promise.all([getFlow(flow), getFlowDiff(flow, from, to)]);
    return <>
      <PageHead crumb={{ href: `/flows/view/${flow}`, label: overview.name }} icon="workflow" title={`${from}. ve ${to}. sürüm farkı`}
        lede="Değişiklikler hedef sürümün adım sırasıyla listelenir.">
        <Link className="button secondary" href={`/flows/view/${flow}`} prefetch={false}>Akışa dön</Link>
      </PageHead>
      <section className="surface">
        <div className="table-wrap"><table><thead><tr><th>Değişiklik</th><th>Adım</th><th>Ayrıntı</th></tr></thead><tbody>
          {diff.changes.map((c, i) => <tr key={i}><td>{kinds[c.kind] ?? c.kind}</td><td className="mono">{c.code}</td><td>{c.detail}</td></tr>)}
        </tbody></table></div>
        {diff.changes.length === 0 && <Empty icon="check" title="Fark yok">İki sürümün adımları, süreleri, talimatları ve takvimi aynı.</Empty>}
      </section>
    </>;
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="Fark yüklenemedi" description={apiMessage(error)} href="/flows" action="Akışlara dön" />;
  }
}
