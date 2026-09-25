import { api, apiMessage, getAccess } from "@/lib/api";
import type { Connection, ConnectionTemplate, ProjectKey } from "@/lib/pilot-types";
import { Empty, Explain, Message, number, PageHead, Time } from "@/components/ui";
import ActionForm from "@/components/action-form";
import RefreshControls from "@/components/refresh-controls";
import PagedTable from "@/components/paged-table";
import { adminAction } from "@/app/admin-actions";
import MappingTester from "@/components/mapping-tester";

export const dynamic = "force-dynamic";
const kinds: Record<string, string> = { direct: "Doğrudan API", agent: "Aktarım ajanı", sdk: "Gönderici kütüphanesi", mapped: "Eşlemeli (şablon)" };

function Op({ value, id }: { value: string; id?: string }) {
  return <><input type="hidden" name="op" value={value} />{id && <input type="hidden" name="id" value={id} />}</>;
}

export default async function ConnectionsPage() {
  let connections: Connection[]; let catalog: ConnectionTemplate[]; let keys: ProjectKey[] = []; let owner = false;
  try {
    const access = await getAccess(); owner = access.role === "owner";
    [connections, catalog] = await Promise.all([api<Connection[]>("/connections"), api<ConnectionTemplate[]>("/connections/catalog")]);
    if (owner) keys = await api<ProjectKey[]>("/keys");
  } catch (e) { return <Message title="Bağlantılar yüklenemedi" description={apiMessage(e)} icon="alert" />; }
  const now = Date.now();
  const health = (c: Connection) => {
    const last = Math.max(Date.parse(c.last_event_at ?? "") || 0, Date.parse(c.last_ping_at ?? "") || 0, Date.parse(c.agent_seen_at ?? "") || 0);
    if (!last) return { tone: "", label: "Henüz veri yok" };
    if (c.compatible === false) return { tone: "bad", label: "Uyumsuz istemci" };
    if (now - last > 24 * 3600 * 1000) return { tone: "warn", label: "24 saattir sessiz" };
    if (c.last_error_at && Date.parse(c.last_error_at) > (Date.parse(c.last_event_at ?? "") || 0)) return { tone: "warn", label: "Son istek reddedildi" };
    return { tone: "good", label: "Çalışıyor" };
  };
  return <>
    <PageHead icon="plug" title="Bağlantılar" lede="Bu projeye olay gönderen kaynak sistemler ve son veri alışverişleri.">
      <RefreshControls />
    </PageHead>
    <section className="surface">
      <div className="surface-head"><h2>Kaynak sistemler</h2><span className="count">{connections.length} bağlantı</span></div>
      <PagedTable unit="bağlantı"
        head={<tr><th>Bağlantı</th><th>Tür</th><th>Son olay</th><th>Son ping</th><th>İstemci</th><th className="right">Olay</th><th className="right">Red</th><th>Durum</th>{owner && <th></th>}</tr>}
        rows={connections.map(c => { const h = health(c); return <tr key={c.id}>
          <td>{c.name}{c.description && <small>{c.description}</small>}</td>
          <td>{kinds[c.kind] ?? c.kind}{c.template_code && <small className="mono">{c.template_code}</small>}</td>
          <td className="nowrap"><Time value={c.last_event_at} /></td><td className="nowrap"><Time value={c.last_ping_at ?? c.agent_seen_at} /></td>
          <td>{c.client_name || c.agent_version ? <>{c.client_name ?? "integraops-agent"}<small className="mono">{c.client_version ?? c.agent_version}</small></> : <span className="muted">—</span>}</td>
          <td className="right num">{number(c.events_total)}</td><td className="right num">{number(c.rejected_total)}{c.last_error && <small>{c.last_error}</small>}</td>
          <td><span className={`badge ${h.tone}`}>{h.label}</span><small>{c.active_keys} etkin anahtar</small></td>
          {owner && <td><div className="cell-actions">{c.active_keys === 0 && <ActionForm action={adminAction} label="Sil" compact tone="danger"><Op value="connection-delete" id={c.id} /></ActionForm>}</div></td>}
        </tr>; })}
        empty={<Empty icon="plug" title="Henüz bağlantı yok">Kaynak sistemi bağlamak için aşağıdan bir bağlantı oluşturun; anahtarları ona bağlayın.</Empty>} />
    </section>

    {owner && <section className="surface">
      <div className="surface-head"><div><h2>Yeni bağlantı</h2><p>Doğrudan API, ajan ve kütüphane olay biçimini kendisi üretir; eşlemeli bağlantı ham veriyi şablonla olaya çevirir.</p></div></div>
      <div className="surface-body">
        <ActionForm action={adminAction} label="Bağlantı oluştur"><Op value="connection-create" />
          <div className="form-grid">
            <div className="field"><label htmlFor="cn-name">Ad</label><input id="cn-name" name="name" required maxLength={120} placeholder="ERP üretim" /><span className="hint">Ajan kullanılıyorsa ajanın NAME değeriyle aynı olmalı.</span></div>
            <div className="field"><label htmlFor="cn-kind">Tür</label><select id="cn-kind" name="kind" defaultValue="direct">
              {Object.entries(kinds).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
            <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor="cn-desc">Açıklama</label><input id="cn-desc" name="description" maxLength={500} /></div>
            <div className="field"><label htmlFor="cn-tc">Şablon başlangıcı (eşlemeli tür için)</label><select id="cn-tc" name="templateCode" defaultValue="">
              <option value="">Boş</option>{catalog.map(t => <option key={t.code} value={t.code}>{t.name}</option>)}</select></div>
            <div className="field" style={{ gridColumn: "1 / -1" }}><label htmlFor="cn-template">Şablon (JSON, yalnızca eşlemeli tür)</label>
              <textarea id="cn-template" name="template" rows={5} placeholder={JSON.stringify(catalog[0]?.template ?? {}, null, 1)} /></div>
          </div>
        </ActionForm>
        <details className="details"><summary>Hazır şablonlar</summary>
          {catalog.map(t => <div key={t.code} className="calendar-card" style={{ marginTop: 10 }}><strong>{t.name}</strong> <span className="mono">{t.code}</span><p className="help">{t.description}</p>
            <pre className="event-example">{JSON.stringify(t.template, null, 1)}</pre></div>)}
        </details>
      </div>
    </section>}

    {(owner || connections.some(c => c.kind === "mapped")) && <MappingTester catalog={catalog} />}

    {owner && <section className="surface">
      <div className="surface-head"><div><h2>Anahtarlar ve yenileme</h2><p>Yenileme yeni anahtar üretir; eski anahtar geçiş süresi boyunca çalışmaya devam eder.</p></div></div>
      <PagedTable unit="anahtar"
        head={<tr><th>Anahtar</th><th>Bağlantı</th><th>Son kullanım</th><th>Durum</th><th></th></tr>}
        rows={keys.filter(k => !k.revoked_at).map(k => <tr key={k.id}><td>{k.name}<small><code>{k.key_prefix}…</code>, {k.scope}</small></td><td>{k.connection ?? <span className="muted">—</span>}</td>
          <td className="nowrap"><Time value={k.last_used_at} /></td>
          <td>{k.rotated_to ? <span className="badge warn">Yenilendi, {k.expires_at ? <>bitiş <Time value={k.expires_at} /></> : "geçişte"}</span> : <span className="badge good">Etkin</span>}</td>
          <td><div className="cell-actions">{!k.rotated_to && <ActionForm action={adminAction} label="Yenile" compact tone="secondary"><Op value="key-rotate" id={k.id} />
            <input name="graceHours" type="number" min={0} max={720} defaultValue={24} className="chip-input" style={{ minWidth: 80 }} aria-label="Geçiş süresi (saat)" /></ActionForm>}</div></td></tr>)}
        empty={<Empty icon="key" title="Etkin anahtar yok" />} />
      <div className="surface-body">
        <h3>Bağlantıya bağlı yeni anahtar</h3>
        <ActionForm action={adminAction} label="Anahtar oluştur"><Op value="key-create" />
          <div className="form-grid">
            <div className="field"><label htmlFor="ck-name">Anahtar adı</label><input id="ck-name" name="name" required maxLength={120} /></div>
            <div className="field"><label htmlFor="ck-scope">Yetki</label><select id="ck-scope" name="scope" defaultValue="ingest"><option value="ingest">Bildirim gönderir</option><option value="read">Takip verilerini okur</option></select></div>
            <div className="field"><label htmlFor="ck-conn">Bağlantı</label><select id="ck-conn" name="connectionId" defaultValue="">
              <option value="">Bağlantısız</option>{connections.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></div>
          </div>
        </ActionForm>
        <Explain title="Anahtar ve bağlantı ilişkisi">
          <p>Anahtarı bağlantıya bağlamak kullanım ve sürüm bilgisini o bağlantıya yazar; böylece hangi sistemin ne zaman veri gönderdiği bu sayfada izlenir.</p>
        </Explain>
      </div>
    </section>}
  </>;
}
