// Alarm listesi ve detayında ortak etiketler.
export function alarmState(a: { resolved_at: string | null; acknowledged_at: string | null }) {
  if (a.resolved_at) return { tone: "good", label: "Kapandı" };
  if (a.acknowledged_at) return { tone: "info", label: "Açık, görüldü" };
  return { tone: "warn", label: "Açık" };
}
export const alarmKind = (kind: string) => (kind === "overdue" ? "Süre aşıldı" : "Hata bildirildi");
