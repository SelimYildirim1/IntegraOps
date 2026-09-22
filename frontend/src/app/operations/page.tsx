import { api, apiMessage } from "@/lib/api";
import type { AgentInfo, Operations } from "@/lib/pilot-types";
import { Message, number, PageHead, Time } from "@/components/ui";
import RefreshControls from "@/components/refresh-controls";

export const dynamic = "force-dynamic";

const actionNames: Record<string, string> = {
  "member.owner": "Kullanıcı proje sahibi yapıldı", "member.operator": "Kullanıcı operatör yapıldı",
  "member.viewer": "Kullanıcı izleyici yapıldı", "member.remove": "Kullanıcı projeden çıkarıldı",
  "key.create": "Anahtar oluşturuldu", "key.revoke": "Anahtar iptal edildi",
  "channel.create": "Kanal oluşturuldu", "channel.enable": "Kanal etkinleştirildi", "channel.disable": "Kanal kapatıldı",
  "alarm.acknowledge": "Alarm görüldü olarak işaretlendi", "delivery.retry": "Gönderim yeniden denendi",
  "project.create": "Proje oluşturuldu", "owner.bootstrap": "İlk proje sahibi kuruldu",
  "member.invite": "Kullanıcı davet edildi", "alarm.assign": "Alarm sorumlusu atandı", "alarm.unassign": "Alarm sorumlusu kaldırıldı",
  "alarm.note": "Alarma not eklendi", "retention.update": "Saklama kuralları güncellendi",
  "export.transactions": "İşlem listesi dışa aktarıldı", "export.alarms": "Alarm listesi dışa aktarıldı",
  "flow.draft": "Akış taslağı oluşturuldu", "flow.draft.update": "Akış taslağı düzenlendi", "flow.draft.delete": "Akış taslağı silindi",
  "flow.publish": "Akış sürümü yayınlandı", "flow.rename": "Akış adı değiştirildi",
  "calendar.create": "Takvim oluşturuldu", "calendar.update": "Takvim güncellendi", "calendar.delete": "Takvim silindi",
  "reconciliation.run": "Mutabakat koşusu kaydedildi", "digest.settings": "Özet ayarları güncellendi", "digest.send": "Rapor özeti gönderildi",
  "connection.create": "Bağlantı oluşturuldu", "connection.update": "Bağlantı güncellendi", "connection.delete": "Bağlantı silindi", "key.rotate": "Anahtar yenilendi",
};

export default async function OperationsPage() {
  let result: Operations; let agents: AgentInfo[];
  try { [result, agents] = await Promise.all([api<Operations>("/operations"), api<AgentInfo[]>("/agents")]); }
  catch (e) { return <Message title="Sistem görünümü açılamadı" description={apiMessage(e)} />; }
  const c = result.counts;
  const recent = Boolean(c.worker_seen_at) && Date.parse(c.checked_at) - Date.parse(c.worker_seen_at as string) < 20000;
  const stats: [string, number, boolean][] = [
    ["Kuyruğa aktarılacak olay", c.outbox_pending, c.outbox_pending > 0 && Boolean(c.oldest_outbox_at) && Date.parse(c.checked_at) - Date.parse(c.oldest_outbox_at as string) > 60000],
    ["İşlenmeyi bekleyen bildirim", c.events_pending, false],
    ["Gönderim bekleyen alarm bildirimi", c.notifications_pending, false],
    ["Denemesi duran bildirim", c.notifications_dead, c.notifications_dead > 0],
  ];
  return <>
    <PageHead title="Sistem" lede="Seçili projenin bekleyen işleri, worker yaşam sinyali ve yönetim işlemlerinin geçmişi.">
      <RefreshControls />
    </PageHead>
    <div className="stat-grid">
      {stats.map(([name, value, attention]) => <div className={`stat ${attention ? "attention" : ""}`} key={name}><p>{name}</p><strong>{number(value)}</strong></div>)}
    </div>
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Worker</h2></div>
        <div className="surface-body">
          <div className="heartbeat"><span className={`lamp ${c.worker_seen_at ? (recent ? "on" : "off") : ""}`} />
            <strong>{recent ? "Yaşam sinyali güncel" : c.worker_seen_at ? "Yaşam sinyali gecikmiş" : "Henüz yaşam sinyali yok"}</strong></div>
          <dl className="facts">
            <div><dt>Son sinyal</dt><dd className="time"><Time value={c.worker_seen_at} /></dd></div>
            <div><dt>En eski bekleyen aktarım</dt><dd className="time"><Time value={c.oldest_outbox_at} /></dd></div>
            <div><dt>Kontrol zamanı</dt><dd className="time"><Time value={c.checked_at} /></dd></div>
          </dl>
          <p className="help">Yaşam sinyali worker sürecinin çalıştığını gösterir; kuyruk bağlantısının sağlıklı olduğunu tek başına kanıtlamaz. Bekleyen sayılar artıyorsa worker ve RabbitMQ günlüklerine bakın.</p>
        </div>
      </section>
      <section className="surface">
        <div className="surface-head"><h2>Sayılar ne anlama geliyor</h2></div>
        <div className="surface-body">
          <p className="help"><strong>Kuyruğa aktarılacak olay:</strong> API'nin kabul edip henüz kuyruğa yazmadığı bildirimler. Bir dakikadan uzun süre bekleyen varsa dikkat gerekir.</p>
          <p className="help"><strong>İşlenmeyi bekleyen bildirim:</strong> API'nin kabul ettiği ve worker'ın henüz işlemediği bütün olaylar. Henüz kuyruğa aktarılmamış olaylar da bu sayıya dahildir; iki sayıyı toplamayın.</p>
          <p className="help"><strong>Gönderim bekleyen alarm bildirimi:</strong> E-posta ya da webhook için sırada bekleyen alarm iletileri.</p>
          <p className="help"><strong>Denemesi duran bildirim:</strong> Deneme sınırına ulaşmış veya kalıcı bir ret nedeniyle durmuş iletiler. Hata nedenini giderdikten sonra alarm detayından yeniden deneme başlatılabilir.</p>
        </div>
      </section>
    </div>
    <section className="surface">
      <div className="surface-head"><div><h2>Aktarım ajanları</h2><p>Kaynak sistemlerin yanında çalışan ajanlar 30 saniyede bir sürüm, bekleyen kayıt ve son hata bilgisini bildirir. 2 dakikadır sinyal vermeyen ajan gecikmiş sayılır.</p></div>
        <span className="count">{agents.length} ajan</span></div>
      <div className="table-wrap"><table><thead><tr><th>Ajan</th><th>Sürüm</th><th>Kaynak</th><th className="right">Bekleyen</th><th className="right">Gönderilen</th><th className="right">Duran</th><th>Son başarı</th><th>Son sinyal</th><th>Durum</th></tr></thead><tbody>
        {agents.map(a => <tr key={a.name}><td>{a.name}<small>{a.host}</small></td><td className="mono">{a.version}</td><td>{a.source_kind}{typeof a.config.table === "string" && <small className="mono">{a.config.table}</small>}{typeof a.config.directory === "string" && <small className="mono">{a.config.directory}</small>}</td>
          <td className="right num">{number(a.pending)}</td><td className="right num">{number(a.sent_total)}</td><td className="right num">{number(a.dead_total)}</td>
          <td className="nowrap"><Time value={a.last_success_at} /></td><td className="nowrap"><Time value={a.seen_at} /></td>
          <td>{a.stale ? <span className="badge bad">Sinyal yok</span> : a.pending > 0 && a.last_error ? <span className="badge warn">Bekliyor</span> : <span className="badge good">Çalışıyor</span>}{a.last_error && <small className="mono">{a.last_error}</small>}</td></tr>)}
      </tbody></table></div>
      {agents.length === 0 && <div className="empty"><h3>Henüz ajan bağlanmadı</h3><p>Bir kaynak sistemin yanında integraops-agent çalıştığında burada görünür.</p></div>}
    </section>
    <section className="surface">
      <div className="surface-head"><div><h2>Yönetim geçmişi</h2><p>Son 100 yönetim işlemi.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Kullanıcı</th><th>İşlem</th><th>Kayıt</th></tr></thead><tbody>
        {result.audit.map((a, i) => <tr key={i}><td className="nowrap"><Time value={a.created_at} /></td><td>{a.actor ?? "Sistem"}</td>
          <td>{actionNames[a.action] ?? a.action}<small className="mono">{a.action}</small></td><td><code>{a.target}</code></td></tr>)}
      </tbody></table></div>
      {result.audit.length === 0 && <div className="empty"><h3>Henüz yönetim işlemi yok</h3><p>Kullanıcı, anahtar ve kanal değişiklikleri burada listelenir.</p></div>}
    </section>
  </>;
}
