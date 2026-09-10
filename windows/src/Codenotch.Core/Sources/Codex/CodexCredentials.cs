namespace Codenotch.Core.Sources.Codex;

/// <summary>What the Codex CLI wrote into ~/.codex/auth.json. Read only, never written.</summary>
public sealed record CodexCredentials(string AccessToken, string? RefreshToken, string? IdToken, string? AccountId);

public enum CodexCredentialProblem
{
    NotFound,
    Unreadable,
    DecodeFailed,
    MissingTokens,
}

public sealed class CodexCredentialException : Exception
{
    public CodexCredentialException(CodexCredentialProblem problem, string message, Exception? inner = null)
        : base(message, inner)
    {
        Problem = problem;
    }

    public CodexCredentialProblem Problem { get; }

    public string UserMessage => MessageFor(Problem);

    public static string MessageFor(CodexCredentialProblem problem) => problem switch
    {
        CodexCredentialProblem.DecodeFailed or CodexCredentialProblem.Unreadable =>
            "Codex credentials are unreadable",
        _ => "not signed in, run `codex login`",
    };
}
