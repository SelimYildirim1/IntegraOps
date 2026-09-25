import Link from "next/link";
import { apiMessage, getFlows, getAccess } from "@/lib/api";
import type { Flow } from "@/lib/types";
import { Empty, Message, number, PageHead } from "@/components/ui";
import PagedTable from "@/components/paged-table";
import Icon from "@/components/icons";

export const dynamic = "force-dynamic";

export default async function FlowsPage() {
  let flows: Flow[];
  let canWrite = false;
  try { flows = await getFlows(); canWrite = (await getAccess()).role !== "viewer"; }
  catch (error) { return <Message title="Akışlar yüklenemedi" description={apiMessage(error)} icon="alert" />; }
  return <>
    <PageHead icon="workflow" title="Akışlar" lede="İzlediğiniz süreçler: sırayla beklenen adımlar ve her adım için tanınan süre.">
      {canWrite && <Link className="button" href="/flows/new" prefetch={false}><Icon name="plus" />Yeni akış</Link>}
    </PageHead>
    <section className="surface">
      <div className="surface-head"><h2>Tanımlı akışlar</h2><span className="count">{number(flows.length)} akış</span></div>
      <PagedTable unit="akış"
        head={<tr><th>Akış</th><th>Kod</th><th className="right">Adım</th><th></th></tr>}
        rows={flows.map(flow => <tr key={flow.id}>
          <td><Link className="ident" style={{ fontFamily: "var(--font-sans)", fontSize: 14 }} href={`/flows/view/${encodeURIComponent(flow.code)}`} prefetch={false}>{flow.name}</Link></td>
          <td className="mono">{flow.code}</td><td className="right num">{flow.stepCount}</td>
          <td><div className="cell-actions"><Link className="button quiet small" href={`/?${new URLSearchParams({ flow: flow.code })}`} prefetch={false}><Icon name="activity" size={14} />İşlemler</Link></div></td>
        </tr>)}
        empty={<Empty icon="workflow" title="Henüz akış tanımlanmamış"
          action={canWrite ? <Link className="button" href="/flows/new" prefetch={false}><Icon name="plus" />Yeni akış</Link> : undefined}>
          İzlenecek adımları belirlemek için bir akış oluşturun.</Empty>} />
    </section>
  </>;
}
