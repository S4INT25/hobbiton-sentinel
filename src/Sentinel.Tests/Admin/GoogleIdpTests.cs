using System.Text;
using Sentinel.Admin.Auth;

namespace Sentinel.Tests.Admin;

public class GoogleIdpTests
{
    private static readonly GoogleIdpSettings Settings = new(true, "client-1", "secret", "hobbiton.co.zm");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static string Jwt(string payloadJson) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    private static GoogleIdp.IdToken Token(string? hd = "hobbiton.co.zm", string aud = "client-1", bool verified = true) =>
        GoogleIdp.DecodeIdToken(Jwt(
            $$"""{"aud":"{{aud}}","iss":"https://accounts.google.com","exp":2000000,"email":"a@hobbiton.co.zm","email_verified":{{verified.ToString().ToLower()}}{{(hd is null ? "" : $",\"hd\":\"{hd}\"")}},"name":"Ann"}"""));

    [Fact]
    public void Validate_WorkspaceAccount_Passes() => Assert.Null(GoogleIdp.Validate(Token(), Settings, Now));

    [Fact]
    public void Validate_PersonalGmail_RejectedByHdClaim() =>
        Assert.Equal("wrong domain", GoogleIdp.Validate(Token(hd: null), Settings, Now));

    [Fact]
    public void Validate_OtherClientsToken_Rejected() =>
        Assert.Equal("audience mismatch", GoogleIdp.Validate(Token(aud: "someone-else"), Settings, Now));

    [Fact]
    public void Validate_UnverifiedEmail_Rejected() =>
        Assert.Equal("email not verified", GoogleIdp.Validate(Token(verified: false), Settings, Now));

    [Fact]
    public void Validate_Expired_Rejected() =>
        Assert.Equal("token expired", GoogleIdp.Validate(Token(), Settings, DateTimeOffset.FromUnixTimeSeconds(3_000_000)));
}
