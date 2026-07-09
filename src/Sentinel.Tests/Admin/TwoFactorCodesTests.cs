using OtpNet;
using Sentinel.Admin.Auth;

namespace Sentinel.Tests.Admin;

public class TwoFactorCodesTests
{
    [Fact]
    public void GenerateEmailCode_IsSixDigits()
    {
        for (var i = 0; i < 20; i++)
            Assert.Matches("^\\d{6}$", TwoFactorCodes.GenerateEmailCode());
    }

    [Fact]
    public void VerifyTotp_CurrentCode_Succeeds()
    {
        var secret = TwoFactorCodes.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        Assert.True(TwoFactorCodes.VerifyTotp(secret, code));
    }

    [Fact]
    public void VerifyTotp_WrongCode_Fails()
    {
        var secret = TwoFactorCodes.GenerateSecret();
        var wrong = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp() == "000000" ? "111111" : "000000";

        Assert.False(TwoFactorCodes.VerifyTotp(secret, wrong));
    }

    [Fact]
    public void BuildOtpauthUrl_ContainsSecretAndIssuer()
    {
        var secret = TwoFactorCodes.GenerateSecret();
        var url = TwoFactorCodes.BuildOtpauthUrl(secret, "user@hobbiton.co.zm");

        Assert.StartsWith("otpauth://totp/", url);
        Assert.Contains($"secret={secret}", url);
        Assert.Contains("issuer=Sentinel", url);
    }
}