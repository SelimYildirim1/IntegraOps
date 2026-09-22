import TokenForm from "@/components/token-form";

export const dynamic = "force-dynamic";

export default async function InvitePage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return <TokenForm kind="invite" token={token} />;
}
