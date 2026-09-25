"use client";

import { useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import Icon from "@/components/icons";

// Sayfayı elle ya da 10 saniyede bir yeniler. Sekme görünür değilken otomatik yenileme atlanır.
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
    <label className="toggle" title="Sekme açıkken 10 saniyede bir yeniler"><input type="checkbox" checked={automatic}
      onChange={event => setAutomatic(event.target.checked)} /> Otomatik</label>
    <button className={`button secondary${pending ? " spinning" : ""}`} type="button" onClick={refresh} disabled={pending} aria-label="Yenile">
      <Icon name="refresh" /><span>{pending ? "Yenileniyor" : "Yenile"}</span>
    </button>
  </div>;
}
