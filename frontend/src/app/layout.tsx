import type { Metadata } from "next";
import Link from "next/link";
import { cookies } from "next/headers";
import { Manrope, JetBrains_Mono } from "next/font/google";
import PanelToolbar from "@/components/panel-toolbar";
import Icon from "@/components/icons";
import { logoutAction } from "@/app/account-actions";
import { api, getProjects } from "@/lib/api";
import type { AlarmPage } from "@/lib/pilot-types";
import "./globals.css";

const sans = Manrope({
  subsets: ["latin", "latin-ext"], weight: ["400", "500", "600", "700"], variable: "--font-sans-src", display: "swap",
});
const mono = JetBrains_Mono({
  subsets: ["latin", "latin-ext"], weight: ["400", "500"], variable: "--font-mono-src", display: "swap",
});

export const metadata: Metadata = { title: "IntegraOps", robots: { index: false, follow: false } };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const jar = await cookies();
  const loggedIn = Boolean(jar.get("io_session")?.value);
  const projectCookie = jar.get("io_project")?.value;
  let projectName = "";
  let openAlarms: number | null = null;
  if (loggedIn) {
    try {
      const projects = await getProjects();
      const selected = projects.find(p => p.id === projectCookie) ?? projects[0];
      projectName = selected?.name ?? "";
      // Alarm sayısı yalnızca seçili proje varken sorulur; sayfa yönlendirmesi tetiklenmez.
      if (selected && projectCookie === selected.id) {
        openAlarms = (await api<AlarmPage>("/alarms?" + new URLSearchParams({ state: "open", page: "1" }))).total;
      }
    } catch { /* Süresi dolan oturumu sayfa kendisi ele alır; giriş ekranı erişilebilir kalır. */ }
  }
  const brand = <Link className="brand" href="/" prefetch={false}><span className="brand-mark" aria-hidden="true" />IntegraOps</Link>;
  return <html lang="tr" className={`${sans.variable} ${mono.variable}`}><body>
    <a className="skip-link" href="#content">İçeriğe geç</a>
    {loggedIn ? <div className="frame">
      <aside className="sidebar">
        <div className="sidebar-top">
          {brand}
          <Link className="project-switch" href="/projects" prefetch={false} title="Projeyi değiştir">
            <Icon name="layers" size={16} />
            <span className="project-name">{projectName || "Proje seçin"}</span>
            <Icon name="down" size={14} className="project-caret" />
          </Link>
        </div>
        <PanelToolbar openAlarms={openAlarms} />
        <div className="sidebar-foot">
          <Link href="/organization" prefetch={false}><Icon name="building" size={18} /><span>Organizasyon</span></Link>
          <Link href="/account" prefetch={false}><Icon name="user" size={18} /><span>Hesabım</span></Link>
          <form action={logoutAction}><button type="submit"><Icon name="logout" size={18} /><span>Çıkış</span></button></form>
        </div>
      </aside>
      <div className="frame-main">
        <main id="content" className="shell">{children}</main>
        <footer className="footer"><span>IntegraOps</span><span>Saatler Türkiye saatiyle gösterilir.</span></footer>
      </div>
    </div> : <div className="auth-frame">
      <header className="auth-top">{brand}</header>
      <main id="content" className="shell">{children}</main>
    </div>}
  </body></html>;
}
