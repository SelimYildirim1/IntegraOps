using System.Security.Cryptography;
using System.Text;

namespace IntegraOps.Infrastructure;

/// <summary>RFC 6238 time-based one-time passwords (SHA-1, 30-second step, 6 digits) as used by authenticator apps.</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32(RandomNumberGenerator.GetBytes(20));

    public static string Uri(string secret, string account, string issuer = "IntegraOps") =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={secret}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

    public static string Code(string secret, long unixSeconds, int period = 30)
    {
        var counter = unixSeconds / period;
        var message = new byte[8];
        for (var i = 7; i >= 0; i--) { message[i] = (byte)(counter & 0xFF); counter >>= 8; }
        var hash = HMACSHA1.HashData(FromBase32(secret), message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>Accepts the current step and one step either side to absorb clock drift.</summary>
    public static bool Verify(string secret, string? code, long unixSeconds)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) return false;
        for (var drift = -1; drift <= 1; drift++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, unixSeconds + drift * 30)), Encoding.ASCII.GetBytes(code))) return true;
        return false;
    }

    public static string Base32(byte[] data)
    {
        var sb = new StringBuilder(); int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(value >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(value << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var output = new List<byte>(); int bits = 0, value = 0;
        foreach (var ch in text.ToUpperInvariant().Where(c => c != '='))
        {
            var index = Alphabet.IndexOf(ch);
            if (index < 0) throw new FormatException("Base32 değeri geçersiz.");
            value = (value << 5) | index; bits += 5;
            if (bits >= 8) { output.Add((byte)((value >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return [.. output];
    }
}
