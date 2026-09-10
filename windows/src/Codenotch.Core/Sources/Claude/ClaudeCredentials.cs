namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// What Claude Code wrote into ~/.claude/.credentials.json. We only ever read
/// this file; the refresh token is never exchanged by us (see
/// <see cref="ClaudeRefreshPolicy"/>).
/// </summary>
public sealed record ClaudeCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? SubscriptionType,
    string? RateLimitTier)
{
    /// <summary>
    /// A credential with no expiry is treated as expired: an old file that predates
    /// the field should send us through the delegated refresh rather than straight
    /// into a guaranteed 401.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not DateTimeOffset at || at <= now;
}

public enum ClaudeCredentialProblem
{
    NotFound,
    Unreadable,
    DecodeFailed,
    McpOAuthOnly,
    MissingOAuth,
    MissingAccessToken,
}

public sealed class ClaudeCredentialException : Exception
{
    public ClaudeCredentialException(ClaudeCredentialProblem problem, string message, Exception? inner = null)
        : base(message, inner)
    {
        Problem = problem;
    }

    public ClaudeCredentialProblem Problem { get; }

    /// <summary>What the flyout and the notch callout show for this problem.</summary>
    public string UserMessage => MessageFor(Problem);

    public static string MessageFor(ClaudeCredentialProblem problem) => problem switch
    {
        ClaudeCredentialProblem.McpOAuthOnly =>
            "Claude Code stored only MCP OAuth state; run `claude` to sign in again",
        ClaudeCredentialProblem.DecodeFailed or ClaudeCredentialProblem.Unreadable =>
            "Claude credentials are unreadable",
        _ => "not signed in, run `claude` to sign in",
    };
}
