"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import Icon, { type IconName } from "@/components/icons";

// Kenar çubuğundaki ana gezinme. Etkin sayfa vurgulanır; Alarmlar açık alarm sayısını taşır.
export default function PanelToolbar({ openAlarms }: { openAlarms: number | null }) {
  const pathname = usePathname();
  if (pathname === "/login") return null;
  const tabs: [string, string, IconName][] = [
    ["/", "İşlemler", "activity"], ["/flows", "Akışlar", "workflow"], ["/alarms", "Alarmlar", "bell"], ["/reports", "Raporlar", "chart"],
    ["/reconciliation", "Mutabakat", "scale"], ["/connections", "Bağlantılar", "plug"], ["/settings", "Ayarlar", "sliders"], ["/operations", "Sistem", "server"],
    ["/developers", "Geliştirici", "code"],
  ];
  return <nav className="sidenav" aria-label="Ana menü">
    {tabs.map(([href, label, icon]) => {
      const active = href === "/"
        ? pathname === "/" || pathname.startsWith("/transactions/")
        : pathname === href || pathname.startsWith(href + "/");
      return <Link key={href} href={href} prefetch={false} aria-current={active ? "page" : undefined}>
        <Icon name={icon} size={18} />
        <span>{label}</span>
        {href === "/alarms" && openAlarms ? <span className="pill" aria-label={`${openAlarms} açık alarm`}>{openAlarms}</span> : null}
      </Link>;
    })}
  </nav>;
}
