using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codenotch.Core.Sources.Codex;

/// <summary>
/// Locates and parses the Codex CLI's auth.json. Both snake_case and camelCase
/// spellings are accepted for every token field, because the file has been
/// written both ways across CLI versions.
/// </summary>
public static class CodexCredentialsFile
{
    public const string FileName = "auth.json";
    public const string DirectoryName = ".codex";
    public const string HomeVariable = "CODEX_HOME";

    public static string ResolvePath(IReadOnlyDictionary<string, string?> env, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(env);
        string? home = null;
        if (env.TryGetValue(HomeVariable, out string? raw) && !string.IsNullOrWhiteSpace(raw))
            home = raw.Trim();
        home ??= Path.Combine(userProfile ?? string.Empty, DirectoryName);
        return Path.Combine(home, FileName);
    }

    public static CodexCredentials Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException ex)
        {
            throw new CodexCredentialException(CodexCredentialProblem.NotFound, $"{path} does not exist", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new CodexCredentialException(CodexCredentialProblem.NotFound, $"{path} does not exist", ex);
        }
        catch (IOException ex)
        {
            throw new CodexCredentialException(CodexCredentialProblem.Unreadable, $"{path} could not be read", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new CodexCredentialException(CodexCredentialProblem.Unreadable, $"{path} could not be read", ex);
        }
        return Parse(json);
    }

    public static CodexCredentials Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Model.CodexBarJson.DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new CodexCredentialException(CodexCredentialProblem.DecodeFailed,
                "auth.json is not valid JSON", ex);
        }

        if (root is not JsonObject obj)
            throw new CodexCredentialException(CodexCredentialProblem.DecodeFailed,
                "auth.json is not a JSON object");

        if (obj["tokens"] is not JsonObject tokens)
            throw new CodexCredentialException(CodexCredentialProblem.MissingTokens, "tokens is missing");

        string accessToken = (Text(tokens, "access_token", "accessToken") ?? string.Empty).Trim();
        if (accessToken.Length == 0)
            throw new CodexCredentialException(CodexCredentialProblem.MissingTokens,
                "tokens.access_token is missing");

        string? refreshToken = Text(tokens, "refresh_token", "refreshToken");
        string? idToken = Text(tokens, "id_token", "idToken");
        string? accountId = Text(tokens, "account_id", "accountId");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            // Old files predate the field; CodexBar recovers it from the JWT.
            accountId = AccountIdFromJwt(idToken, accessToken);
        }

        _ = Text(obj, "last_refresh", "lastRefresh");   // parsed and ignored
        // OPENAI_API_KEY is deliberately not read: v1 only speaks OAuth.

        return new CodexCredentials(accessToken, refreshToken, idToken,
                                    string.IsNullOrWhiteSpace(accountId) ? null : accountId);
    }

    /// <summary>
    /// The ChatGPT account id hidden in the OAuth JWT. Tries the id token first,
    /// then the access token, and inside each: the top-level claim, the
    /// https://api.openai.com/auth namespace, then the first organization — the
    /// order CodexBar uses. Signature is never verified; we are reading our own
    /// file, not trusting a third party.
    /// </summary>
    public static string? AccountIdFromJwt(string? idToken, string? accessToken)
    {
        foreach (string? token in new[] { idToken, accessToken })
        {
            JsonObject? payload = Payload(token);
            if (payload is null)
                continue;

            string? direct = AsString(payload["chatgpt_account_id"]);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            if (payload["https://api.openai.com/auth"] is JsonObject ns)
            {
                string? scoped = AsString(ns["chatgpt_account_id"]);
                if (!string.IsNullOrWhiteSpace(scoped))
                    return scoped;
            }

            if (payload["organizations"] is JsonArray orgs && orgs.Count > 0
                && orgs[0] is JsonObject org)
            {
                string? orgId = AsString(org["id"]);
                if (!string.IsNullOrWhiteSpace(orgId))
                    return orgId;
            }
        }
        return null;
    }

    private static JsonObject? Payload(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        string[] parts = token.Split('.');
        if (parts.Length != 3)
            return null;              // an opaque token, not a JWT
        try
        {
            byte[] bytes = Base64Url.DecodeFromChars(parts[1]);
            return JsonNode.Parse(Encoding.UTF8.GetString(bytes),
                documentOptions: Model.CodexBarJson.DocumentOptions) as JsonObject;
        }
        catch (Exception)
        {
            return null;              // junk payload; the caller falls back to null
        }
    }

    private static string? Text(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            string? value = AsString(obj[key]);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue(out string? s) ? s : null;
    }
}
