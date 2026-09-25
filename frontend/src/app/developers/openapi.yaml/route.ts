import { NextResponse } from "next/server";

// Proxies the API's OpenAPI document so the browser gets it from the panel origin without needing the API address.
export const dynamic = "force-dynamic";

export async function GET() {
  const base = process.env.INTEGRAOPS_API_URL;
  if (!base) return new NextResponse("API adresi ayarlı değil.", { status: 503 });
  try {
    const response = await fetch(new URL("/openapi.yaml", base), { cache: "no-store", signal: AbortSignal.timeout(10000) });
    if (!response.ok) return new NextResponse("OpenAPI belgesi alınamadı.", { status: 502 });
    return new NextResponse(await response.text(), {
      headers: { "Content-Type": "application/yaml; charset=utf-8", "Content-Disposition": "inline; filename=\"integraops-openapi.yaml\"" },
    });
  } catch { return new NextResponse("API'ye ulaşılamadı.", { status: 502 }); }
}
