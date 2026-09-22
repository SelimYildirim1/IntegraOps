import { NextResponse, type NextRequest } from "next/server";
import { apiDownload } from "@/lib/api";
import { apiFilters } from "@/lib/filters";

export const dynamic = "force-dynamic";

// CSV export proxied with the browser session. The API decides who may export and audits it.
export async function GET(request: NextRequest, context: { params: Promise<{ kind: string }> }) {
  const { kind } = await context.params;
  if (kind !== "transactions" && kind !== "alarms") return new NextResponse("Bulunamadı", { status: 404 });
  const allowed = new Set(["flow", "status", "delay", "search", "from", "to", "test", "state", "assigned"]);
  const params = new URLSearchParams();
  for (const [key, value] of request.nextUrl.searchParams)
    if (allowed.has(key) && value.length <= 128) params.set(key, value);
  const to = apiFilters({ to: params.get("to") ?? "" }).to;
  if (to) params.set("to", to);
  let response: Response;
  try { response = await apiDownload(`/${kind}/export?${params}`); }
  catch { return new NextResponse("Giriş yapmanız gerekli.", { status: 401 }); }
  if (response.status === 401) return NextResponse.redirect(new URL("/login", request.url));
  if (!response.ok) return new NextResponse(response.status === 403 ? "Dışa aktarma yetkiniz yok." : "Dışa aktarma tamamlanamadı.", { status: response.status });
  const stamp = new Date().toISOString().slice(0, 16).replace(/[-:T]/g, "");
  return new NextResponse(response.body, {
    headers: {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": `attachment; filename="integraops-${kind === "transactions" ? "islemler" : "alarmlar"}-${stamp}.csv"`,
      "Cache-Control": "no-store",
    },
  });
}
