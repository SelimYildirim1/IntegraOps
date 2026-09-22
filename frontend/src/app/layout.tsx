import type { Metadata } from "next";
import Link from "next/link";
import { cookies } from "next/headers";
import { IBM_Plex_Sans, IBM_Plex_Mono } from "next/font/google";
import PanelToolbar from "@/components/panel-toolbar";
import { logoutAction } from "@/app/account-actions";
import { api, getProjects } from "@/lib/api";
import type { AlarmPage } from "@/lib/pilot-types";
import "./globals.css";

const sans = IBM_Plex_Sans({
  subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-plex-sans", display: "swap",
});
const mono = IBM_Plex_Mono({
  subsets: ["latin", "latin-ext"], weight: ["400", "500"], variable: "--font-plex-mono", display: "swap",
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
  return <html lang="tr" className={`${sans.variable} ${mono.variable}`}><body>
    <a className="skip-link" href="#content">İçeriğe geç</a>
    <header className="appbar">
      <div className="appbar-row">
        <Link className="brand" href="/" prefetch={false}><span className="brand-mark">io</span>IntegraOps</Link>
        {loggedIn && <PanelToolbar openAlarms={openAlarms} />}
        {loggedIn && <div className="appbar-right">
          <Link className="project-chip" href="/projects" prefetch={false} title="Projeyi değiştir">
            <span>{projectName || "Proje seçin"}</span>
          </Link>
          <Link className="plain" href="/organization" prefetch={false}>Organizasyon</Link>
          <Link className="plain" href="/account" prefetch={false}>Hesabım</Link>
          <form action={logoutAction}><button className="text-button" type="submit">Çıkış</button></form>
        </div>}
      </div>
    </header>
    <main id="content" className="shell">{children}</main>
    <footer className="footer"><span>IntegraOps, olay bazlı işlem takibi</span><span>Saatler Türkiye saatiyle gösterilir.</span></footer>
  </body></html>;
}
