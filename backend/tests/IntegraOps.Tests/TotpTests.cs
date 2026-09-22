using IntegraOps.Infrastructure;
using Xunit;

public class TotpTests
{
    // RFC 6238 appendix B test vector: ASCII secret "12345678901234567890", SHA-1, 8 digits truncated here to 6.
    private const string Rfc6238Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Matches_rfc6238_vectors(long time, string expected) => Assert.Equal(expected, Totp.Code(Rfc6238Secret, time));

    [Fact]
    public void Accepts_one_step_of_drift_but_not_two()
    {
        var secret = Totp.NewSecret();
        var now = 1_800_000_000L;
        Assert.True(Totp.Verify(secret, Totp.Code(secret, now - 30), now));
        Assert.True(Totp.Verify(secret, Totp.Code(secret, now + 30), now));
        Assert.False(Totp.Verify(secret, Totp.Code(secret, now + 90), now));
        Assert.False(Totp.Verify(secret, "12345", now));
        Assert.False(Totp.Verify(secret, null, now));
    }

    [Fact]
    public void Base32_round_trips()
    {
        var bytes = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255, 42 };
        Assert.Equal(bytes, Totp.FromBase32(Totp.Base32(bytes)));
        Assert.Equal(32, Totp.NewSecret().Length);
    }

    [Fact]
    public void Uri_names_issuer_and_account()
    {
        var uri = Totp.Uri("ABC", "ops@example.com");
        Assert.StartsWith("otpauth://totp/IntegraOps:ops%40example.com?secret=ABC&issuer=IntegraOps", uri);
    }
}
