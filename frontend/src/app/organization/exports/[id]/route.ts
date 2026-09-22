import { NextResponse, type NextRequest } from "next/server";
import { sessionDownload } from "@/lib/api";

export const dynamic = "force-dynamic";

// Organization data export proxied with the browser session; the API checks ownership and audits the download.
export async function GET(request: NextRequest, context: { params: Promise<{ id: string }> }) {
  const { id } = await context.params; const org = request.nextUrl.searchParams.get("org") ?? "";
  if (!/^[0-9a-f-]{36}$/i.test(id) || !/^[0-9a-f-]{36}$/i.test(org)) return new NextResponse("Bulunamadı", { status: 404 });
  let response: Response;
  try { response = await sessionDownload(`/organizations/${org}/exports/${id}/download`); }
  catch { return new NextResponse("Giriş yapmanız gerekli.", { status: 401 }); }
  if (response.status === 401) return NextResponse.redirect(new URL("/login", request.url));
  if (!response.ok) return new NextResponse(response.status === 403 ? "Bu dışa aktarımı indirme yetkiniz yok." : "Dışa aktarım bulunamadı ya da süresi doldu.", { status: response.status });
  return new NextResponse(response.body, {
    headers: {
      "Content-Type": "application/zip",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? `attachment; filename="integraops-export.zip"`,
      "Cache-Control": "no-store",
    },
  });
}
