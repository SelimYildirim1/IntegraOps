export type Role = "owner" | "operator" | "viewer";
export type Project = { id: string; name: string; role: Role };
export type Access = { projectId: string; userId: string; role: Role };
export type ActionState = { message: string; ok?: boolean; secret?: string };
export type Member = { id: string; name: string; email: string; role: Role; activated: boolean };
export type Assignee = { id: string; name: string; role: Role };
export type SavedView = { id: string; kind: "transactions" | "alarms"; name: string; query: string; created_at: string };
export type Calendar = {
  id: string; name: string; timezone: string; work_days: number[]; start_time: string; end_time: string;
  version_count: number; holidays: { day: string; name: string }[];
};
export type BreachReport = {
  items: { business_id: string; flow: string; step_code: string; started_at: string; completed_at: string;
    elapsed_seconds: number; allowance_seconds: number; group_code: string | null; is_test: boolean }[];
  total: number; page: number; pageSize: number;
  byStep: { flow: string; step_code: string; breached: number; total: number; avg_elapsed: number; allowance: number }[];
};
export type AgentInfo = {
  name: string; version: string; host: string; source_kind: string; pending: number; sent_total: number; dead_total: number; retry_total: number;
  last_error: string | null; last_success_at: string | null; config: Record<string, unknown>; first_seen_at: string; seen_at: string; stale: boolean;
};
export type ReconciliationRuns = {
  items: { id: string; name: string; flow: string; window_from: string; window_to: string; grace_seconds: number; source_count: number;
    matched_count: number; new_count: number; repeated_count: number; resolved_count: number; version: string; created_at: string }[];
  page: number; pageSize: number;
  lastRuns: { name: string; flow: string; window_to: string; created_at: string }[];
};
export type ReconciliationDiscrepancies = {
  items: { id: string; flow: string; transaction_id: string; kind: string; step_code: string; detail: string; source_occurred_at: string | null;
    first_seen_at: string; last_seen_at: string; resolved_at: string | null; seen_count: number; known: boolean }[];
  total: number; page: number; pageSize: number; counts: { open: number; missing_transactions: number };
};
export type ReportOverview = {
  from: string; to: string; flow: string | null; test: string;
  totals: { transactions: number; completed: number; failed: number; in_progress: number };
  alarms: { overdue: number; failed: number; still_open: number; mtta_seconds: number | null; mtta_p50_seconds: number | null;
    mttr_seconds: number | null; mttr_p50_seconds: number | null; acknowledged: number; resolved: number };
  compliance: { measured: number; breached: number; out_of_order: number };
  steps: { flow: string; step_code: string; measured: number; breached: number; compliance: number; p50: number; p90: number; p95: number; max: number; allowance: number }[];
  topFailed: { flow: string; step_code: string; error: string; count: number; transactions: number }[];
  topOverdue: { flow: string; step_code: string; count: number; avg_late_seconds: number | null }[];
  recurring: { flow: string; business_id: string; alarms: number; failed: number; overdue: number; last_opened_at: string }[];
  byGroup: { group: string; transactions: number; measured: number; breached: number; compliance: number | null }[];
  quality: { events: number; late_events: number; lag_p95_seconds: number | null; future_events: number; out_of_order_steps: number;
    open_discrepancies: number; discrepancies_found: number };
};
export type ReportTrend = { period: string; buckets: { start: string; transactions: number; measured: number; breached: number; alarms: number; p50_seconds: number | null }[] };
export type DigestInfo = {
  settings: { digest_period: "none" | "daily" | "weekly"; digest_hour: number; digest_timezone: string; updated_at: string | null };
  sent: { period: string; period_from: string; period_to: string; recipients: number; sent_at: string; triggered_by: string }[];
};
export type RetentionInfo = {
  policy: { finished_transaction_days: number | null; notification_days: number | null; audit_days: number | null; updated_at: string | null };
  runs: { ran_at: string; transactions_deleted: number; deliveries_deleted: number; audit_deleted: number }[];
};
export type ProjectKey = { id: string; name: string; scope: string; key_prefix: string; revoked_at: string | null };
export type Channel = { id: string; name: string; kind: string; target: string; enabled: boolean };
export type Alarm = {
  id: string; flow: string; business_id: string; kind: string; step_code: string; error: string | null;
  opened_at: string; deadline_at: string | null; resolved_at: string | null; resolution: string | null;
  acknowledged_at: string | null; assigned_to: string | null; assigned_at: string | null; assigned_name: string | null; is_test: boolean;
};
export type AlarmPage = { items: Alarm[]; total: number; page: number; pageSize: number };
export type AlarmDetail = {
  alarm: Alarm;
  events: { id: string; kind: string; created_at: string; actor: string | null }[];
  deliveries: { id: string; channel: string; kind: string; event: string; state: string; attempts: number;
    available_at: string; last_error: string | null; sent_at: string | null }[];
  notes: { id: string; body: string; created_at: string; author: string }[];
};
export type Operations = {
  counts: { outbox_pending: number; oldest_outbox_at: string | null; events_pending: number;
    notifications_pending: number; notifications_dead: number; worker_seen_at: string | null; checked_at: string };
  audit: { action: string; target: string; created_at: string; actor: string | null }[];
};
export const roleNames: Record<Role, string> = { owner: "Proje sahibi", operator: "Operatör", viewer: "İzleyici" };
export const eventNames: Record<string, string> = {
  opened: "Açıldı", resolved: "Kapandı", acknowledged: "Görüldü", assigned: "Sorumlu atandı", unassigned: "Sorumlu kaldırıldı", noted: "Not eklendi",
};
export const deliveryNames: Record<string, string> = {
  pending: "Gönderim bekliyor", sending: "Gönderiliyor", sent: "Gönderildi", dead: "Denemeler durdu", cancelled: "İptal edildi",
};
