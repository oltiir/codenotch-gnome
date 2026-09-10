using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// Locates and parses Claude Code's credential file. Deliberately lenient about
/// everything except the access token: CodexBar's own decoder tolerates missing
/// scopes and tiers, and so must we, or a Claude Code update that drops a field
/// takes the tray icon down with it.
/// </summary>
public static class ClaudeCredentialsFile
{
    public const string FileName = ".credentials.json";
    public const string DirectoryName = ".claude";
    public const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// CLAUDE_CONFIG_DIR wins when set. Claude Code allows a list there; the first
    /// entry is the primary config directory, so that is the one we read.
    /// </summary>
    public static string ResolvePath(IReadOnlyDictionary<string, string?> env, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(env);
        string? configDir = null;
        if (env.TryGetValue(ConfigDirVariable, out string? raw) && !string.IsNullOrWhiteSpace(raw))
        {
            string first = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .FirstOrDefault() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(first))
                configDir = first;
        }
        configDir ??= Path.Combine(userProfile ?? string.Empty, DirectoryName);
        return Path.Combine(configDir, FileName);
    }

    public static ClaudeCredentials Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException ex)
        {
            throw new ClaudeCredentialException(ClaudeCredentialProblem.NotFound, $"{path} does not exist", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new ClaudeCredentialException(ClaudeCredentialProblem.NotFound, $"{path} does not exist", ex);
        }
        catch (IOException ex)
        {
            throw new ClaudeCredentialException(ClaudeCredentialProblem.Unreadable, $"{path} could not be read", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ClaudeCredentialException(ClaudeCredentialProblem.Unreadable, $"{path} could not be read", ex);
        }
        return Parse(json);
    }

    public static ClaudeCredentials Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Model.CodexBarJson.DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ClaudeCredentialException(ClaudeCredentialProblem.DecodeFailed,
                "the credential file is not valid JSON", ex);
        }

        if (root is not JsonObject obj)
            throw new ClaudeCredentialException(ClaudeCredentialProblem.DecodeFailed,
                "the credential file is not a JSON object");

        JsonObject? oauth = obj["claudeAiOauth"] as JsonObject;
        if (oauth is null)
        {
            // A file holding only MCP server OAuth state means Claude Code has
            // never completed (or has lost) its own sign-in. Worth saying out loud,
            // because the file existing looks like being signed in.
            if (obj["mcpOAuth"] is not null)
                throw new ClaudeCredentialException(ClaudeCredentialProblem.McpOAuthOnly,
                    "only mcpOAuth is present");
            throw new ClaudeCredentialException(ClaudeCredentialProblem.MissingOAuth,
                "claudeAiOauth is missing");
        }

        string accessToken = (Text(oauth, "accessToken") ?? string.Empty).Trim();
        if (accessToken.Length == 0)
            throw new ClaudeCredentialException(ClaudeCredentialProblem.MissingAccessToken,
                "claudeAiOauth.accessToken is missing");

        string? refreshToken = Text(oauth, "refreshToken");
        // expiresAt is epoch MILLISECONDS and may arrive as a double.
        DateTimeOffset? expiresAt = Number(oauth, "expiresAt") is double ms
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms)
            : null;
        _ = Number(oauth, "refreshTokenExpiresAt");   // parsed and ignored: we never refresh ourselves

        var scopes = new List<string>();
        if (oauth["scopes"] is JsonArray scopeArray)
        {
            foreach (JsonNode? scope in scopeArray)
            {
                string? value = AsString(scope);
                if (!string.IsNullOrWhiteSpace(value))
                    scopes.Add(value);
            }
        }

        return new ClaudeCredentials(accessToken, refreshToken, expiresAt, scopes,
                                     Text(oauth, "subscriptionType"), Text(oauth, "rateLimitTier"));
    }

    private static string? Text(JsonObject obj, string key) => AsString(obj[key]);

    private static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue(out string? s) ? s : null;
    }

    private static double? Number(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value)
            return null;
        if (value.TryGetValue(out double d))
            return d;
        // Some writers quote the number.
        if (value.TryGetValue(out string? s) && double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        return null;
    }
}
