using System.Security.Cryptography;
using OtpNet;

namespace Sentinel.Admin.Auth;

public static class TwoFactorCodes
{
    private const string Issuer = "Sentinel";

    public static string GenerateSecret() =>
        Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public static string BuildOtpauthUrl(string secret, string account)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{account}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30";
    }

    public static bool VerifyTotp(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code.Trim(), out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }

    public static string GenerateEmailCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}