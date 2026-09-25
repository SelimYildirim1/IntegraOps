export type Role = "owner" | "operator" | "viewer";
export type Project = { id: string; name: string; role: Role; organization_id?: string; organization?: string };
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
export type ProjectKey = { id: string; name: string; scope: string; key_prefix: string; revoked_at: string | null;
  last_used_at?: string | null; expires_at?: string | null; rotated_to?: string | null; connection_id?: string | null; connection?: string | null };
export type Connection = {
  id: string; name: string; kind: string; description: string; template: Record<string, string> | null; template_code: string | null; contract_version: number;
  client_name: string | null; client_version: string | null; events_total: number; rejected_total: number; last_event_at: string | null; last_ping_at: string | null;
  last_error: string | null; last_error_at: string | null; created_at: string; active_keys: number; key_last_used_at: string | null;
  agent_seen_at: string | null; agent_version: string | null; compatible: boolean | null;
};
export type ConnectionTemplate = { code: string; name: string; description: string; template: Record<string, string>; sample: unknown };
export type MappingTestResult = { events: { index: number; event: { eventId: string; flow: string; transactionId: string; step: string; status: string; occurredAt: string }; problems: string[] }[] };
export type Channel = { id: string; name: string; kind: string; target: string; enabled: boolean };
export const channelKindNames: Record<string, string> = { email: "E-posta", webhook: "Webhook", slack: "Slack", teams: "Microsoft Teams" };
export type Alarm = {
  id: string; flow: string; business_id: string; kind: string; step_code: string; error: string | null;
  opened_at: string; deadline_at: string | null; resolved_at: string | null; resolution: string | null;
  acknowledged_at: string | null; assigned_to: string | null; assigned_at: string | null; assigned_name: string | null; is_test: boolean;
  step_name?: string | null; instructions?: string; link?: string | null;
};
export type AlarmPage = { items: Alarm[]; total: number; page: number; pageSize: number };
export type AlarmDetail = {
  alarm: Alarm;
  events: { id: string; kind: string; created_at: string; actor: string | null }[];
  deliveries: { id: string; channel: string; kind: string; event: string; state: string; attempts: number;
    available_at: string; last_error: string | null; sent_at: string | null }[];
  notes: { id: string; body: string; created_at: string; author: string }[];
};
export type AiStatus = {
  enabled: boolean; configured: boolean; planEnabled: boolean; reason: string | null; provider: string; model: string; testMode: boolean;
  dailyLimit: number; usedToday: number; minSecondsBetweenRequests: number; cacheSeconds: number; nextAllowedAt: string | null;
};
export type AiSubject = { kind: "alarm"; id: string } | { kind: "transaction"; flow: string; transactionId: string };
export type AiEvidence = { id: string; type: string; [key: string]: unknown };
export type AiAnalysis = {
  id: string; created_at: string; subject_kind: "alarm" | "transaction"; provider: string; model: string; status: "pending" | "completed" | "failed"; error: string | null;
  duration_ms: number; input_tokens: number | null; output_tokens: number | null; requested_by: string | null; my_rating: AiRating | null;
  useful_count: number; critical_count: number; stale: boolean;
  evidence: AiEvidence[];
  result: { summary: string; cause: string; findings: { text: string; evidence: string[]; grounded: boolean }[];
    checks: string[]; unknowns: string[] } | null;
};
export type AiRating = "useful" | "wrong" | "incomplete";
export const aiCauseNames: Record<string, string> = {
  delay: "Gecikme", source_error: "Kaynak sistem hatası", notification: "Bildirim ulaşmadı", none: "Açık sorun görülmedi", unknown: "Belirlenemedi",
};
export const aiRatingNames: Record<AiRating, string> = { useful: "Faydalı", wrong: "Hatalı", incomplete: "Eksik" };
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

// Stage 13: organizations, plans, security.
export type OrgRole = "owner" | "admin" | "member";
export const orgRoleNames: Record<OrgRole, string> = { owner: "Sahip", admin: "Yönetici", member: "Üye" };
export type PlanLimits = { maxProjects: number | null; maxUsers: number | null; maxEventsPerDay: number | null; maxRetentionDays: number | null };
export type Organization = {
  id: string; name: string; plan: string; planName: string; trialEndsAt: string | null; trialExpired: boolean; planUpdatedAt: string | null; planNote: string;
  myRole: OrgRole | null; limits: PlanLimits; aiEnabled: boolean; counts: { projects: number; users: number; eventsToday: number };
  usage: { day: string; events: number; rejected: number }[];
  members: { id: string; name: string; email: string; role: OrgRole; created_at: string; mfa_enabled: boolean; last_login_at: string | null }[];
  projects: { id: string; name: string; created_at: string }[];
  groups: { id: string; name: string; created_at: string; project_ids: string[] | null; members: { id: string; name: string; email: string; role: Role }[] }[];
  sso: { issuer: string | null; clientId: string | null; domain: string | null; required: boolean; configured: boolean };
  deletion: { requestedAt: string | null; scheduledAt: string | null };
};
export type MyOrganization = { id: string; name: string; plan: string; role: OrgRole; trial_ends_at: string | null };
export type Onboarding = { steps: { key: string; title: string; done: boolean; href: string }[]; completed: number; total: number };
export type SecurityEvent = { id: number; kind: string; ip: string | null; user_agent: string | null; detail: string | null; created_at: string; actor?: string | null };
export type SecurityInfo = { mfaEnabled: boolean; systemAdmin: boolean; events: SecurityEvent[] };
export type AuditRow = { id: number; action: string; target: string; created_at: string; project: string; actor: string | null };
export type DataExport = { id: string; state: "pending" | "running" | "ready" | "failed"; size_bytes: number | null; error: string | null; created_at: string; completed_at: string | null; expires_at: string | null; requested_by: string | null };
export type AdminOrganization = { id: string; name: string; plan: string; trial_ends_at: string | null; plan_updated_at: string | null; plan_note: string; created_at: string;
  deletion_scheduled_at: string | null; projects: number; members: number; events_30d: number; owner_email: string | null; ai_enabled: boolean };
export type PlanInfo = { code: string; name: string; limits: PlanLimits; ai: boolean };
export const securityKinds: Record<string, string> = {
  "login.password": "Parola ile giriş", "login.mfa": "İki adımlı giriş", "login.sso": "Tek oturum açma ile giriş", "login.failed": "Başarısız giriş",
  "login.sso_required": "Parola girişi reddedildi (SSO zorunlu)", "mfa.enabled": "İki adımlı doğrulama açıldı", "mfa.disabled": "İki adımlı doğrulama kapatıldı",
  "mfa.failed": "Hatalı doğrulama kodu", "mfa.recovery_used": "Kurtarma kodu kullanıldı", signup: "Hesap açıldı", "sso.configured": "SSO ayarlandı", "sso.removed": "SSO kaldırıldı",
  "sso.failed": "SSO girişi başarısız", "sso.provisioned": "SSO ile kullanıcı oluşturuldu", "org.rename": "Organizasyon adı değişti", "org.member.owner": "Sahip eklendi",
  "org.member.admin": "Yönetici eklendi", "org.member.member": "Üye eklendi", "org.member.remove": "Üye çıkarıldı", "org.group.create": "Grup oluşturuldu",
  "org.group.update": "Grup güncellendi", "org.group.delete": "Grup silindi", "org.plan": "Plan değişti", "trial.ending": "Deneme süresi bitmek üzere", "trial.ended": "Deneme süresi doldu",
  "data.export_requested": "Veri dışa aktarımı istendi", "data.export_ready": "Veri dışa aktarımı hazır", "data.export_downloaded": "Dışa aktarım indirildi",
  "org.deletion_requested": "Silme isteği", "org.deletion_cancelled": "Silme isteği iptal edildi",
};
