import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { redeemSsoTicket } from "@/lib/api";

// The identity provider sends the browser back through the API, which redirects here with a one-time ticket.
export async function GET(request: NextRequest) {
  const ticket = request.nextUrl.searchParams.get("ticket") ?? "";
  const next = request.nextUrl.searchParams.get("next") ?? "";
  if (!/^[0-9a-f]{64}$/.test(ticket)) return NextResponse.redirect(new URL("/login?sso=invalid", request.url));
  let result;
  try { result = await redeemSsoTicket(ticket); }
  catch { return NextResponse.redirect(new URL("/login?sso=expired", request.url)); }
  const jar = await cookies();
  jar.set("io_session", result.token, { httpOnly: true, sameSite: "lax", secure: process.env.PANEL_COOKIE_SECURE === "true", path: "/", maxAge: 28800 });
  jar.delete("io_project");
  return NextResponse.redirect(new URL(next.startsWith("/") && !next.startsWith("//") ? next : "/projects", request.url));
}
