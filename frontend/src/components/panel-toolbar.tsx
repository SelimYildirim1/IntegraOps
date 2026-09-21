"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

// Uygulama çubuğundaki ana gezinme. Etkin sekme, açık alarm sayısıyla birlikte gösterilir.
export default function PanelToolbar({ openAlarms }: { openAlarms: number | null }) {
  const pathname = usePathname();
  if (pathname === "/login") return null;
  const tabs: [string, string][] = [
    ["/", "İşlemler"], ["/flows", "Akışlar"], ["/alarms", "Alarmlar"], ["/settings", "Ayarlar"], ["/operations", "Sistem"],
  ];
  return <nav className="appnav" aria-label="Ana menü">
    {tabs.map(([href, label]) => {
      const active = href === "/"
        ? pathname === "/" || pathname.startsWith("/transactions/")
        : pathname === href || pathname.startsWith(href + "/");
      return <Link key={href} href={href} prefetch={false} aria-current={active ? "page" : undefined}>
        {label}
        {href === "/alarms" && openAlarms ? <span className="pill" aria-label={`${openAlarms} açık alarm`}>{openAlarms}</span> : null}
      </Link>;
    })}
  </nav>;
}
