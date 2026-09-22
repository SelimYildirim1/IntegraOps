import "server-only";
import { cookies } from "next/headers";
import { redirect, unstable_rethrow } from "next/navigation";
import type { FlowDetails, FlowDiffResult, FlowVersionDetails, TestEventPayload } from "./flow-details";
import type { CreateFlowInput } from "./flow-form";
import type { Filters, Flow, Summary, TransactionDetail, TransactionPage } from "./types";
import type { Project, Access, Assignee, Calendar, SavedView } from "./pilot-types";

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function raw<T>(path: string, method: string, body: unknown, headers: Record<string, string>): Promise<T> {
  const base = process.env.INTEGRAOPS_API_URL;
  if (!base) throw new ApiError(503, "Panelin API bağlantı ayarı eksik.");
  let response: Response;
  try {
    response = await fetch(new URL("/api/v1" + path, base), {
      method, headers: { ...headers, "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store", redirect: "error", signal: AbortSignal.timeout(15000),
    });
  } catch {
    throw new ApiError(503, "Servis yanıtı alınamadı. Yazma işlemi kaydedilmiş olabilir; ilgili listeyi kontrol edin.");
  }
  if (!response.ok) {
    let title = "İstek tamamlanamadı.";
    try {
      const problem = await response.json();
      if (typeof problem.title === "string" && problem.title.length < 500) title = problem.title;
    } catch { /* Do not display raw proxy/server responses. */ }
    if (response.status === 401) title = "Oturum doğrulanamadı. Tekrar giriş yapın.";
    if (response.status === 429) title = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.";
    if (response.status >= 500) title = "Servis şu anda isteği tamamlayamıyor. Bir süre sonra tekrar deneyin.";
    throw new ApiError(response.status, title);
  }
  if (response.status === 204) return undefined as T;
  try { return await response.json() as T; }
  catch { throw new ApiError(502, "Servis yanıtı okunamadı."); }
}

export async function api<T>(path: string, method = "GET", body?: unknown, projectScoped = true): Promise<T> {
  const jar = await cookies(); const token = jar.get("io_session")?.value;
  if (!token) throw new ApiError(401, "Giriş yapmanız gerekli.");
  const headers: Record<string, string> = { Authorization: "Bearer " + token };
  if (projectScoped) {
    let project = jar.get("io_project")?.value;
    if (!project) project = (await raw<Project[]>("/me/projects", "GET", undefined, headers))[0]?.id;
    if (!project) redirect("/projects");
    headers["X-Project-Id"] = project;
  }
  return raw<T>(path, method, body, headers);
}

export type LoginResult = { token?: string; expiresIn?: number; mfaRequired?: boolean; challenge?: string };
export const login = (email: string, password: string) => raw<LoginResult>("/auth/login", "POST", { email, password }, {});
export const completeMfa = (challenge: string, code: string) => raw<{ token: string }>("/auth/mfa", "POST", { challenge, code }, {});
export const signup = (input: { organizationName: string; name: string; email: string; password: string }) =>
  raw<{ token: string; projectId: string }>("/auth/signup", "POST", input, {});
export const startSso = (email: string, next: string | null) => raw<{ url: string }>("/auth/sso/start" + (next ? "?" + new URLSearchParams({ next }) : ""), "POST", { email }, {});
export const redeemSsoTicket = (ticket: string) => raw<{ token: string }>("/auth/sso/ticket", "POST", { ticket }, {});
export const getProjects = () => api<Project[]>("/me/projects", "GET", undefined, false);
export const getAccess = () => api<Access>("/access");
export const getFlows = () => api<Flow[]>("/flows");
export const getFlow = (flow: string) => api<FlowDetails>("/flows/" + encodeURIComponent(flow));
export const getFlowVersion = (flow: string, version: number) => api<FlowVersionDetails>(`/flows/${encodeURIComponent(flow)}/versions/${version}`);
export const getFlowDiff = (flow: string, from: number, to: number) => api<FlowDiffResult>(`/flows/${encodeURIComponent(flow)}/versions/${from}/diff/${to}`);
export const getCalendars = () => api<Calendar[]>("/calendars");
export const getSummary = (flow: string, test: string) => api<Summary>(
  "/transactions/summary?" + new URLSearchParams({ flow, test }));
export const getTransactions = (filters: Filters) => api<TransactionPage>(
  "/transactions?" + new URLSearchParams({ ...filters, page: String(filters.page), pageSize: String(filters.pageSize) }));
export const getSavedViews = (kind: "transactions" | "alarms") => api<SavedView[]>("/views?" + new URLSearchParams({ kind }));
export const getAssignable = () => api<Assignee[]>("/members/assignable");
export const requestPasswordReset = (email: string) => raw<void>("/auth/password-reset", "POST", { email }, {});
export const getTokenInfo = (token: string) => raw<{ kind: "invite" | "reset"; email: string; name: string }>(
  "/auth/tokens/" + encodeURIComponent(token), "GET", undefined, {});
export const completeToken = (kind: "invite" | "reset", token: string, password: string) => raw<void>(
  (kind === "invite" ? "/auth/invitations/" : "/auth/password-reset/") + encodeURIComponent(token), "POST", { password }, {});

// Session-only fetch (no project header) that returns the raw response, for organization-level downloads.
export async function sessionDownload(path: string): Promise<Response> {
  const jar = await cookies(); const token = jar.get("io_session")?.value; const base = process.env.INTEGRAOPS_API_URL;
  if (!token || !base) throw new ApiError(401, "Giriş yapmanız gerekli.");
  return fetch(new URL("/api/v1" + path, base), { headers: { Authorization: "Bearer " + token }, cache: "no-store", redirect: "error", signal: AbortSignal.timeout(120000) });
}
// Session-scoped fetch that returns the raw response, for CSV downloads.
export async function apiDownload(path: string): Promise<Response> {
  const jar = await cookies(); const token = jar.get("io_session")?.value; const project = jar.get("io_project")?.value;
  const base = process.env.INTEGRAOPS_API_URL;
  if (!token || !project || !base) throw new ApiError(401, "Giriş yapmanız gerekli.");
  return fetch(new URL("/api/v1" + path, base), {
    headers: { Authorization: "Bearer " + token, "X-Project-Id": project },
    cache: "no-store", redirect: "error", signal: AbortSignal.timeout(60000),
  });
}
export const getTransaction = (flow: string, id: string) => api<TransactionDetail>(
  "/flows/" + encodeURIComponent(flow) + "/transactions/" + encodeURIComponent(id));
export async function createFlow(input: CreateFlowInput): Promise<void> { await api("/flows", "POST", input); }
export async function postTestEvent(input: TestEventPayload): Promise<{ duplicate: boolean }> {
  const receipt = await api<{ duplicate: boolean }>("/events", "POST", { ...input, status: "completed" });
  if (typeof receipt.duplicate !== "boolean") throw new ApiError(502, "Bildirim yanıtı okunamadı. Aynı bildirimi tekrar gönderebilirsiniz.");
  return receipt;
}
export function apiMessage(error: unknown) {
  unstable_rethrow(error);
  if (error instanceof ApiError && error.status === 401) redirect("/login");
  return error instanceof ApiError ? error.message : "Veriler yüklenemedi. Yeniden deneyin.";
}
