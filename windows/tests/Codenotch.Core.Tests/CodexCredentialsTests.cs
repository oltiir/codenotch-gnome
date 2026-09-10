using System.Text;
using System.Text.Json;
using Codenotch.Core.Sources.Codex;
using Xunit;

namespace Codenotch.Core.Tests;

public class CodexCredentialsTests
{
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string MakeJwt(object payload)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var sig = Base64Url(Encoding.UTF8.GetBytes("sig"));
        return $"{header}.{body}.{sig}";
    }

    [Fact]
    public void CodexAuthJsonParsesAccessTokenRefreshTokenIdTokenAndAccountId()
    {
        var creds = CodexCredentialsFile.Parse(Fixtures.Text("codex-auth.json"));
        Assert.Equal("eyJ...", creds.AccessToken);
        Assert.Equal("rt_x", creds.RefreshToken);
        Assert.Equal("eyJ...", creds.IdToken);
        Assert.Equal("acct_2f9c1d40", creds.AccountId);
    }

    [Fact]
    public void CamelCaseVariantsParseIdentically()
    {
        var json = """{"tokens":{"accessToken":"at","refreshToken":"rt","idToken":"it","accountId":"acct"}}""";
        var creds = CodexCredentialsFile.Parse(json);
        Assert.Equal("at", creds.AccessToken);
        Assert.Equal("rt", creds.RefreshToken);
        Assert.Equal("it", creds.IdToken);
        Assert.Equal("acct", creds.AccountId);
    }

    [Fact]
    public void NoAccountFixtureRecoversTheAccountIdFromTheTopLevelClaim()
    {
        var creds = CodexCredentialsFile.Parse(Fixtures.Text("codex-auth-no-account.json"));
        Assert.Equal("acct_from_jwt", creds.AccountId);
    }

    [Fact]
    public void AJwtWithOnlyTheOpenAiAuthNamespaceYieldsThatClaimsValue()
    {
        var payload = new Dictionary<string, object>
        {
            ["https://api.openai.com/auth"] = new Dictionary<string, object> { ["chatgpt_account_id"] = "acct_ns_only" },
        };
        var jwt = MakeJwt(payload);

        var accountId = CodexCredentialsFile.AccountIdFromJwt(jwt, null);
        Assert.Equal("acct_ns_only", accountId);
    }

    [Fact]
    public void AJwtWithOnlyOrganizationsFirstIdYieldsThatId()
    {
        var payload = new Dictionary<string, object>
        {
            ["organizations"] = new[] { new Dictionary<string, object> { ["id"] = "org_abc" } },
        };
        var jwt = MakeJwt(payload);

        var accountId = CodexCredentialsFile.AccountIdFromJwt(jwt, null);
        Assert.Equal("org_abc", accountId);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("only.two-parts")]
    public void AnOpaqueOrTwoPartTokenYieldsANullAccountIdRatherThanThrowing(string token)
    {
        Assert.Null(CodexCredentialsFile.AccountIdFromJwt(token, null));
    }

    [Fact]
    public void AFileWithNoTokensObjectThrowsMissingTokens()
    {
        var ex = Assert.Throws<CodexCredentialException>(() => CodexCredentialsFile.Parse("{}"));
        Assert.Equal(CodexCredentialProblem.MissingTokens, ex.Problem);
    }

    [Fact]
    public void ResolvePathHonoursCodexHomeAndOtherwiseUsesUserProfileDotCodex()
    {
        var withCodexHome = new Dictionary<string, string?> { ["CODEX_HOME"] = "/custom/codex" };
        Assert.Equal(Path.Combine("/custom/codex", "auth.json"),
            CodexCredentialsFile.ResolvePath(withCodexHome, "/home/user"));

        var withoutCodexHome = new Dictionary<string, string?>();
        Assert.Equal(Path.Combine("/home/user", ".codex", "auth.json"),
            CodexCredentialsFile.ResolvePath(withoutCodexHome, "/home/user"));
    }
}
