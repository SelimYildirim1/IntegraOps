import { cookies } from "next/headers";
import { apiMessage, getProjects } from "@/lib/api";
import { roleNames } from "@/lib/pilot-types";
import { Message, PageHead } from "@/components/ui";
import ActionForm from "@/components/action-form";
import { createProjectAction, switchProject } from "@/app/account-actions";

export const dynamic = "force-dynamic";

export default async function ProjectsPage() {
  let projects;
  try { projects = await getProjects(); }
  catch (e) { return <Message title="Projeler yüklenemedi" description={apiMessage(e)} />; }
  const current = (await cookies()).get("io_project")?.value;
  return <>
    <PageHead title="Projeler" lede="Her projenin akışları, anahtarları ve kullanıcı yetkileri birbirinden ayrıdır. Çalışmak istediğiniz projeyi seçin." />
    <div className="two-col">
      <section className="surface">
        <div className="surface-head"><h2>Üye olduğunuz projeler</h2><span className="count">{projects.length} proje</span></div>
        <div className="surface-body">
          <div className="project-list">
            {projects.map(p => <form action={switchProject} className="project-row" key={p.id}>
              <input type="hidden" name="project" value={p.id} />
              <div><strong>{p.name}</strong><p>{roleNames[p.role]}{p.id === current && ", şu an açık"}</p></div>
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
          </ActionForm>
        </div>
      </section>
    </div>
  </>;
}
