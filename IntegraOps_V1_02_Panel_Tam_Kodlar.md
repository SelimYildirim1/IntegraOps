# IntegraOps V1 — Panel tam kodlar

Bu bölüm mevcut paneli kullanıcı oturumu ve proje seçimiyle çalıştırır. Mevcut işlem/akış sayfaları dahil bütün kaynak dosyaları verilmiştir. API anahtarını NEXT_PUBLIC değişkeni yapma. `.env.local` içindeki `INTEGRAOPS_API_URL` sunucu adresidir; kurulumu 00 numaralı rehberde yap.

**19 yeni/değişen dosya; 18 referans dosya.**

Yollar proje köküne göredir. YENİ: dosyayı oluştur. DEĞİŞTİR: dosyanın tam içeriğini aşağıdakiyle değiştir. REFERANS: önceki sürümle aynı, sende varsa tekrar kopyalaman gerekmez.

## Dosya listesi

| İşlem | Dosya | Görevi |
|---|---|---|
| YENİ | `frontend/src/app/account/page.tsx` | Mevcut parola ile yeni parola belirleme. |
| YENİ | `frontend/src/app/account-actions.ts` | Giriş, çıkış, proje seçimi/oluşturma ve parola değiştirme form işlemleri. |
| YENİ | `frontend/src/app/admin-actions.ts` | Yalnızca tanımlı yönetim işlemlerini API’ye ileten sunucu eylemi. |
| YENİ | `frontend/src/app/alarms/[id]/page.tsx` | Alarm koşulu, görüldü işareti, olay geçmişi ve gönderim denemeleri. |
| YENİ | `frontend/src/app/alarms/page.tsx` | Açık/kapalı alarm listesi, akış filtresi ve sayfalama. |
| DEĞİŞTİR | `frontend/src/app/flows/new/page.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `frontend/src/app/flows/page.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `frontend/src/app/flows/view/[flow]/page.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `frontend/src/app/globals.css` | Mevcut tasarım ve yeni formlar/alarm ekranları için stiller. |
| DEĞİŞTİR | `frontend/src/app/layout.tsx` | Ortak başlık, seçili proje adı, hesap bağlantıları ve sayfa düzeni. |
| YENİ | `frontend/src/app/login/page.tsx` | Kullanıcı giriş ekranı. |
| YENİ | `frontend/src/app/operations/page.tsx` | Kuyruk sayıları, yaşam sinyali ve yönetim geçmişi. |
| YENİ | `frontend/src/app/projects/page.tsx` | Proje seçme ve yeni proje oluşturma. |
| YENİ | `frontend/src/app/settings/page.tsx` | Üyeler, roller, API anahtarları ve bildirim kanalları. |
| YENİ | `frontend/src/components/action-form.tsx` | Form gönderimi sırasında bekleme, sonuç ve bir kez gösterilen sır alanı. |
| DEĞİŞTİR | `frontend/src/components/panel-toolbar.tsx` | İşlemler, Akışlar, Alarmlar, Ayarlar ve Sistem sekmeleri. |
| DEĞİŞTİR | `frontend/src/lib/api.ts` | Panel sunucusundan API’ye kullanıcı oturumu ve seçili proje ile istek gönderir. |
| YENİ | `frontend/src/lib/pilot-types.ts` | Yeni ekranların beklediği yanıt modelleri ve Türkçe etiketler. |
| YENİ | `frontend/src/proxy.ts` | Girişsiz panel gezinmesini giriş ekranına yönlendirir; asıl yetki kontrolü API’dedir. |
| REFERANS | `frontend/src/app/error.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/flows/new/actions.ts` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/flows/not-found.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/flows/test-actions.ts` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/loading.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/not-found.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/page.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/app/transactions/[flow]/[transactionId]/page.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/components/flow-form.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/components/flow-test.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/components/refresh-controls.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/components/ui.tsx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/lib/flow-details.ts` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/lib/flow-form.ts` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/src/lib/types.ts` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/package.json` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/tsconfig.json` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `frontend/.gitignore` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |

## 1. frontend/src/app/account/page.tsx

**YENİ** — Mevcut parola ile yeni parola belirleme.

```tsx
import ActionForm from "@/components/action-form";
import { passwordAction } from "@/app/account-actions";

export default function AccountPage() {
  return <section className="panel auth-panel"><h1>Parolamı değiştir</h1><p>Değişiklikten sonra bütün oturumlarınız kapanır.</p>
    <ActionForm action={passwordAction} label="Parolayı değiştir">
      <label>Mevcut parola<input type="password" name="currentPassword" required maxLength={128} autoComplete="current-password" /></label>
      <label>Yeni parola<input type="password" name="newPassword" required minLength={12} maxLength={128} autoComplete="new-password" /></label>
    </ActionForm></section>;
}
```

## 2. frontend/src/app/account-actions.ts

**YENİ** — Giriş, çıkış, proje seçimi/oluşturma ve parola değiştirme form işlemleri.

```typescript
"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { api, apiMessage, ApiError, getProjects, login } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

const cookieOptions = () => ({ httpOnly: true, sameSite: "lax" as const,
  secure: process.env.PANEL_COOKIE_SECURE === "true", path: "/", maxAge: 28800 });

export async function loginAction(_state: ActionState, data: FormData): Promise<ActionState> {
  const email = String(data.get("email") ?? ""); const password = String(data.get("password") ?? "");
  if (email.length > 254 || password.length > 128) return { message: "Giriş bilgilerini kontrol edin." };
  let result;
  try { result = await login(email, password); }
  catch (e) { return { message: e instanceof ApiError && e.status === 401 ? "E-posta veya parola doğrulanamadı." : apiMessage(e) }; }
  const jar = await cookies();
  jar.set("io_session", result.token, cookieOptions()); jar.delete("io_project");
  redirect("/projects");
}
export async function logoutAction() {
  try { await api("/me/logout", "POST", undefined, false); } catch { /* Clear this browser even if the service is unavailable. */ }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project");
  redirect("/login");
}
export async function switchProject(data: FormData) {
  const id = String(data.get("project") ?? ""); const projects = await getProjects();
  if (!projects.some(p => p.id === id)) throw new Error("Proje erişimi bulunamadı.");
  (await cookies()).set("io_project", id, cookieOptions());
  revalidatePath("/", "layout"); redirect("/");
}
export async function createProjectAction(_state: ActionState, data: FormData): Promise<ActionState> {
  try {
    await api("/me/projects", "POST", { name: String(data.get("name") ?? "") }, false);
    revalidatePath("/projects"); return { ok: true, message: "Proje oluşturuldu. Listeden açabilirsiniz." };
  } catch (e) { return { message: apiMessage(e) }; }
}
export async function passwordAction(_state: ActionState, data: FormData): Promise<ActionState> {
  try {
    await api("/me/password", "POST", { currentPassword: String(data.get("currentPassword") ?? ""),
      newPassword: String(data.get("newPassword") ?? "") }, false);
  } catch (e) { return { message: apiMessage(e) }; }
  const jar = await cookies(); jar.delete("io_session"); jar.delete("io_project"); redirect("/login");
}
```

## 3. frontend/src/app/admin-actions.ts

**YENİ** — Yalnızca tanımlı yönetim işlemlerini API’ye ileten sunucu eylemi.

```typescript
"use server";

import { revalidatePath } from "next/cache";
import { api, apiMessage } from "@/lib/api";
import type { ActionState } from "@/lib/pilot-types";

export async function adminAction(_previous: ActionState, data: FormData): Promise<ActionState> {
  const field = (name: string) => String(data.get(name) ?? "");
  const op = field("op"); const id = field("id");
  const withId = ["member-remove", "key-revoke", "channel-toggle", "ack", "retry"].includes(op);
  if (withId && !/^[0-9a-f-]{36}$/i.test(id)) return { message: "Kayıt kimliği geçersiz." };
  let result: { key?: string; secret?: string } | undefined;
  try {
    switch (op) {
      case "member-save": await api("/members", "POST", { email: field("email"), name: field("name"), role: field("role"), password: field("password") || null }); break;
      case "member-remove": await api(`/members/${id}`, "DELETE"); break;
      case "key-create": result = await api("/keys", "POST", { name: field("name"), scope: field("scope") }); break;
      case "key-revoke": await api(`/keys/${id}`, "DELETE"); break;
      case "channel-create": result = await api("/channels", "POST", { name: field("name"), kind: field("kind"), target: field("target"), secret: field("secret") || null }); break;
      case "channel-toggle": await api(`/channels/${id}`, "PUT", { enabled: field("enabled") === "true" }); break;
      case "ack": await api(`/alarms/${id}/acknowledge`, "POST"); break;
      case "retry": await api(`/deliveries/${id}/retry`, "POST"); break;
      default: return { message: "İşlem tanınmadı." };
    }
  } catch (e) { return { message: apiMessage(e) }; }
  revalidatePath("/settings"); revalidatePath("/alarms", "layout"); revalidatePath("/operations");
  return { ok: true, message: "İşlem tamamlandı.", secret: result?.key || result?.secret };
}
```

## 4. frontend/src/app/alarms/[id]/page.tsx

**YENİ** — Alarm koşulu, görüldü işareti, olay geçmişi ve gönderim denemeleri.

```tsx
import Link from "next/link";
import { api, apiMessage, getAccess } from "@/lib/api";
import type { AlarmDetail } from "@/lib/pilot-types";
import { eventNames, deliveryNames } from "@/lib/pilot-types";
import { Message, date } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";
export default async function AlarmDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; let result: AlarmDetail; let canAct = false;
  if (!/^[0-9a-f-]{36}$/i.test(id)) return <Message title="Alarm bulunamadı" description="Bağlantıyı kontrol edin." />;
  try { const access = await getAccess(); canAct = access.role !== "viewer"; result = await api<AlarmDetail>(`/alarms/${id}`); }
  catch (e) { return <Message title="Alarm yüklenemedi" description={apiMessage(e)} />; }
  const a = result.alarm;
  return <><Link href="/alarms" className="back-link" prefetch={false}>← Alarmlar</Link>
    <section className="hero"><div><h1>{a.kind === "overdue" ? "Süre aşıldı" : "Hata bildirildi"}</h1><p>{a.business_id} · {a.step_code}</p></div>
      {!a.acknowledged_at && canAct && <ActionForm action={adminAction} label="Gördüm" compact><input type="hidden" name="op" value="ack" /><input type="hidden" name="id" value={id} /></ActionForm>}
    </section>
    <section className="panel detail-summary"><h2>{a.resolved_at ? "Alarm kapandı" : "Alarm açık"}</h2>
      <p>Açılış: {date(a.opened_at)} · Beklenen son zaman: {date(a.deadline_at)} · Kapanış: {date(a.resolved_at)}</p>
      {a.resolution && <p>{a.resolution === "step_completed" ? "Adımın tamamlandığı bildirildi." : "İşlem durumu değişti; bu gecikme koşulu artık geçerli değil."}</p>}
      {a.error && <pre className="error-content">{a.error}</pre>}
      <p>“Gördüm” alarmı kapatmaz. Kapanış, gelen bildirimlerle belirlenir.</p>
      <Link href={`/transactions/${encodeURIComponent(a.flow)}/${encodeURIComponent(a.business_id)}`} prefetch={false}>İşlem detayını aç →</Link>
    </section>
    <section className="panel detail-summary"><h2>Alarm geçmişi</h2><ol className="alarm-history">
      {result.events.map(e => <li key={e.id}><strong>{eventNames[e.kind] ?? e.kind}</strong> · {date(e.created_at)}{e.actor && ` · ${e.actor}`}</li>)}
    </ol></section>
    <section className="panel"><div className="section-head"><h2>Bildirim gönderimleri</h2></div>
      <div className="table-wrap"><table><thead><tr><th>Kanal / olay</th><th>Durum</th><th>Deneme</th><th>Sonraki deneme / gönderim</th><th>İşlem</th></tr></thead><tbody>
        {result.deliveries.map(d => <tr key={d.id}><td>{d.channel}<small className="block">{eventNames[d.event]}</small></td>
          <td>{deliveryNames[d.state]}{d.last_error && <small className="block">{d.last_error}</small>}</td><td>{d.attempts}</td>
          <td>{date(d.state === "sent" ? d.sent_at : d.state === "pending" ? d.available_at : null)}</td>
          <td>{d.state === "dead" && canAct && <ActionForm action={adminAction} label="Tekrar dene" compact><input type="hidden" name="op" value="retry" /><input type="hidden" name="id" value={d.id} /></ActionForm>}</td></tr>)}
      </tbody></table></div>{result.deliveries.length === 0 && <p className="empty">Bu olay oluşurken etkin bildirim kanalı yoktu.</p>}
    </section></>;
}
```

## 5. frontend/src/app/alarms/page.tsx

**YENİ** — Açık/kapalı alarm listesi, akış filtresi ve sayfalama.

```tsx
import Link from "next/link";
import { api, apiMessage } from "@/lib/api";
import type { AlarmPage } from "@/lib/pilot-types";
import { Message, date } from "@/components/ui";

export const dynamic = "force-dynamic";
export default async function AlarmsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const state = typeof params.state === "string" ? params.state : "open";
  const flow = typeof params.flow === "string" ? params.flow : "";
  const page = typeof params.page === "string" ? params.page : "1";
  let result: AlarmPage;
  try { result = await api<AlarmPage>("/alarms?" + new URLSearchParams({ state, flow, page })); }
  catch (e) { return <Message title="Alarmlar yüklenemedi" description={apiMessage(e)} />; }
  const href = (p: number) => "/alarms?" + new URLSearchParams({ state, flow, page: String(p) });
  return <><section className="hero"><div><p className="eyebrow">OPERASYON TAKİBİ</p><h1>Alarmlar</h1>
    <p>Gecikmeleri ve bildirilen hataları, çözülene kadar takip edin.</p></div></section>
    <section className="panel"><form className="alarm-filters" action="/alarms">
      <label>Durum<select name="state" defaultValue={state}><option value="open">Açık alarmlar</option><option value="resolved">Kapanan alarmlar</option><option value="all">Tümü</option></select></label>
      <label>Akış kodu<input name="flow" defaultValue={flow} maxLength={64} placeholder="Tüm akışlar" /></label><button className="button">Filtrele</button>
    </form><div className="table-wrap"><table><thead><tr><th>İşlem / akış</th><th>Alarm</th><th>Adım</th><th>Açılış</th><th>Durum</th></tr></thead><tbody>
      {result.items.map(a => <tr key={a.id}><td><Link href={`/alarms/${a.id}`} prefetch={false}>{a.business_id} →</Link><small className="block">{a.flow}</small></td>
        <td>{a.kind === "overdue" ? "Süre aşıldı" : "Hata bildirildi"}</td><td>{a.step_code}</td><td>{date(a.opened_at)}</td>
        <td><span className={`badge ${a.resolved_at ? "good" : "warn"}`}>{a.resolved_at ? "Kapandı" : a.acknowledged_at ? "Açık · görüldü" : "Açık"}</span></td></tr>)}
    </tbody></table></div>{result.items.length === 0 && <div className="empty">Seçilen filtrede alarm yok.</div>}
    <div className="pagination"><span>{result.total} alarm · Sayfa {result.page}</span><div>
      {result.page > 1 && <Link href={href(result.page - 1)} prefetch={false}>← Önceki</Link>}{" "}
      {result.page * result.pageSize < result.total && <Link href={href(result.page + 1)} prefetch={false}>Sonraki →</Link>}
    </div></div></section></>;
}
```

## 6. frontend/src/app/flows/new/page.tsx

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import FlowForm from "@/components/flow-form";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import type { FlowDetails } from "@/lib/flow-details";
import { Message } from "@/components/ui";

export const dynamic = "force-dynamic";

export default async function NewFlowPage({ searchParams }: {
  searchParams: Promise<{ copyFrom?: string | string[] }>;
}) {
  const { copyFrom } = await searchParams;
  let source: FlowDetails | undefined;
  try {
    if ((await getAccess()).role === "viewer") return <Message title="Görüntüleme yetkisi" description="Yeni akışı proje sahibi veya operatör oluşturabilir." />;
  } catch (e) { return <Message title="Erişim doğrulanamadı" description={apiMessage(e)} />; }
  if (copyFrom !== undefined) {
    if (typeof copyFrom !== "string" || !/^[a-z][a-z0-9-]{0,63}$/.test(copyFrom)) notFound();
    try { source = await getFlow(copyFrom); }
    catch (error) {
      if (error instanceof ApiError && error.status === 404) notFound();
      return <Message title="Kaynak akış yüklenemedi" description={apiMessage(error)} />;
    }
  }
  return <>
    <section className="hero"><div><p className="eyebrow">AKIŞ YÖNETİMİ</p><h1>Yeni akış</h1>
      <p>İzlenecek adımları ve her adım için bekleme süresini tanımlayın.</p></div></section>
    {source && <p className="notice copy-notice"><strong>{source.name}</strong> akışının kopyasını hazırlıyorsunuz. Yeni bir akış kodu girin; adımları ve süreleri değiştirebilirsiniz. Kaydettiğinizde ayrı bir akış oluşacak.</p>}
    <FlowForm key={source?.id ?? "new"} initialFlow={source} />
  </>;
}
```

## 7. frontend/src/app/flows/page.tsx

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";
import { apiMessage, getFlows, getAccess } from "@/lib/api";
import type { Flow } from "@/lib/types";
import { Message, number } from "@/components/ui";

export const dynamic = "force-dynamic";

export default async function FlowsPage() {
  let flows: Flow[];
  let canWrite = false;
  try { flows = await getFlows(); canWrite = (await getAccess()).role !== "viewer"; }
  catch (error) { return <Message title="Akışlar yüklenemedi" description={apiMessage(error)} />; }
  return <>
    <section className="hero"><div><p className="eyebrow">AKIŞ YÖNETİMİ</p><h1>Akışlar</h1>
      <p>Bu projede izlediğiniz süreçler ve adım sayıları.</p></div>
      {canWrite && <Link className="button" href="/flows/new" prefetch={false}>+ Yeni akış</Link>}
    </section>
    <section className="panel">
      <div className="section-head"><h2>Tanımlı akışlar</h2><span className="count">{number(flows.length)} akış</span></div>
      <div className="table-wrap"><table><thead><tr><th>Akış adı</th><th>Akış kodu</th><th>Adım sayısı</th><th>İşlemler</th></tr></thead>
        <tbody>{flows.map(flow => <tr key={flow.id}>
          <td><Link className="flow-name" href={`/flows/view/${encodeURIComponent(flow.code)}`} prefetch={false}>{flow.name} →</Link></td><td className="mono">{flow.code}</td><td>{flow.stepCount}</td>
          <td><Link href={`/?${new URLSearchParams({ flow: flow.code })}`} prefetch={false}>İşlemleri göster →</Link></td>
        </tr>)}</tbody>
      </table></div>
      {flows.length === 0 && <div className="empty"><h3>Henüz akış tanımlanmamış</h3><p>Yeni akış oluşturarak izlenecek adımları belirleyin.</p></div>}
    </section>
  </>;
}
```

## 8. frontend/src/app/flows/view/[flow]/page.tsx

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getFlow, getAccess } from "@/lib/api";
import { duration, type FlowDetails } from "@/lib/flow-details";
import { Message } from "@/components/ui";
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
    return <Message title="Akış yüklenemedi" description={apiMessage(error)} />;
  }
  const steps = [...item.steps].sort((a, b) => a.position - b.position);
  return <>
    <Link className="back-link" href="/flows" prefetch={false}>← Akışlara dön</Link>
    <section className="hero"><div><p className="eyebrow">AKIŞ DETAYI · {item.code}</p><h1>{item.name}</h1>
      <p>Tanımlı adımlar ve her adım için beklenen süre.</p></div>
      {canWrite && <Link className="button" href={`/flows/new?${new URLSearchParams({ copyFrom: item.code })}`} prefetch={false}>Kopyadan yeni akış oluştur</Link>}
    </section>
    <section className="panel">
      <div className="section-head"><h2>Adımlar ve süreler</h2><span className="count">{steps.length} adım</span></div>
      <div className="table-wrap"><table><thead><tr><th>Sıra</th><th>Adım</th><th>Adım kodu</th><th>Bekleme süresi</th><th>Sürenin başlangıcı</th></tr></thead>
        <tbody>{steps.map((step, index) => <tr key={step.code}>
          <td>{step.position + 1}</td><td>{step.name}</td><td className="mono">{step.code}</td>
          <td>{duration(step.maxDelaySeconds)}</td><td>{index === 0 ? "—" : `${steps[index - 1].name} tamamlandığında`}</td>
        </tr>)}</tbody>
      </table></div>
    </section>
    <p className="scope-note">Süreleri değiştirmek için kopyadan yeni bir akış oluşturabilirsiniz. Yeni işlemleri gönderen sistemlerde yeni akış kodunu kullanmanız gerekir.</p>
    {canWrite && <FlowTest key={item.id} flow={item} />}
    <section className="panel detail-summary"><h2>Sistem bağlantısı</h2>
      <p className="form-help">Sistemler <code>POST /api/v1/events</code> adresine, <code>X-API-Key</code> başlığında proje anahtarıyla bildirim gönderir.</p>
      <p className="form-help">Aynı işleme ait adımlarda <code>transactionId</code> aynı kalır. Her yeni bildirimin <code>eventId</code> değeri farklıdır. Bir bildirimi yeniden gönderirken kimliği ve içeriği korunur.</p>
      <Link href={`/?${new URLSearchParams({ flow: item.code })}`} prefetch={false}>Bu akışın işlemlerini göster →</Link>
    </section>
  </>;
}
```

## 9. frontend/src/app/globals.css

**DEĞİŞTİR** — Mevcut tasarım ve yeni formlar/alarm ekranları için stiller.

```css
* { box-sizing: border-box; }
:root { color-scheme: light; --ink: #1a2b38; --muted: #627581; --line: #dfe7e9; --teal: #086f62; }
body { margin: 0; color: var(--ink); background: #f3f6f7; font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
a { color: var(--teal); text-decoration: none; }
a:hover { text-decoration: underline; }
button, select, input { font: inherit; }
button, a, select, input { -webkit-tap-highlight-color: transparent; }
:focus-visible { outline: 3px solid #339cbd; outline-offset: 3px; }
h1, h2, h3, p { margin: 0; }
h1 { font-size: clamp(26px, 3vw, 36px); letter-spacing: -1px; line-height: 1.2; overflow-wrap: anywhere; }
h2 { font-size: 18px; letter-spacing: -.3px; }
h3 { font-size: 15px; }
small { display: block; font-size: 11px; color: var(--muted); margin-top: 5px; overflow-wrap: anywhere; }
.topbar { min-height: 78px; padding: 16px 5%; border-bottom: 1px solid var(--line); background: white; display: flex; align-items: center; justify-content: space-between; gap: 20px; }
.brand { color: var(--ink); font-size: 21px; font-weight: 750; display: flex; align-items: center; gap: 11px; letter-spacing: -.7px; }
.brand:hover { text-decoration: none; }
.brand-mark { display: grid; place-items: center; background: #123c38; color: #e0ffef; width: 38px; height: 38px; border-radius: 11px; font-size: 22px; }
.workspace, .refresh-controls { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
.workspace { color: var(--muted); font-size: 12px; }
.local-tag, .scope { background: #e7eeec; color: #395950; padding: 6px 10px; border-radius: 7px; font-size: 12px; }
.shell { max-width: 1440px; margin: auto; padding: 0 5% 50px; }
.toolbar { display: flex; justify-content: space-between; align-items: center; gap: 15px; padding: 22px 0; }
.nav-link { font-weight: 650; border-bottom: 2px solid var(--teal); padding: 9px 0; }
.toggle { display: flex; align-items: center; gap: 7px; color: var(--muted); font-size: 12px; }
.toggle input { accent-color: var(--teal); width: 16px; height: 16px; }
.button { display: inline-flex; align-items: center; justify-content: center; border: 1px solid var(--teal); border-radius: 8px; background: var(--teal); color: white; padding: 9px 15px; min-height: 40px; font-weight: 600; cursor: pointer; white-space: nowrap; }
.button:hover { text-decoration: none; filter: brightness(.95); }
.button.secondary { background: white; border-color: var(--line); color: var(--ink); }
.button:disabled { opacity: .55; cursor: wait; }
.hero { display: flex; justify-content: space-between; align-items: center; gap: 24px; margin: 14px 0 28px; }
.hero > div { min-width: 0; }
.hero p:not(.eyebrow) { color: var(--muted); margin-top: 10px; }
.eyebrow { color: var(--teal); font-size: 10px; font-weight: 750; letter-spacing: 1.8px; margin-bottom: 10px; overflow-wrap: anywhere; }
.scope { flex-shrink: 0; max-width: 280px; overflow-wrap: anywhere; }
.metrics { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 12px; }
.metric { display: flex; flex-direction: column; gap: 16px; background: white; border: 1px solid var(--line); border-radius: 10px; padding: 17px; color: var(--ink); border-top: 3px solid #a4b4bc; }
.metric:hover { text-decoration: none; border-color: var(--teal); box-shadow: 0 4px 14px #173a3510; }
.metric > span { color: var(--muted); font-size: 11px; }
.metric strong { font-size: 30px; font-weight: 650; letter-spacing: -1px; }
.metric.good { border-top-color: #259175; }.metric.bad { border-top-color: #d86a67; }.metric.warn { border-top-color: #d6a440; }.metric.info { border-top-color: #568bb1; }
.scope-note { color: var(--muted); font-size: 11px; display: flex; justify-content: space-between; gap: 15px; flex-wrap: wrap; margin: 12px 0 28px; }
.panel { border: 1px solid var(--line); border-radius: 12px; background: white; overflow: hidden; margin-bottom: 22px; }
.section-head { padding: 23px; display: flex; justify-content: space-between; align-items: center; gap: 15px; }
.section-head p { color: var(--muted); font-size: 12px; margin-top: 4px; }
.count { color: var(--muted); background: #f2f5f6; padding: 5px 10px; border-radius: 6px; font-size: 12px; white-space: nowrap; }
.filters { display: flex; align-items: end; flex-wrap: wrap; gap: 12px; padding: 0 23px 24px; }
.filters label { display: flex; flex-direction: column; gap: 7px; color: var(--muted); font-size: 11px; flex: 1 1 140px; min-width: 0; }
.filters select { width: 100%; height: 40px; padding: 0 10px; border: 1px solid #cfdbdf; background: white; border-radius: 7px; color: var(--ink); }
.reset { align-self: center; margin-top: 20px; font-size: 12px; padding: 8px; }
.table-wrap { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; min-width: 790px; text-align: left; font-size: 12px; }
th { padding: 13px 22px; background: #f7f9fa; color: var(--muted); font-size: 10px; font-weight: 650; border-block: 1px solid var(--line); }
td { padding: 17px 22px; border-bottom: 1px solid #edf1f2; vertical-align: middle; }
tbody tr:last-child td { border-bottom: 0; }
tbody tr:hover { background: #fafcfc; }
.identifier { font-weight: 650; max-width: 240px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; display: inline-block; vertical-align: middle; }
.flow-name { display: block; max-width: 190px; overflow-wrap: anywhere; }
.badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 8px; border-radius: 6px; font-size: 10px; font-weight: 600; white-space: nowrap; }
.badge::before { content: ""; width: 5px; height: 5px; border-radius: 50%; background: currentColor; }
.badge.good { background: #e7f5ee; color: #176b50; }.badge.bad { background: #fdeeed; color: #a43635; }.badge.warn { background: #fff4dc; color: #88590c; }.badge.info { background: #eaf2fa; color: #37648c; }.badge.neutral { background: #eef2f4; color: #5b6e7b; }
.nowrap { white-space: nowrap; }.mono { font-family: ui-monospace, SFMono-Regular, monospace; }
.empty { padding: 42px 20px; text-align: center; color: var(--muted); }
.empty p { margin: 8px 0 15px; font-size: 12px; }
.pagination { display: flex; justify-content: space-between; gap: 15px; align-items: center; border-top: 1px solid var(--line); padding: 15px 23px; color: var(--muted); font-size: 12px; }
.pagination nav { display: flex; gap: 8px; }.pagination .button { font-size: 12px; }
.back-link { display: inline-block; margin: 8px 0 12px; font-size: 12px; }
.detail-summary { padding: 24px; }.detail-summary > h2 { display: inline-block; margin-right: 14px; }
.facts { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 20px; margin: 23px 0 0; }
dt { color: var(--muted); font-size: 11px; }dd { margin: 6px 0 0; font-size: 13px; overflow-wrap: anywhere; }
.notice { margin-top: 20px; border-left: 3px solid #d6a440; background: #fff8e9; padding: 12px 15px; font-size: 12px; }
.timeline { list-style: none; margin: 0; padding: 0 23px 24px; }
.timeline li { display: flex; gap: 16px; padding: 20px 12px; border-top: 1px solid var(--line); }
.timeline li.expected { background: #fcfaf3; border-radius: 8px; }.step-content { flex: 1; min-width: 0; }
.step-dot { display: grid; place-items: center; flex-shrink: 0; width: 30px; height: 30px; border: 1px solid var(--line); border-radius: 50%; color: var(--muted); font-size: 12px; }
.step-dot.completed { color: #176b50; background: #e7f5ee; border-color: #abd4c1; }.step-dot.failed { color: #a43635; background: #fdeeed; border-color: #eec3c2; }
.step-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }.step-heading h3 { overflow-wrap: anywhere; }
.step-content p { font-size: 12px; color: var(--muted); margin-top: 5px; }
.event-error, .step-content .event-error { color: #a43635; white-space: pre-wrap; overflow-wrap: anywhere; max-width: 350px; }
.message { padding: 48px 24px; text-align: center; }.message p { color: var(--muted); margin: 16px auto 24px; max-width: 550px; }
.footer { border-top: 1px solid var(--line); padding: 20px 5%; color: var(--muted); font-size: 11px; display: flex; justify-content: space-between; gap: 16px; }
.skip-link { position: fixed; top: -60px; left: 10px; padding: 10px; background: white; z-index: 10; }.skip-link:focus { top: 10px; }
@media (max-width: 1100px) { .metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }.facts { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 600px) {
  .topbar { padding: 16px 5%; }.workspace { gap: 5px; flex-direction: column; align-items: end; max-width: 45%; overflow-wrap: anywhere; }
  .brand { font-size: 18px; }.brand-mark { width: 32px; height: 32px; }.toolbar { align-items: start; }.refresh-controls { justify-content: flex-end; gap: 8px; }.toggle { font-size: 10px; }
  .hero { align-items: flex-start; flex-direction: column; gap: 12px; }.metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }.metric { padding: 14px; gap: 9px; }
  .section-head { padding: 18px; }.filters { padding: 0 18px 18px; }.filters label { flex-basis: 42%; }.scope-note { font-size: 10px; }
  .detail-summary { padding: 18px; }.facts { grid-template-columns: 1fr; gap: 16px; }.timeline { padding: 0 12px 12px; }.timeline li { gap: 10px; }.step-heading { align-items: flex-start; flex-direction: column; }
  .pagination { padding: 14px; }.footer { flex-direction: column; gap: 4px; }
}

/* Akış yönetimi */
.panel-nav { display: flex; gap: 24px; }
.panel-nav .nav-link { border-bottom-color: transparent; color: var(--muted); }
.panel-nav .nav-link.active { border-bottom-color: var(--teal); color: var(--teal); }
.flow-editor { padding: 26px; }
.flow-editor fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
.flow-legend { font-size: 18px; font-weight: 650; padding: 0; margin-bottom: 18px; }
.flow-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }
.flow-field { display: flex; flex-direction: column; gap: 7px; min-width: 0; }
.flow-field label { color: var(--muted); font-size: 12px; }
.flow-field input, .flow-field select { width: 100%; min-width: 0; height: 42px; padding: 0 12px; border: 1px solid #cfdbdf; border-radius: 7px; color: var(--ink); background: white; }
.flow-field [aria-invalid="true"] { border-color: #b2413e; }
.form-help { margin: 12px 0 18px; color: var(--muted); font-size: 12px; }
.flow-steps-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 24px 0 16px; }
.flow-step-list { list-style: none; margin: 18px 0; padding: 0; }
.flow-step-card { border: 1px solid var(--line); border-radius: 10px; padding: 20px; margin-bottom: 16px; }
.flow-step-card .flow-steps-head { margin-top: 0; }
.flow-step-card .form-help { margin-bottom: 0; }
.flow-step-card .button { min-height: 34px; padding: 6px 10px; font-size: 12px; }
.delay-inputs { margin-top: 18px; }
.field-error { color: #a43635; font-size: 12px; margin: 0; }
.flow-error { border-left: 3px solid #b2413e; background: #fdeeed; color: #a43635; padding: 14px; margin: 18px 0; }
.flow-actions { display: flex; gap: 18px; align-items: center; flex-wrap: wrap; margin-top: 24px; }
.message .flow-actions { justify-content: center; }
.flow-editor fieldset:disabled { opacity: .7; }
.copy-notice { margin: 0 0 22px; }
.auth-panel { max-width: 600px; margin: 40px auto; padding: 32px; }
.admin-form { margin-top: 24px; max-width: 720px; }
.admin-form fieldset, .inline-form fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
.admin-form fieldset { display: grid; gap: 18px; }
.admin-form label, .alarm-filters label { display: grid; gap: 8px; color: #526b76; font-size: 14px; }
.admin-form input, .admin-form select, .alarm-filters input, .alarm-filters select { width: 100%; min-height: 46px; border: 1px solid #c6d6de; border-radius: 8px; padding: 10px 12px; background: white; color: #1b2e39; font: inherit; }
.admin-form .button { justify-self: start; }
.inline-form .button { padding: 8px 12px; min-height: 36px; font-size: 13px; }
.secret-box { margin-top: 16px; padding: 16px; background: #edf6f3; border: 1px solid #b8d9cf; border-radius: 8px; }
.secret-box code { display: block; margin-top: 12px; overflow-wrap: anywhere; user-select: all; }
.project-row { display: flex; gap: 20px; align-items: center; justify-content: space-between; padding: 20px 0; border-bottom: 1px solid #e0e8ec; }
.project-row p { margin: 8px 0 0; }
.block { display: block; margin-top: 7px; color: #627985; }
.text-button { border: 0; padding: 0; background: none; color: #087e70; font: inherit; cursor: pointer; }
.alarm-filters { padding: 24px; display: flex; flex-wrap: wrap; align-items: end; gap: 20px; }
.alarm-history { display: grid; gap: 16px; padding-left: 20px; }
.ops-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 18px; margin-bottom: 24px; }
.ops-number { font-size: 34px; }
.error-content { white-space: pre-wrap; overflow-wrap: anywhere; color: #a13232; }
.detail-summary { margin-bottom: 24px; }
.detail-summary h3 { margin-top: 30px; }
.workspace { flex-wrap: wrap; gap: 18px; }
.panel-nav { flex-wrap: wrap; }
@media (max-width: 900px) { .ops-grid { grid-template-columns: repeat(2, 1fr); } .topbar { flex-wrap: wrap; gap: 16px; } .toolbar { flex-wrap: wrap; gap: 20px; } }
@media (max-width: 500px) { .ops-grid { grid-template-columns: 1fr; } .auth-panel { padding: 20px; } .alarm-filters label { width: 100%; } }
.test-progress { padding: 16px; margin-top: 16px; background: #f3f7f7; border-radius: 8px; }
.test-progress p { margin-bottom: 8px; overflow-wrap: anywhere; }
.event-example { margin-top: 22px; }
.event-example summary { cursor: pointer; font-weight: 600; color: var(--teal); }
.event-example pre { overflow-x: auto; background: #142e31; color: #e0f4ef; padding: 18px; border-radius: 8px; font-size: 12px; }
@media (max-width: 600px) {
  .toolbar { flex-wrap: wrap; }
  .flow-editor { padding: 18px; }
  .flow-grid { grid-template-columns: 1fr; gap: 14px; }
  .flow-step-card { padding: 15px; }
}
```

## 10. frontend/src/app/layout.tsx

**DEĞİŞTİR** — Ortak başlık, seçili proje adı, hesap bağlantıları ve sayfa düzeni.

```tsx
import type { Metadata } from "next";
import Link from "next/link";
import PanelToolbar from "@/components/panel-toolbar";
import { cookies } from "next/headers";
import { logoutAction } from "@/app/account-actions";
import { getProjects } from "@/lib/api";
import "./globals.css";

export const metadata: Metadata = { title: "IntegraOps | Operasyon paneli", robots: { index: false, follow: false } };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const jar = await cookies();
  const loggedIn = Boolean(jar.get("io_session")?.value);
  let projectName = "";
  if (loggedIn) {
    try { const projects = await getProjects(); projectName = (projects.find(p => p.id === jar.get("io_project")?.value) ?? projects[0])?.name ?? ""; }
    catch { /* The page handles expired sessions; the login page remains accessible. */ }
  }
  return <html lang="tr"><body>
    <a className="skip-link" href="#content">İçeriğe geç</a>
    <header className="topbar">
      <Link className="brand" href="/" prefetch={false}><span className="brand-mark">io</span>IntegraOps</Link>
      <div className="workspace">{loggedIn && <><span className="local-tag">{projectName || "Proje seçin"}</span><Link href="/projects" prefetch={false}>Projelerim</Link>
        <Link href="/account" prefetch={false}>Hesabım</Link><form action={logoutAction}><button className="text-button">Çıkış</button></form></>}</div>
    </header>
    <main id="content" className="shell">
      {loggedIn && <PanelToolbar />}
      {children}
    </main>
    <footer className="footer">IntegraOps · Akış takibi<span>Tüm saatler Türkiye saatidir.</span></footer>
  </body></html>;
}
```

## 11. frontend/src/app/login/page.tsx

**YENİ** — Kullanıcı giriş ekranı.

```tsx
import ActionForm from "@/components/action-form";
import { loginAction } from "@/app/account-actions";

export default function LoginPage() {
  return <section className="panel auth-panel"><p className="eyebrow">ÇALIŞMA ALANINIZ</p><h1>Giriş yapın</h1>
    <p>Projelerinize ve işlem akışlarınıza erişin.</p>
    <ActionForm action={loginAction} label="Giriş yap">
      <label>E-posta<input name="email" type="email" required maxLength={254} autoComplete="username" /></label>
      <label>Parola<input name="password" type="password" required maxLength={128} autoComplete="current-password" /></label>
    </ActionForm>
  </section>;
}
```

## 12. frontend/src/app/operations/page.tsx

**YENİ** — Kuyruk sayıları, yaşam sinyali ve yönetim geçmişi.

```tsx
import { api, apiMessage } from "@/lib/api";
import type { Operations } from "@/lib/pilot-types";
import { Message, date } from "@/components/ui";

export const dynamic = "force-dynamic";
export default async function OperationsPage() {
  let result: Operations;
  try { result = await api<Operations>("/operations"); }
  catch (e) { return <Message title="Sistem görünümü açılamadı" description={apiMessage(e)} />; }
  const c = result.counts;
  const recent = c.worker_seen_at && Date.parse(c.checked_at) - Date.parse(c.worker_seen_at) < 20000;
  const stats = [["Kuyruğa aktarılacak", c.outbox_pending], ["İşlenecek bildirim", c.events_pending],
    ["Gönderim bekleyen alarm bildirimi", c.notifications_pending], ["Denemesi duran bildirim", c.notifications_dead]];
  return <><section className="hero"><div><h1>Sistem durumu</h1><p>Seçili projenin bekleyen işleri ve yönetim geçmişi.</p></div></section>
    <section className="ops-grid">{stats.map(([name, value]) => <div className="panel detail-summary" key={name}><p>{name}</p><strong className="ops-number">{value}</strong></div>)}</section>
    <section className="panel detail-summary"><h2>Worker yaşam sinyali: {recent ? "Güncel" : "Gecikmiş / henüz yok"}</h2>
      <p>Son sinyal: {date(c.worker_seen_at)} · En eski bekleyen aktarım: {date(c.oldest_outbox_at)}</p>
      <p>Yaşam sinyali işlemin çalıştığını gösterir. Kuyruk bağlantısının veya bütün işlerin sağlıklı olduğunu tek başına kanıtlamaz; bekleyen kayıtlarla birlikte değerlendirin.</p>
    </section><section className="panel"><div className="section-head"><h2>Son 100 yönetim işlemi</h2></div>
      <div className="table-wrap"><table><thead><tr><th>Zaman</th><th>Kullanıcı</th><th>İşlem</th><th>Kayıt</th></tr></thead><tbody>
        {result.audit.map((a, i) => <tr key={i}><td>{date(a.created_at)}</td><td>{a.actor ?? "Sistem"}</td><td>{a.action}</td><td><code>{a.target}</code></td></tr>)}
      </tbody></table></div></section></>;
}
```

## 13. frontend/src/app/projects/page.tsx

**YENİ** — Proje seçme ve yeni proje oluşturma.

```tsx
import { apiMessage, getProjects } from "@/lib/api";
import { roleNames } from "@/lib/pilot-types";
import { Message } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { createProjectAction, switchProject } from "@/app/account-actions";

export const dynamic = "force-dynamic";
export default async function ProjectsPage() {
  let projects;
  try { projects = await getProjects(); }
  catch (e) { return <Message title="Projeler yüklenemedi" description={apiMessage(e)} />; }
  return <><section className="hero"><div><h1>Projelerim</h1><p>Her projenin akışları, anahtarları ve kullanıcı yetkileri ayrıdır.</p></div></section>
    <section className="panel detail-summary"><h2>Çalışma alanı seçin</h2>
      {projects.map(p => <form action={switchProject} className="project-row" key={p.id}>
        <input type="hidden" name="project" value={p.id} /><div><strong>{p.name}</strong><p>{roleNames[p.role]}</p></div>
        <button className="button" type="submit">Projeyi aç</button></form>)}
      {projects.length === 0 && <p>Henüz bir projeye üye değilsiniz. Proje sahibiniz sizi ekleyebilir veya yeni proje oluşturabilirsiniz.</p>}
    </section>
    <section className="panel detail-summary"><h2>Yeni proje</h2><ActionForm action={createProjectAction} label="Proje oluştur">
      <label>Proje adı<input name="name" required maxLength={160} /></label>
    </ActionForm></section></>;
}
```

## 14. frontend/src/app/settings/page.tsx

**YENİ** — Üyeler, roller, API anahtarları ve bildirim kanalları.

```tsx
import { api, apiMessage, getAccess } from "@/lib/api";
import type { Member, ProjectKey, Channel } from "@/lib/pilot-types";
import { roleNames } from "@/lib/pilot-types";
import { Message } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { adminAction } from "@/app/admin-actions";

export const dynamic = "force-dynamic";
function Op({ value, id }: { value: string; id?: string }) {
  return <><input type="hidden" name="op" value={value} />{id && <input type="hidden" name="id" value={id} />}</>;
}
export default async function SettingsPage() {
  let members: Member[], keys: ProjectKey[], channels: Channel[];
  try {
    const access = await getAccess();
    if (access.role !== "owner") return <Message title="Proje ayarları" description="Kullanıcıları, anahtarları ve bildirim kanallarını proje sahibi yönetir." />;
    [members, keys, channels] = await Promise.all([api<Member[]>("/members"), api<ProjectKey[]>("/keys"), api<Channel[]>("/channels")]);
  } catch (e) { return <Message title="Ayarlar yüklenemedi" description={apiMessage(e)} />; }
  return <><section className="hero"><div><h1>Proje ayarları</h1><p>Kullanıcı erişimi ve sistem bağlantıları.</p></div></section>
    <section className="panel detail-summary"><h2>Kullanıcılar</h2>
      <div className="table-wrap"><table><thead><tr><th>Kullanıcı</th><th>Rol</th><th>İşlem</th></tr></thead><tbody>
        {members.map(m => <tr key={m.id}><td>{m.name}<small className="block">{m.email}</small></td><td>{roleNames[m.role]}</td>
          <td><ActionForm action={adminAction} label="Projeden çıkar" compact><Op value="member-remove" id={m.id} /></ActionForm></td></tr>)}
      </tbody></table></div>
      <h3>Kullanıcı ekle veya rolünü değiştir</h3><p>Aynı e-posta adresiyle kayıtlı bir kullanıcı varsa parolayı boş bırakın. Yeni kullanıcı için başlangıç parolası belirleyin.</p>
      <ActionForm action={adminAction} label="Kullanıcıyı kaydet"><Op value="member-save" />
        <label>Ad soyad<input name="name" required maxLength={120} /></label>
        <label>E-posta<input name="email" type="email" required maxLength={254} /></label>
        <label>Rol<select name="role"><option value="viewer">İzleyici · görüntüleme</option><option value="operator">Operatör · akış ve alarm işlemleri</option><option value="owner">Proje sahibi · bütün ayarlar</option></select></label>
        <label>Yeni kullanıcı parolası<input name="password" type="password" minLength={12} maxLength={128} autoComplete="new-password" /></label>
      </ActionForm>
    </section>
    <section className="panel detail-summary"><h2>API anahtarları</h2><p>Gönderim anahtarını bağlanacak sistemde saklayın. İptal edilen anahtar hemen erişimini kaybeder.</p>
      <div className="table-wrap"><table><thead><tr><th>Ad</th><th>Yetki</th><th>Önek</th><th>Durum</th></tr></thead><tbody>
        {keys.map(k => <tr key={k.id}><td>{k.name}</td><td>{k.scope === "ingest" ? "Bildirim gönderimi" : k.scope === "read" ? "Takip verilerini okuma" : "Önceki geniş yetki"}</td><td><code>{k.key_prefix}…</code></td>
          <td>{k.revoked_at ? "İptal edilmiş" : <ActionForm action={adminAction} label="Anahtarı iptal et" compact><Op value="key-revoke" id={k.id} /></ActionForm>}</td></tr>)}
      </tbody></table></div>
      <ActionForm action={adminAction} label="Anahtar oluştur"><Op value="key-create" />
        <label>Anahtar adı<input name="name" required maxLength={120} placeholder="ERP bağlantısı" /></label>
        <label>Yetki<select name="scope"><option value="ingest">Bildirim gönderimi</option><option value="read">Takip verilerini okuma</option></select></label>
      </ActionForm>
    </section>
    <section className="panel detail-summary"><h2>Bildirim kanalları</h2><p>Etkin kanallara, yeni açılan ve kapanan alarmlar gönderilir. Kanalı kapatmak bekleyen gönderimleri iptal eder; başlamış bir gönderim tamamlanabilir.</p>
      <div className="table-wrap"><table><thead><tr><th>Kanal</th><th>Tür</th><th>Hedef</th><th>Durum</th></tr></thead><tbody>
        {channels.map(c => <tr key={c.id}><td>{c.name}</td><td>{c.kind === "email" ? "E-posta" : "Webhook"}</td><td>{c.target}</td>
          <td><ActionForm action={adminAction} label={c.enabled ? "Kapat" : "Etkinleştir"} compact><Op value="channel-toggle" id={c.id} /><input type="hidden" name="enabled" value={String(!c.enabled)} /></ActionForm></td></tr>)}
      </tbody></table></div>
      <ActionForm action={adminAction} label="Kanal oluştur"><Op value="channel-create" />
        <label>Kanal adı<input name="name" required maxLength={120} /></label>
        <label>Tür<select name="kind"><option value="email">E-posta</option><option value="webhook">Webhook</option></select></label>
        <label>E-posta adresi veya webhook URL<input name="target" required maxLength={2048} /></label>
        <label>Webhook ortak sırrı · boşsa üretilir<input name="secret" type="password" minLength={32} maxLength={128} autoComplete="off" /></label>
      </ActionForm>
    </section></>;
}
```

## 15. frontend/src/components/action-form.tsx

**YENİ** — Form gönderimi sırasında bekleme, sonuç ve bir kez gösterilen sır alanı.

```tsx
"use client";

import { useActionState } from "react";
import type { ActionState } from "@/lib/pilot-types";

export default function ActionForm({ action, children, label, compact = false }: {
  action: (state: ActionState, data: FormData) => Promise<ActionState>;
  children?: React.ReactNode; label: string; compact?: boolean;
}) {
  const [state, submit, pending] = useActionState(action, { message: "" });
  return <form action={submit} className={compact ? "inline-form" : "admin-form"}>
    <fieldset disabled={pending}>{children}<button className="button" type="submit">{pending ? "İşleniyor…" : label}</button></fieldset>
    {state.message && <p role="status" className={state.ok ? "form-success" : "form-error"}>{state.message}</p>}
    {state.secret && <div className="secret-box"><strong>Bir kez gösterilir; şimdi güvenli bir yere kaydedin.</strong>
      <code>{state.secret}</code></div>}
  </form>;
}
```

## 16. frontend/src/components/panel-toolbar.tsx

**DEĞİŞTİR** — İşlemler, Akışlar, Alarmlar, Ayarlar ve Sistem sekmeleri.

```tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import RefreshControls from "./refresh-controls";

export default function PanelToolbar() {
  const pathname = usePathname();
  if (pathname === "/login") return null;
  const tabs = [["/", "İşlemler"], ["/flows", "Akışlar"], ["/alarms", "Alarmlar"], ["/settings", "Ayarlar"], ["/operations", "Sistem"]];
  return <div className="toolbar">
    <nav className="panel-nav" aria-label="Ana menü">
      {tabs.map(([href, label]) => {
        const active = href === "/" ? pathname === "/" || pathname.startsWith("/transactions/") : pathname === href || pathname.startsWith(href + "/");
        return <Link key={href} className={`nav-link ${active ? "active" : ""}`} href={href} prefetch={false}
          aria-current={active ? "page" : undefined}>{label}</Link>;
      })}
    </nav>
    {(pathname === "/" || pathname.startsWith("/transactions/") || pathname.startsWith("/alarms") || pathname === "/operations") && <RefreshControls />}
  </div>;
}
```

## 17. frontend/src/lib/api.ts

**DEĞİŞTİR** — Panel sunucusundan API’ye kullanıcı oturumu ve seçili proje ile istek gönderir.

```typescript
import "server-only";
import { cookies } from "next/headers";
import { redirect, unstable_rethrow } from "next/navigation";
import type { FlowDetails, TestEventPayload } from "./flow-details";
import type { CreateFlowInput } from "./flow-form";
import type { Filters, Flow, Summary, TransactionDetail, TransactionPage } from "./types";
import type { Project, Access } from "./pilot-types";

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function raw<T>(path: string, method: string, body: unknown, headers: Record<string, string>): Promise<T> {
  const base = process.env.INTEGRAOPS_API_URL;
  if (!base) throw new ApiError(503, "Panelin API bağlantı ayarı eksik.");
  let response: Response;
  try {
    response = await fetch(new URL("/api/v1" + path, base), {
      method, headers: { ...headers, "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store", redirect: "error", signal: AbortSignal.timeout(15000),
    });
  } catch {
    throw new ApiError(503, "Servis yanıtı alınamadı. Yazma işlemi kaydedilmiş olabilir; ilgili listeyi kontrol edin.");
  }
  if (!response.ok) {
    let title = "İstek tamamlanamadı.";
    try {
      const problem = await response.json();
      if (typeof problem.title === "string" && problem.title.length < 500) title = problem.title;
    } catch { /* Do not display raw proxy/server responses. */ }
    if (response.status === 401) title = "Oturum doğrulanamadı. Tekrar giriş yapın.";
    if (response.status === 429) title = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.";
    if (response.status >= 500) title = "Servis şu anda isteği tamamlayamıyor. Bir süre sonra tekrar deneyin.";
    throw new ApiError(response.status, title);
  }
  if (response.status === 204) return undefined as T;
  try { return await response.json() as T; }
  catch { throw new ApiError(502, "Servis yanıtı okunamadı."); }
}

export async function api<T>(path: string, method = "GET", body?: unknown, projectScoped = true): Promise<T> {
  const jar = await cookies(); const token = jar.get("io_session")?.value;
  if (!token) throw new ApiError(401, "Giriş yapmanız gerekli.");
  const headers: Record<string, string> = { Authorization: "Bearer " + token };
  if (projectScoped) {
    let project = jar.get("io_project")?.value;
    if (!project) project = (await raw<Project[]>("/me/projects", "GET", undefined, headers))[0]?.id;
    if (!project) redirect("/projects");
    headers["X-Project-Id"] = project;
  }
  return raw<T>(path, method, body, headers);
}

export const login = (email: string, password: string) => raw<{ token: string; expiresIn: number }>(
  "/auth/login", "POST", { email, password }, {});
export const getProjects = () => api<Project[]>("/me/projects", "GET", undefined, false);
export const getAccess = () => api<Access>("/access");
export const getFlows = () => api<Flow[]>("/flows");
export const getFlow = (flow: string) => api<FlowDetails>("/flows/" + encodeURIComponent(flow));
export const getSummary = (flow: string) => api<Summary>(
  "/transactions/summary" + (flow ? "?" + new URLSearchParams({ flow }) : ""));
export const getTransactions = (filters: Filters) => api<TransactionPage>(
  "/transactions?" + new URLSearchParams({ ...filters, page: String(filters.page), pageSize: String(filters.pageSize) }));
export const getTransaction = (flow: string, id: string) => api<TransactionDetail>(
  "/flows/" + encodeURIComponent(flow) + "/transactions/" + encodeURIComponent(id));
export async function createFlow(input: CreateFlowInput): Promise<void> { await api("/flows", "POST", input); }
export async function postTestEvent(input: TestEventPayload): Promise<{ duplicate: boolean }> {
  const receipt = await api<{ duplicate: boolean }>("/events", "POST", { ...input, status: "completed" });
  if (typeof receipt.duplicate !== "boolean") throw new ApiError(502, "Bildirim yanıtı okunamadı. Aynı bildirimi tekrar gönderebilirsiniz.");
  return receipt;
}
export function apiMessage(error: unknown) {
  unstable_rethrow(error);
  if (error instanceof ApiError && error.status === 401) redirect("/login");
  return error instanceof ApiError ? error.message : "Veriler yüklenemedi. Yeniden deneyin.";
}
```

## 18. frontend/src/lib/pilot-types.ts

**YENİ** — Yeni ekranların beklediği yanıt modelleri ve Türkçe etiketler.

```typescript
export type Role = "owner" | "operator" | "viewer";
export type Project = { id: string; name: string; role: Role };
export type Access = { projectId: string; userId: string; role: Role };
export type ActionState = { message: string; ok?: boolean; secret?: string };
export type Member = { id: string; name: string; email: string; role: Role };
export type ProjectKey = { id: string; name: string; scope: string; key_prefix: string; revoked_at: string | null };
export type Channel = { id: string; name: string; kind: string; target: string; enabled: boolean };
export type Alarm = {
  id: string; flow: string; business_id: string; kind: string; step_code: string; error: string | null;
  opened_at: string; deadline_at: string | null; resolved_at: string | null; resolution: string | null;
  acknowledged_at: string | null;
};
export type AlarmPage = { items: Alarm[]; total: number; page: number; pageSize: number };
export type AlarmDetail = {
  alarm: Alarm;
  events: { id: string; kind: string; created_at: string; actor: string | null }[];
  deliveries: { id: string; channel: string; kind: string; event: string; state: string; attempts: number;
    available_at: string; last_error: string | null; sent_at: string | null }[];
};
export type Operations = {
  counts: { outbox_pending: number; oldest_outbox_at: string | null; events_pending: number;
    notifications_pending: number; notifications_dead: number; worker_seen_at: string | null; checked_at: string };
  audit: { action: string; target: string; created_at: string; actor: string | null }[];
};
export const roleNames: Record<Role, string> = { owner: "Proje sahibi", operator: "Operatör", viewer: "İzleyici" };
export const eventNames: Record<string, string> = { opened: "Açıldı", resolved: "Kapandı", acknowledged: "Görüldü" };
export const deliveryNames: Record<string, string> = {
  pending: "Gönderim bekliyor", sending: "Gönderiliyor", sent: "Gönderildi", dead: "Denemeler durdu", cancelled: "İptal edildi",
};
```

## 19. frontend/src/proxy.ts

**YENİ** — Girişsiz panel gezinmesini giriş ekranına yönlendirir; asıl yetki kontrolü API’dedir.

```typescript
import { NextResponse, type NextRequest } from "next/server";

// Navigation protection only; the API verifies sessions and roles on every request.
export function proxy(request: NextRequest) {
  if (!request.cookies.get("io_session")?.value && request.nextUrl.pathname !== "/login")
    return NextResponse.redirect(new URL("/login", request.url));
  return NextResponse.next();
}
export const config = { matcher: ["/((?!_next/static|_next/image|favicon.ico|robots.txt).*)"] };
```

## 20. frontend/src/app/error.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
"use client";
export default function ErrorPage({ reset }: { reset: () => void }) {
  return <section className="panel message" role="alert"><h1>Görünüm yüklenemedi</h1>
    <p>Beklenmeyen bir sorun oluştu. Sayfayı yeniden yüklemeyi deneyin.</p>
    <button className="button" onClick={reset}>Tekrar dene</button>
  </section>;
}
```

## 21. frontend/src/app/flows/new/actions.ts

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```typescript
"use server";

import { revalidatePath } from "next/cache";
import { ApiError, apiMessage, createFlow } from "@/lib/api";
import { validateFlow, type FlowFormState } from "@/lib/flow-form";

export async function createFlowAction(
  _previous: FlowFormState,
  formData: FormData,
): Promise<FlowFormState> {
  const payload = formData.get("payload");
  let raw: unknown;
  try {
    if (typeof payload !== "string" || payload.length > 32768) throw new Error();
    raw = JSON.parse(payload);
  } catch {
    return { message: "Form verisi okunamadı. Sayfayı yenileyip tekrar deneyin.", errors: {} };
  }
  const result = validateFlow(raw);
  if (!result.ok) return { message: "İşaretli alanları kontrol edin.", errors: result.errors };
  try { await createFlow(result.data); }
  catch (error) {
    return {
      message: apiMessage(error),
      errors: error instanceof ApiError && error.status === 409 ? { "flow-code": "Bu kod zaten kullanılıyor." } : {},
    };
  }
  revalidatePath("/flows");
  revalidatePath("/");
  return { message: "", errors: {}, createdCode: result.data.code };
}
```

## 22. frontend/src/app/flows/not-found.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";

export default function FlowNotFound() {
  return <section className="panel message"><h1>Akış bulunamadı</h1>
    <p>Bu akış mevcut projede bulunmuyor veya bağlantı geçerli değil.</p>
    <Link className="button" href="/flows" prefetch={false}>Akışlara dön</Link>
  </section>;
}
```

## 23. frontend/src/app/flows/test-actions.ts

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```typescript
"use server";

import { revalidatePath } from "next/cache";
import { ApiError, apiMessage, getFlow, postTestEvent } from "@/lib/api";
import { parseTestEvent, type TestEventResult } from "@/lib/flow-details";

export async function sendTestEvent(value: unknown): Promise<TestEventResult> {
  const input = parseTestEvent(value);
  if (!input) return { ok: false, message: "Test bildirimi geçerli değil. Yeni test başlatın.", retry: false };
  try {
    const flow = await getFlow(input.flow);
    if (!flow.steps.some(step => step.code === input.step)) {
      return { ok: false, message: "Bu adım seçilen akışta bulunmuyor.", retry: false };
    }
    const result = await postTestEvent(input);
    revalidatePath("/");
    revalidatePath(`/transactions/${input.flow}/${input.transactionId}`);
    return { ok: true, duplicate: result.duplicate };
  } catch (error) {
    return { ok: false, message: apiMessage(error), retry: !(error instanceof ApiError) || error.status >= 500 };
  }
}
```

## 24. frontend/src/app/loading.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
export default function Loading() {
  return <section className="panel message" role="status" aria-live="polite"><h2>Veriler yükleniyor…</h2><p>İşlemleriniz getiriliyor.</p></section>;
}
```

## 25. frontend/src/app/not-found.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import { Message } from "@/components/ui";
export default function NotFound() {
  return <Message title="İşlem bulunamadı" description="Bu işlem mevcut projede bulunmuyor veya bağlantı geçerli değil." />;
}
```

## 26. frontend/src/app/page.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";
import { apiMessage, getFlows, getSummary, getTransactions } from "@/lib/api";
import type { Filters, Flow, Summary, TransactionPage } from "@/lib/types";
import { Badge, date, delayLabels, listHref, Message, number, statusLabels } from "@/components/ui";

export const dynamic = "force-dynamic";
type Search = Record<string, string | string[] | undefined>;
const one = (value: Search[string]) => (Array.isArray(value) ? value[0] : value) ?? "";

export default async function Home({ searchParams }: { searchParams: Promise<Search> }) {
  const raw = await searchParams;
  const pageText = one(raw.page) || "1";
  const filters: Filters = {
    flow: one(raw.flow).trim(), status: one(raw.status), delay: one(raw.delay),
    page: Number(pageText), pageSize: Number(one(raw.pageSize) || "20"),
  };
  if (!/^\d+$/.test(pageText) || !Number.isSafeInteger(filters.page) || filters.page < 1 || filters.page > 2147483647
    || ![10, 20, 50, 100].includes(filters.pageSize)
    || (filters.flow && !/^[a-z][a-z0-9-]{0,63}$/.test(filters.flow))
    || (filters.status && !Object.hasOwn(statusLabels, filters.status))
    || (filters.delay && !Object.hasOwn(delayLabels, filters.delay))) {
    return <Message title="Filtreler geçerli değil" description="Listeyi açıp filtrelerinizi yeniden seçin." />;
  }

  let data: [Flow[], Summary, TransactionPage];
  try { data = await Promise.all([getFlows(), getSummary(filters.flow), getTransactions(filters)]); }
  catch (error) { return <Message title="Veriler yüklenemedi" description={apiMessage(error)} />; }
  const [flows, summary, result] = data;
  const flowNames = new Map(flows.map(flow => [flow.code, flow.name]));
  if (filters.flow && !flowNames.has(filters.flow)) {
    return <Message title="Akış bulunamadı" description="Seçilen akış bu projenin akış listesinde bulunmuyor." />;
  }
  const metrics = [
    { label: "Toplam işlem", value: summary.total, status: "", delay: "", tone: "neutral" },
    { label: "İşlenmeyi bekleyen", value: summary.pending, status: "pending", delay: "", tone: "neutral" },
    { label: "Devam eden", value: summary.inProgress, status: "inProgress", delay: "", tone: "info" },
    { label: "Tamamlanan", value: summary.completed, status: "completed", delay: "", tone: "good" },
    { label: "Hata bildirilen", value: summary.failed, status: "failed", delay: "", tone: "bad" },
    { label: "Gecikmiş", value: summary.overdue, status: "", delay: "overdue", tone: "warn" },
  ];

  return <>
    <section className="hero"><div><p className="eyebrow">OPERASYON GÖRÜNÜMÜ</p><h1>İşlem akışları</h1>
      <p>Hangi adım tamamlandı, hangisi bekliyor? İşlemlerinizi tek yerden izleyin.</p></div>
      <span className="scope">{flowNames.get(filters.flow) || "Tüm akışlar"}</span>
    </section>
    <section className="metrics" aria-label="İşlem özeti">
      {metrics.map(metric => <Link key={metric.label} prefetch={false} className={`metric ${metric.tone}`}
        href={listHref(filters, { status: metric.status, delay: metric.delay, page: 1 })}>
        <span>{metric.label}</span><strong>{number(metric.value)}</strong>
      </Link>)}
    </section>
    <p className="scope-note">Kartlar seçili akışın tamamını gösterir. Gecikmiş işlemler devam edenlere de dahil olabilir.
      <span>Özet sorgusu: {date(summary.queriedAt)}</span></p>

    <section className="panel">
      <div className="section-head"><div><h2>İşlemler</h2><p>Durum ve gecikme filtreleri bu listeye uygulanır.</p></div>
        <span className="count">{number(result.totalCount)} kayıt</span></div>
      <form key={listHref(filters)} className="filters" method="get" action="/">
        <label>Akış<select name="flow" defaultValue={filters.flow}>
          <option value="">Tüm akışlar</option>{flows.map(flow => <option key={flow.id} value={flow.code}>{flow.name}</option>)}
        </select></label>
        <label>İşlem durumu<select name="status" defaultValue={filters.status}>
          <option value="">Tüm durumlar</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
        <label>Gecikme<select name="delay" defaultValue={filters.delay}>
          <option value="">Tümü</option>{Object.entries(delayLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
        <label>Sayfa boyutu<select name="pageSize" defaultValue={filters.pageSize}>
          {[10, 20, 50, 100].map(size => <option key={size} value={size}>{size} kayıt</option>)}
        </select></label>
        <button className="button" type="submit">Filtrele</button><Link className="reset" href="/" prefetch={false}>Temizle</Link>
      </form>
      <div className="table-wrap"><table><thead><tr>
        <th>İşlem</th><th>Akış</th><th>Durum</th><th>Gecikme</th><th>Oluşturulma</th>
      </tr></thead><tbody>
        {result.items.map(item => <tr key={item.id}>
          <td><Link className="identifier" title={item.transactionId} prefetch={false}
            href={`/transactions/${encodeURIComponent(item.flow)}/${encodeURIComponent(item.transactionId)}`}>{item.transactionId}</Link></td>
          <td><span className="flow-name">{flowNames.get(item.flow) || item.flow}</span><small>{item.flow}</small></td>
          <td><Badge value={item.status} /></td>
          <td><Badge value={item.delay.state} delay />{item.delay.step && <small>{item.delay.step}</small>}</td>
          <td className="nowrap">{date(item.createdAt)}</td>
        </tr>)}
      </tbody></table></div>
      {result.items.length === 0 && <div className="empty"><h3>{result.totalCount ? "Bu sayfada kayıt yok" : "Gösterilecek işlem yok"}</h3>
        <p>Filtreleri değiştirebilir veya bu projeye yeni bir durum bildirimi gönderebilirsiniz.</p>
        {filters.page > 1 && <Link href={listHref(filters, { page: 1 })} prefetch={false}>İlk sayfaya dön</Link>}</div>}
      <div className="pagination"><span>{result.totalPages ? `Sayfa ${result.page} / ${number(result.totalPages)}` : "0 kayıt"}</span>
        <nav aria-label="Sayfalama">
          {filters.page > 1 && <Link className="button secondary" href={listHref(filters, { page: filters.page - 1 })} prefetch={false}>Önceki</Link>}
          {result.hasNextPage && <Link className="button secondary" href={listHref(filters, { page: filters.page + 1 })} prefetch={false}>Sonraki</Link>}
        </nav>
      </div>
    </section>
  </>;
}
```

## 27. frontend/src/app/transactions/[flow]/[transactionId]/page.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiError, apiMessage, getTransaction } from "@/lib/api";
import type { DelayInfo, TransactionDetail } from "@/lib/types";
import { Badge, date, Message, number } from "@/components/ui";

export const dynamic = "force-dynamic";

export default async function Detail({ params }: {
  params: Promise<{ flow: string; transactionId: string }>;
}) {
  const { flow, transactionId } = await params;
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(flow) || !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(transactionId)) notFound();
  let item: TransactionDetail;
  try { item = await getTransaction(flow, transactionId); }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    return <Message title="İşlem yüklenemedi" description={apiMessage(error)} />;
  }
  const delay: DelayInfo = item.delay ?? {
    state: "pending_evaluation", step: null, startedAt: null, deadlineAt: null, checkedAt: null,
  };
  const expected = item.steps.find(step => step.code === delay.step);

  return <>
    <Link className="back-link" href={`/?${new URLSearchParams({ flow })}`} prefetch={false}>← İşlem listesine dön</Link>
    <section className="hero"><div><p className="eyebrow">İŞLEM DETAYI · {item.flow}</p>
      <h1>{item.transactionId}</h1><p>Adım adım ilerleme ve sisteme ulaşan bildirimler.</p></div><Badge value={item.status} /></section>
    <section className="panel detail-summary"><h2>Süre takibi</h2><Badge value={delay.state} delay />
      <dl className="facts">
        <div><dt>Beklenen adım</dt><dd>{expected?.name || delay.step || "—"}</dd></div>
        <div><dt>Bekleme başlangıcı</dt><dd>{date(delay.startedAt)}</dd></div>
        <div><dt>Beklenen son zaman</dt><dd>{date(delay.deadlineAt)}</dd></div>
        <div><dt>Son değerlendirme</dt><dd>{date(delay.checkedAt)}</dd></div>
      </dl>
      {delay.state === "overdue" && <p className="notice">Beklenen tamamlanma bildirimi tanımlanan sürede gözlenmedi.</p>}
      {item.acceptedEventCount > item.processedEventCount && <p className="notice">İşlenmeyi bekleyen bildirim var. Son durum henüz bu görünüme yansımamış olabilir.</p>}
    </section>
    <section className="panel"><div className="section-head"><h2>Akış adımları</h2><span className="count">{item.steps.length} adım</span></div>
      <ol className="timeline">{[...item.steps].sort((a, b) => a.position - b.position).map(step =>
        <li key={step.code} className={step.code === delay.step ? "expected" : ""}>
          <span className={`step-dot ${step.observation}`}>{step.position + 1}</span>
          <div className="step-content"><div className="step-heading"><h3>{step.name}</h3><Badge value={step.observation} /></div>
            <p>{step.maxDelaySeconds === null ? "Başlangıç adımı" : `Önceki adımdan sonra tanınan süre: ${number(step.maxDelaySeconds)} sn`}</p>
            <small>Gerçekleşme: {date(step.occurredAt)}</small>{step.error && <p className="event-error">{step.error}</p>}
          </div>
        </li>)}
      </ol>
    </section>
    <section className="panel"><div className="section-head"><div><h2>Gelen bildirimler</h2>
      <p>{number(item.acceptedEventCount)} kabul edildi · {number(item.processedEventCount)} işlendi</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Adım / Bildirim kimliği</th><th>Durum</th><th>Gerçekleşme</th><th>Bize ulaşma</th><th>Hata</th></tr></thead>
        <tbody>{[...item.events].reverse().map(event => <tr key={event.eventId}>
          <td>{event.step}<small className="mono">{event.eventId}</small></td><td><Badge value={event.status} /></td>
          <td className="nowrap">{date(event.occurredAt)}</td><td className="nowrap">{date(event.receivedAt)}</td>
          <td className="event-error">{event.error || "—"}</td>
        </tr>)}</tbody></table></div>
      {item.events.length === 0 && <div className="empty">Henüz işlenmiş bildirim yok.</div>}
    </section>
  </>;
}
```

## 28. frontend/src/components/flow-form.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
"use client";

import { useActionState, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { createFlowAction } from "@/app/flows/new/actions";
import { initialFlowState } from "@/lib/flow-form";
import { delayDraft, type FlowDetails } from "@/lib/flow-details";

type StepDraft = {
  id: number; code: string; name: string; delayValue: string; delayUnit: string;
};
const blankStep = (id: number): StepDraft => ({ id, code: "", name: "", delayValue: "", delayUnit: "minutes" });

function Field({ id, label, error, children }: {
  id: string; label: string; error?: string; children: ReactNode;
}) {
  return <div className="flow-field"><label htmlFor={id}>{label}</label>{children}
    {error && <small id={`${id}-error`} className="field-error">{error}</small>}
  </div>;
}

export default function FlowForm({ initialFlow }: { initialFlow?: FlowDetails }) {
  const [state, action, pending] = useActionState(createFlowAction, initialFlowState);
  const [code, setCode] = useState("");
  const [name, setName] = useState(initialFlow?.name ?? "");
  const [steps, setSteps] = useState<StepDraft[]>(() => initialFlow
    ? [...initialFlow.steps].sort((a, b) => a.position - b.position).map((step, index) => ({
      id: index, code: step.code, name: step.name, ...delayDraft(step.maxDelaySeconds),
    })) : [blankStep(0), blankStep(1)]);
  const [edited, setEdited] = useState(false);
  const nextId = useRef(initialFlow?.steps.length ?? 2);
  const errors = edited ? {} : state.errors;
  const accessibility = (id: string) => ({
    "aria-invalid": Boolean(errors[id]),
    "aria-describedby": errors[id] ? `${id}-error` : undefined,
  });
  const updateStep = (id: number, change: Partial<StepDraft>) => {
    setEdited(true);
    setSteps(current => current.map(step => step.id === id ? { ...step, ...change } : step));
  };

  if (state.createdCode) return <section className="panel message" role="status">
    <h1>Akış oluşturuldu</h1><p><strong>{state.createdCode}</strong> kaydedildi. İlk durum bildirimi geldiğinde bu akışın işlemleri listelenmeye başlayacak.</p>
    <div className="flow-actions">
      <Link className="button" href="/flows" prefetch={false}>Akışlara dön</Link>
      <Link className="button secondary" href={`/flows/view/${encodeURIComponent(state.createdCode)}`} prefetch={false}>Adımları ve süreleri gör</Link>
      <Link className="button secondary" href={`/?${new URLSearchParams({ flow: state.createdCode })}`} prefetch={false}>İşlemleri göster</Link>
    </div>
  </section>;

  return <form action={action} noValidate onSubmit={() => setEdited(false)} className="panel flow-editor">
    <input type="hidden" name="payload" value={JSON.stringify({ code, name, steps })} />
    <fieldset disabled={pending}>
      <legend className="flow-legend">Akış bilgileri</legend>
      <div className="flow-grid">
        <Field id="flow-name" label="Akış adı" error={errors["flow-name"]}>
          <input id="flow-name" value={name} maxLength={160} required {...accessibility("flow-name")}
            placeholder="Örnek: Sipariş ve fatura takibi" onChange={event => { setName(event.target.value); setEdited(true); }} />
        </Field>
        <Field id="flow-code" label="Akış kodu" error={errors["flow-code"]}>
          <input id="flow-code" value={code} maxLength={64} required {...accessibility("flow-code")}
            placeholder="Örnek: siparis-fatura" autoCapitalize="none" spellCheck={false}
            onChange={event => { setCode(event.target.value); setEdited(true); }} />
        </Field>
      </div>
      <p className="form-help">Kodları durum bildirimi gönderen sistemler kullanacak. Küçük İngilizce harf, rakam ve tire kullanın.</p>

      <div className="flow-steps-head"><h2>Akış adımları</h2><span className="count">{steps.length} / 30 adım</span></div>
      <p className="form-help">Adımları gerçekleşmesini beklediğiniz sırayla ekleyin. Her süre, bir önceki adımın tamamlanma zamanından başlar.</p>
      {errors.steps && <p className="field-error" role="alert">{errors.steps}</p>}

      <ol className="flow-step-list">{steps.map((step, index) => {
        const prefix = `step-${index}`;
        return <li key={step.id} className="flow-step-card">
          <div className="flow-steps-head"><h3>{index + 1}. adım{index === 0 ? " · Başlangıç" : ""}</h3>
            <button className="button secondary" type="button" disabled={steps.length <= 2 || index === 0}
              aria-label={`${index + 1}. adımı kaldır`} onClick={() => {
                setSteps(current => current.filter(item => item.id !== step.id)); setEdited(true);
              }}>Kaldır</button>
          </div>
          <div className="flow-grid">
            <Field id={`${prefix}-name`} label="Adım adı" error={errors[`${prefix}-name`]}>
              <input id={`${prefix}-name`} value={step.name} maxLength={160} required {...accessibility(`${prefix}-name`)}
                placeholder={index === 0 ? "Örnek: ERP kaydı tamamlandı" : "Örnek: Fatura oluşturuldu"}
                onChange={event => updateStep(step.id, { name: event.target.value })} />
            </Field>
            <Field id={`${prefix}-code`} label="Adım kodu" error={errors[`${prefix}-code`]}>
              <input id={`${prefix}-code`} value={step.code} maxLength={64} required {...accessibility(`${prefix}-code`)}
                placeholder={index === 0 ? "Örnek: erp-completed" : "Örnek: invoice-created"} autoCapitalize="none" spellCheck={false}
                onChange={event => updateStep(step.id, { code: event.target.value })} />
            </Field>
          </div>
          {index === 0 ? <p className="form-help">İlk adım için önceki adıma bağlı bekleme süresi tanımlanmaz.</p>
            : <div className="flow-grid delay-inputs">
              <Field id={`${prefix}-delay`} label="Önceki adımdan sonra tanınan süre" error={errors[`${prefix}-delay`]}>
                <input id={`${prefix}-delay`} type="number" min={1} step={1} required value={step.delayValue}
                  {...accessibility(`${prefix}-delay`)} onChange={event => updateStep(step.id, { delayValue: event.target.value })} />
              </Field>
              <Field id={`${prefix}-unit`} label="Süre birimi">
                <select id={`${prefix}-unit`} value={step.delayUnit} onChange={event => updateStep(step.id, { delayUnit: event.target.value })}>
                  <option value="seconds">Saniye</option><option value="minutes">Dakika</option><option value="hours">Saat</option>
                </select>
              </Field>
            </div>}
        </li>;
      })}</ol>

      <button className="button secondary" type="button" disabled={steps.length >= 30} onClick={() => {
        const id = nextId.current++; setSteps(current => [...current, blankStep(id)]); setEdited(true);
      }}>+ Adım ekle</button>
      <p className="form-help">Her adım için en az 1 saniye, en fazla 24 saat tanımlayabilirsiniz.</p>
      {!edited && state.message && <p className="flow-error" role="alert">{state.message}</p>}
      <div className="flow-actions">
        <button className="button" type="submit">{pending ? "Kaydediliyor…" : "Akışı oluştur"}</button>
        <Link href="/flows" prefetch={false}>Akış listesine dön</Link>
      </div>
    </fieldset>
  </form>;
}
```

## 29. frontend/src/components/flow-test.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
"use client";

import { useRef, useState, useTransition } from "react";
import Link from "next/link";
import { sendTestEvent } from "@/app/flows/test-actions";
import type { FlowDetails, TestEventPayload } from "@/lib/flow-details";

export default function FlowTest({ flow }: { flow: FlowDetails }) {
  const steps = [...flow.steps].sort((a, b) => a.position - b.position);
  const [run, setRun] = useState<{ id: string; accepted: number } | null>(null);
  const [attempt, setAttempt] = useState<TestEventPayload | null>(null);
  const [lastPayload, setLastPayload] = useState<TestEventPayload | null>(null);
  const [message, setMessage] = useState("");
  const [retry, setRetry] = useState(false);
  const [pending, startTransition] = useTransition();
  const inFlight = useRef(false);

  function send(payload: TestEventPayload) {
    if (inFlight.current) return;
    inFlight.current = true;
    setAttempt(payload); setLastPayload(payload); setMessage(""); setRetry(false);
    startTransition(async () => {
      try {
        const result = await sendTestEvent(payload);
        if (result.ok) {
          setRun(current => current?.id === payload.transactionId ? { ...current, accepted: current.accepted + 1 } : current);
          setAttempt(null);
          setMessage(result.duplicate ? "Bildirim daha önce kabul edilmiş; tekrar kayıt oluşturulmadı." : "Bildirim kabul edildi. Worker işledikten sonra işlem detayına yansıyacak.");
        } else { setMessage(result.message); setRetry(result.retry); }
      } catch {
        setMessage("Panele ulaşılamadı. Aynı bildirimi tekrar gönderebilirsiniz."); setRetry(true);
      } finally { inFlight.current = false; }
    });
  }

  function sendStep(transactionId: string, index: number) {
    send({ eventId: crypto.randomUUID(), flow: flow.code, transactionId,
      step: steps[index].code, occurredAt: new Date().toISOString() });
  }

  return <section className="panel detail-summary">
    <h2>Adım adım deneme</h2>
    <p className="form-help">Her düğme bir adımın tamamlanma bildirimini gönderir. TEST- kimlikli gerçek işlem kayıtları oluşur ve paneldeki sayılara dahil edilir.</p>
    <div className="flow-actions">
      <button className="button secondary" type="button" disabled={pending || steps.length === 0} onClick={() => {
        if (inFlight.current) return;
        const id = `TEST-${crypto.randomUUID()}`;
        setRun({ id, accepted: 0 }); sendStep(id, 0);
      }}>{run ? "Yeni test başlat" : "Testi başlat"}</button>
      {run && !attempt && run.accepted < steps.length && <button className="button" type="button" disabled={pending}
        onClick={() => sendStep(run.id, run.accepted)}>Bildir: {steps[run.accepted].name}</button>}
      {attempt && retry && <button className="button" type="button" disabled={pending}
        onClick={() => send(attempt)}>Aynı bildirimi tekrar gönder</button>}
    </div>
    <p className="form-help" role="status" aria-live="polite">{pending ? "Bildirim gönderiliyor…" : message}</p>
    {run && <div className="test-progress">
      <p><strong>{run.accepted} / {steps.length}</strong> bildirim kabul edildi.</p>
      <p className="mono">{run.id}</p>
      <Link href={`/transactions/${encodeURIComponent(flow.code)}/${encodeURIComponent(run.id)}`}
        target="_blank" rel="noopener noreferrer" prefetch={false}>İşlem detayını yeni sekmede aç →</Link>
      {run.accepted === steps.length && <p>Tüm adımların bildirimi kabul edildi. İşlenme sonucunu işlem detayında izleyebilirsiniz.</p>}
    </div>}
    {lastPayload && <details className="event-example"><summary>Son bildirimin JSON içeriği</summary>
      <pre>{JSON.stringify({ ...lastPayload, status: "completed" }, null, 2)}</pre>
    </details>}
  </section>;
}
```

## 30. frontend/src/components/refresh-controls.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
"use client";

import { useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";

export default function RefreshControls() {
  const router = useRouter();
  const [automatic, setAutomatic] = useState(false);
  const [pending, startTransition] = useTransition();
  const refresh = () => startTransition(() => router.refresh());

  useEffect(() => {
    if (!automatic || pending) return;
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") startTransition(() => router.refresh());
    }, 10000);
    return () => clearInterval(timer);
  }, [automatic, pending, router]);

  return <div className="refresh-controls">
    <label className="toggle"><input type="checkbox" checked={automatic}
      onChange={event => setAutomatic(event.target.checked)} /> 10 sn’de bir yenile</label>
    <button className="button secondary" type="button" onClick={refresh} disabled={pending}>
      {pending ? "Yenileniyor…" : "Yenile"}
    </button>
  </div>;
}
```

## 31. frontend/src/components/ui.tsx

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```tsx
import Link from "next/link";
import type { Filters } from "@/lib/types";

export const statusLabels: Record<string, string> = {
  pending: "İşlenmeyi bekliyor", inProgress: "Devam ediyor",
  completed: "Tamamlandı", failed: "Hata bildirildi",
};
export const delayLabels: Record<string, string> = {
  none: "Yok", waiting: "Bekleniyor", overdue: "Gecikmiş",
  pending_processing: "Bildirim işleniyor", pending_evaluation: "Kontrol bekleniyor",
};
export function Badge({ value, delay = false }: { value: string; delay?: boolean }) {
  const tone = value === "completed" ? "good" : value === "failed" ? "bad"
    : value === "overdue" ? "warn" : ["waiting", "inProgress"].includes(value) ? "info" : "neutral";
  const label = value === "not_observed" ? "Bildirim yok"
    : (delay ? delayLabels[value] : statusLabels[value]) ?? value;
  return <span className={`badge ${tone}`}>{label}</span>;
}
export function date(value: string | null | undefined) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "short", timeStyle: "medium", timeZone: "Europe/Istanbul",
  }).format(new Date(value));
}
export const number = (value: number) => new Intl.NumberFormat("tr-TR").format(value);
export function listHref(filters: Filters, changes: Partial<Filters> = {}) {
  const next = { ...filters, ...changes };
  return `/?${new URLSearchParams({ ...next, page: String(next.page), pageSize: String(next.pageSize) })}`;
}
export function Message({ title, description }: { title: string; description: string }) {
  return <section className="panel message" role="status">
    <h1>{title}</h1><p>{description}</p>
    <Link className="button" href="/" prefetch={false}>İşlem listesine dön</Link>
  </section>;
}
```

## 32. frontend/src/lib/flow-details.ts

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```typescript
export type FlowDetails = {
  id: string;
  code: string;
  name: string;
  steps: { code: string; name: string; position: number; maxDelaySeconds: number | null }[];
};

export type TestEventPayload = {
  eventId: string; flow: string; transactionId: string;
  step: string; occurredAt: string;
};

export type TestEventResult =
  | { ok: true; duplicate: boolean }
  | { ok: false; message: string; retry: boolean };

export function duration(seconds: number | null) {
  if (seconds === null) return "Başlangıç adımı";
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;
  return [hours && `${hours} saat`, minutes && `${minutes} dakika`, rest && `${rest} saniye`].filter(Boolean).join(" ");
}

export function delayDraft(seconds: number | null) {
  if (seconds === null) return { delayValue: "", delayUnit: "minutes" };
  if (seconds % 3600 === 0) return { delayValue: String(seconds / 3600), delayUnit: "hours" };
  if (seconds % 60 === 0) return { delayValue: String(seconds / 60), delayUnit: "minutes" };
  return { delayValue: String(seconds), delayUnit: "seconds" };
}

export function parseTestEvent(value: unknown): TestEventPayload | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const raw = value as Record<string, unknown>;
  const { eventId, flow, transactionId, step, occurredAt } = raw;
  if (typeof eventId !== "string" || typeof flow !== "string" || typeof transactionId !== "string"
    || typeof step !== "string" || typeof occurredAt !== "string") return null;
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const code = /^[a-z][a-z0-9-]{0,63}$/;
  if (!uuid.test(eventId) || eventId === "00000000-0000-0000-0000-000000000000"
    || !transactionId.startsWith("TEST-") || !uuid.test(transactionId.slice(5))
    || !code.test(flow) || !code.test(step)) return null;
  if (occurredAt.length > 40 || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(occurredAt)
    || !Number.isFinite(Date.parse(occurredAt))) return null;
  return { eventId, flow, transactionId, step, occurredAt };
}
```

## 33. frontend/src/lib/flow-form.ts

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```typescript
export type CreateFlowInput = {
  code: string;
  name: string;
  steps: { code: string; name: string; maxDelaySeconds: number | null }[];
};

export type FlowFormState = {
  message: string;
  errors: Record<string, string>;
  createdCode?: string;
};

export const initialFlowState: FlowFormState = { message: "", errors: {} };

export function validateFlow(value: unknown):
  | { ok: true; data: CreateFlowInput }
  | { ok: false; errors: Record<string, string> } {
  const errors: Record<string, string> = {};
  const record = (input: unknown): Record<string, unknown> =>
    input !== null && typeof input === "object" && !Array.isArray(input)
      ? input as Record<string, unknown> : {};
  const text = (input: unknown) => typeof input === "string" ? input.trim() : "";
  const readCode = (input: unknown, field: string) => {
    const code = text(input);
    if (!/^[a-z][a-z0-9-]{0,63}$/.test(code)) {
      errors[field] = "Küçük İngilizce harfle başlayın; harf, rakam ve tire kullanın. En fazla 64 karakter.";
    }
    return code;
  };
  const readName = (input: unknown, field: string) => {
    const name = text(input);
    if (!name || name.length > 160) errors[field] = "1–160 karakterlik bir ad yazın.";
    return name;
  };
  const raw = record(value);
  const code = readCode(raw.code, "flow-code");
  const name = readName(raw.name, "flow-name");
  if (!Array.isArray(raw.steps) || raw.steps.length < 2 || raw.steps.length > 30) {
    errors.steps = "Akış 2–30 sıralı adımdan oluşmalı.";
    return { ok: false, errors };
  }
  const codes = new Set<string>();
  const steps = raw.steps.map((value, index) => {
    const step = record(value);
    const code = readCode(step.code, `step-${index}-code`);
    const name = readName(step.name, `step-${index}-name`);
    if (codes.has(code)) errors[`step-${index}-code`] = "Adım kodları birbirinden farklı olmalı.";
    codes.add(code);

    let maxDelaySeconds: number | null = null;
    if (index > 0) {
      const amount = text(step.delayValue);
      const multiplier = step.delayUnit === "seconds" ? 1
        : step.delayUnit === "minutes" ? 60 : step.delayUnit === "hours" ? 3600 : 0;
      const seconds = Number(amount) * multiplier;
      if (!/^\d+$/.test(amount) || !multiplier || !Number.isSafeInteger(seconds)
        || seconds < 1 || seconds > 86400) {
        errors[`step-${index}-delay`] = "Süreyi tam sayı olarak girin. İzin verilen aralık: 1 saniye–24 saat.";
      } else maxDelaySeconds = seconds;
    }
    return { code, name, maxDelaySeconds };
  });
  return Object.keys(errors).length ? { ok: false, errors } : { ok: true, data: { code, name, steps } };
}
```

## 34. frontend/src/lib/types.ts

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```typescript
export type TransactionStatus = "pending" | "inProgress" | "completed" | "failed";
export type DelayState = "none" | "waiting" | "overdue" | "pending_processing" | "pending_evaluation";

export type DelayInfo = {
  state: DelayState;
  step: string | null;
  startedAt: string | null;
  deadlineAt: string | null;
  checkedAt: string | null;
};

export type Flow = { id: string; code: string; name: string; stepCount: number };
export type Summary = {
  total: number; pending: number; inProgress: number;
  completed: number; failed: number; overdue: number; queriedAt: string;
};
export type TransactionItem = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  createdAt: string; updatedAt: string; delay: DelayInfo;
};
export type TransactionPage = {
  page: number; pageSize: number; totalCount: number; totalPages: number;
  hasNextPage: boolean; items: TransactionItem[];
};
export type TimelineStep = {
  code: string; name: string; position: number; maxDelaySeconds: number | null;
  observation: "not_observed" | "completed" | "failed";
  occurredAt: string | null; error: string | null;
};
export type ObservedEvent = {
  eventId: string; step: string; status: "completed" | "failed";
  occurredAt: string; receivedAt: string; error: string | null;
};
export type TransactionDetail = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  acceptedEventCount: number; processedEventCount: number;
  steps: TimelineStep[]; events: ObservedEvent[]; delay: DelayInfo | null;
};
export type Filters = { flow: string; status: string; delay: string; page: number; pageSize: number };
```

## 35. frontend/package.json

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```json
{
  "name": "integraops-panel",
  "version": "0.1.0",
  "private": true,
  "scripts": {
    "dev": "next dev --hostname 127.0.0.1",
    "build": "next build",
    "start": "next start --hostname 127.0.0.1",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": {
    "next": "16.3.5",
    "react": "19.3.0",
    "react-dom": "19.3.0",
    "server-only": "0.0.1"
  },
  "devDependencies": {
    "@types/node": "24.13.4",
    "@types/react": "19.3.0",
    "@types/react-dom": "19.3.0",
    "typescript": "5.9.3"
  }
}
```

## 36. frontend/tsconfig.json

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```json
{
  "compilerOptions": {
    "target": "ES2017",
    "lib": [
      "dom",
      "dom.iterable",
      "esnext"
    ],
    "strict": true,
    "noEmit": true,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "module": "esnext",
    "moduleResolution": "bundler",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "jsx": "react-jsx",
    "incremental": true,
    "plugins": [
      {
        "name": "next"
      }
    ],
    "paths": {
      "@/*": [
        "./src/*"
      ]
    },
    "allowJs": true
  },
  "include": [
    "next-env.d.ts",
    "**/*.ts",
    "**/*.tsx",
    ".next/types/**/*.ts",
    ".next/dev/types/**/*.ts"
  ],
  "exclude": [
    "node_modules"
  ]
}
```

## 37. frontend/.gitignore

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```text
node_modules/
.next/
.env*
*.tsbuildinfo
next-env.d.ts
```
