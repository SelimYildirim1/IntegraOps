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
