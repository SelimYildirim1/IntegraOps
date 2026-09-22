import { NextResponse, type NextRequest } from "next/server";

// Navigation protection only; the API verifies sessions and roles on every request.
export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const open = pathname === "/login" || pathname === "/forgot" || pathname.startsWith("/reset/") || pathname.startsWith("/invite/");
  if (!request.cookies.get("io_session")?.value && !open)
    return NextResponse.redirect(new URL("/login", request.url));
  return NextResponse.next();
}
export const config = { matcher: ["/((?!_next/static|_next/image|favicon.ico|robots.txt).*)"] };
