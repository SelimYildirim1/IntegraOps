import Link from "next/link";
import { apiMessage, getFlows, getAccess } from "@/lib/api";
import type { Flow } from "@/lib/types";
import { Message, number, PageHead } from "@/components/ui";

export const dynamic = "force-dynamic";

export default async function FlowsPage() {
  let flows: Flow[];
  let canWrite = false;
  try { flows = await getFlows(); canWrite = (await getAccess()).role !== "viewer"; }
  catch (error) { return <Message title="Akışlar yüklenemedi" description={apiMessage(error)} />; }
  return <>
    <PageHead title="Akışlar" lede="İzlediğiniz süreçler: her akış, sırayla beklenen adımlardan ve adımlar için tanınan sürelerden oluşur.">
      {canWrite && <Link className="button" href="/flows/new" prefetch={false}>Yeni akış</Link>}
    </PageHead>
    <section className="surface">
      <div className="surface-head"><h2>Tanımlı akışlar</h2><span className="count">{number(flows.length)} akış</span></div>
      <div className="table-wrap"><table><thead><tr><th>Akış</th><th>Kod</th><th className="right">Adım</th><th></th></tr></thead>
        <tbody>{flows.map(flow => <tr key={flow.id}>
          <td><Link className="ident" style={{ fontFamily: "var(--font-sans)", fontSize: 14 }} href={`/flows/view/${encodeURIComponent(flow.code)}`} prefetch={false}>{flow.name}</Link></td>
          <td className="mono">{flow.code}</td><td className="right num">{flow.stepCount}</td>
          <td><div className="cell-actions"><Link className="button quiet small" href={`/?${new URLSearchParams({ flow: flow.code })}`} prefetch={false}>İşlemleri göster</Link></div></td>
        </tr>)}</tbody>
      </table></div>
      {flows.length === 0 && <div className="empty"><h3>Henüz akış tanımlanmamış</h3><p>İzlenecek adımları belirlemek için bir akış oluşturun.</p>
        {canWrite && <Link className="button" href="/flows/new" prefetch={false}>Yeni akış</Link>}</div>}
    </section>
  </>;
}
