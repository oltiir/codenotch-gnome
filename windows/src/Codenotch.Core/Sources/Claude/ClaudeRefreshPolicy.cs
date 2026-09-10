namespace Codenotch.Core.Sources.Claude;

public enum RefreshOutcome
{
    NotNeeded,
    Refreshed,
    StillExpired,
    SkippedByCooldown,
    CliUnavailable,
}

/// <summary>
/// Delegated token refresh. When the stored access token has expired we do NOT
/// exchange the refresh token ourselves: Claude Code rotates it and rewrites the
/// file, so doing it here would race the CLI and could invalidate the user's
/// session. Instead we run the CLI once and let it refresh and rewrite, then
/// re-read the file.
///
/// This is the one behaviour in the port that cannot be validated without a live
/// expired token (plan H.2): `claude auth status --json` is documented to exist
/// and exits non-interactively, but whether it triggers a refresh is unverified.
/// The failure mode is benign — StillExpired, and the user sees "run `claude`
/// once to refresh" — so a wrong guess costs a message, not correctness.
/// </summary>
public sealed class ClaudeRefreshPolicy
{
    /// <summary>CodexBar's defaultCooldownInterval.</summary>
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Non-interactive, exits on its own, and prints machine-readable status.</summary>
    private static readonly string[] Arguments = ["auth", "status", "--json"];

    private readonly IProcessRunner _runner;
    private readonly IClock _clock;
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _timeout;
    private readonly string _claudeExecutable;

    public ClaudeRefreshPolicy(IProcessRunner runner, IClock clock,
                               TimeSpan? cooldown = null,
                               TimeSpan? timeout = null,
                               string claudeExecutable = "claude")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _cooldown = cooldown ?? DefaultCooldown;
        _timeout = timeout ?? DefaultTimeout;
        _claudeExecutable = string.IsNullOrWhiteSpace(claudeExecutable) ? "claude" : claudeExecutable;
    }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>The single message every unsuccessful outcome surfaces to the UI.</summary>
    public const string ExpiredMessage = "Claude token expired; run `claude` once to refresh";

    public async Task<(RefreshOutcome Outcome, ClaudeCredentials Credentials)> EnsureFreshAsync(
        ClaudeCredentials current, Func<ClaudeCredentials> reload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(reload);

        DateTimeOffset now = _clock.UtcNow;
        if (!current.IsExpired(now))
            return (RefreshOutcome.NotNeeded, current);

        // At most one delegated refresh per cooldown, however often we poll.
        if (LastAttemptAt is DateTimeOffset last && now - last < _cooldown)
            return (RefreshOutcome.SkippedByCooldown, current);

        ProcessResult result = await _runner
            .RunAsync(_claudeExecutable, Arguments, _timeout, ct).ConfigureAwait(false);

        // Record the attempt whatever happened, including a timeout: a CLI that
        // hangs must not be re-launched every poll.
        LastAttemptAt = _clock.UtcNow;

        if (result == ProcessResult.NotFound)
            return (RefreshOutcome.CliUnavailable, current);

        ClaudeCredentials reloaded;
        try
        {
            reloaded = reload();
        }
        catch (Exception)
        {
            // The CLI may have removed or rewritten the file mid-read.
            return (RefreshOutcome.StillExpired, current);
        }

        return reloaded.IsExpired(_clock.UtcNow)
            ? (RefreshOutcome.StillExpired, reloaded)
            : (RefreshOutcome.Refreshed, reloaded);
    }
}
