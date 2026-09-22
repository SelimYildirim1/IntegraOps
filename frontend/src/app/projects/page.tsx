import Link from "next/link";
import { cookies } from "next/headers";
import { api, apiMessage, getProjects } from "@/lib/api";
import type { MyOrganization, Onboarding } from "@/lib/pilot-types";
import { roleNames } from "@/lib/pilot-types";
import { Message, PageHead } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { createProjectAction, switchProject } from "@/app/account-actions";

export const dynamic = "force-dynamic";

export default async function ProjectsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  let projects, organizations: MyOrganization[] = [], onboarding: Onboarding | null = null;
  const welcome = (await searchParams).welcome === "1";
  try {
    [projects, organizations] = await Promise.all([getProjects(), api<MyOrganization[]>("/me/organizations", "GET", undefined, false)]);
  } catch (e) { return <Message title="Projeler yüklenemedi" description={apiMessage(e)} />; }
  const current = (await cookies()).get("io_project")?.value;
  if (current && projects.some(p => p.id === current)) {
    try { onboarding = await api<Onboarding>("/onboarding"); } catch { /* checklist is optional */ }
  }
  const owned = organizations.filter(o => o.role === "owner" || o.role === "admin");
  return <>
    <PageHead title="Projeler" lede="Her projenin akışları, anahtarları ve kullanıcı yetkileri birbirinden ayrıdır. Çalışmak istediğiniz projeyi seçin.">
      {organizations.length > 0 && <Link className="button secondary" href="/organization" prefetch={false}>Organizasyon</Link>}
    </PageHead>
    {welcome && <p className="callout good">Hesabınız açıldı ve ilk projeniz hazır. Aşağıdaki başlangıç adımları sizi ilk bildirime götürür.</p>}
    {onboarding && onboarding.completed < onboarding.total && <section className="surface" id="onboarding">
      <div className="surface-head"><div><h2>Başlangıç adımları</h2><p>Seçili projede ilk bildirimin görünmesi için gereken adımlar.</p></div><span className="count">{onboarding.completed} / {onboarding.total} tamamlandı</span></div>
      <div className="surface-body"><ol className="checklist">
        {onboarding.steps.map(s => <li key={s.key} className={s.done ? "done" : ""}>
          <span className="check" aria-hidden="true">{s.done ? "✓" : ""}</span>
          {s.done ? <span>{s.title}</span> : <Link href={s.href} prefetch={false}>{s.title}</Link>}
        </li>)}
      </ol></div>
    </section>}
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Üye olduğunuz projeler</h2><span className="count">{projects.length} proje</span></div>
        <div className="surface-body">
          <div className="project-list">
            {projects.map(p => <form action={switchProject} className="project-row" key={p.id}>
              <input type="hidden" name="project" value={p.id} />
              <div><strong>{p.name}</strong><p>{roleNames[p.role]}{p.organization && organizations.length > 1 ? ` · ${p.organization}` : ""}{p.id === current && ", şu an açık"}</p></div>
              <button className={`button ${p.id === current ? "secondary" : ""}`} type="submit">{p.id === current ? "Devam et" : "Projeyi aç"}</button>
            </form>)}
          </div>
          {projects.length === 0 && <div className="empty"><h3>Henüz bir projeye üye değilsiniz</h3><p>Bir proje sahibi sizi ekleyebilir ya da sağdan kendi projenizi oluşturabilirsiniz.</p></div>}
        </div>
      </section>
      <section className="surface">
        <div className="surface-head"><h2>Yeni proje</h2></div>
        <div className="surface-body">
          <p className="help">Yeni projenin sahibi siz olursunuz. Kullanıcıları ve anahtarları daha sonra Ayarlar'dan eklersiniz.</p>
          <ActionForm action={createProjectAction} label="Proje oluştur">
            <div className="field"><label htmlFor="p-name">Proje adı</label><input id="p-name" name="name" required maxLength={160} placeholder="Örnek: Sipariş entegrasyonu" /></div>
            {owned.length > 1 && <div className="field"><label htmlFor="p-org">Organizasyon</label>
              <select id="p-org" name="organizationId" defaultValue={owned[0].id}>{owned.map(o => <option key={o.id} value={o.id}>{o.name}</option>)}</select></div>}
            {owned.length === 1 && <input type="hidden" name="organizationId" value={owned[0].id} />}
          </ActionForm>
        </div>
      </section>
    </div>
  </>;
}
