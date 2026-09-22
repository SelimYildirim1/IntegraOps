export type TransactionStatus = "pending" | "inProgress" | "completed" | "failed" | "cancelled";
export type StepKind = "required" | "optional" | "conditional";
export const stepKindLabels: Record<StepKind, string> = { required: "Zorunlu", optional: "İsteğe bağlı", conditional: "Koşullu" };
export type DelayState = "none" | "waiting" | "overdue" | "pending_processing" | "pending_evaluation";

export type DelayInfo = {
  state: DelayState;
  step: string | null;
  startedAt: string | null;
  deadlineAt: string | null;
  checkedAt: string | null;
};

export type Flow = { id: string; code: string; name: string; stepCount: number };
export type Summary = {
  total: number; pending: number; inProgress: number;
  completed: number; failed: number; overdue: number; queriedAt: string; cancelled: number;
};
export type TransactionItem = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  createdAt: string; updatedAt: string; delay: DelayInfo; isTest: boolean;
};
export type TransactionPage = {
  page: number; pageSize: number; totalCount: number; totalPages: number;
  hasNextPage: boolean; items: TransactionItem[];
};
export type TimelineStep = {
  code: string; name: string; position: number; maxDelaySeconds: number | null;
  observation: "not_observed" | "completed" | "failed" | "skipped" | "cancelled" | "not_applicable";
  occurredAt: string | null; error: string | null; stage: number; kind: StepKind;
};
export type ObservedEvent = {
  eventId: string; step: string; status: "completed" | "failed" | "skipped" | "cancelled";
  occurredAt: string; receivedAt: string; error: string | null;
};
export type TransactionDetail = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  acceptedEventCount: number; processedEventCount: number;
  steps: TimelineStep[]; events: ObservedEvent[]; delay: DelayInfo | null;
  flowVersion: number; group: string | null; calendar: string | null;
};
export type Filters = {
  flow: string; status: string; delay: string; page: number; pageSize: number;
  search: string; from: string; to: string; test: string;
};
export type TestFilter = "exclude" | "include" | "only";
export const testLabels: Record<TestFilter, string> = {
  exclude: "Test verisi hariç", include: "Test verisi dahil", only: "Yalnızca test verisi",
};
