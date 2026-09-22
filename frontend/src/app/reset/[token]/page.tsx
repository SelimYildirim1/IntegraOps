import TokenForm from "@/components/token-form";

export const dynamic = "force-dynamic";

export default async function ResetPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return <TokenForm kind="reset" token={token} />;
}
