export type TransactionStatus = "pending" | "inProgress" | "completed" | "failed";
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
  completed: number; failed: number; overdue: number; queriedAt: string;
};
export type TransactionItem = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  createdAt: string; updatedAt: string; delay: DelayInfo;
};
export type TransactionPage = {
  page: number; pageSize: number; totalCount: number; totalPages: number;
  hasNextPage: boolean; items: TransactionItem[];
};
export type TimelineStep = {
  code: string; name: string; position: number; maxDelaySeconds: number | null;
  observation: "not_observed" | "completed" | "failed";
  occurredAt: string | null; error: string | null;
};
export type ObservedEvent = {
  eventId: string; step: string; status: "completed" | "failed";
  occurredAt: string; receivedAt: string; error: string | null;
};
export type TransactionDetail = {
  id: string; flow: string; transactionId: string; status: TransactionStatus;
  acceptedEventCount: number; processedEventCount: number;
  steps: TimelineStep[]; events: ObservedEvent[]; delay: DelayInfo | null;
};
export type Filters = { flow: string; status: string; delay: string; page: number; pageSize: number };
