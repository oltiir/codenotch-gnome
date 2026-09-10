namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// The usage endpoint is Claude Code's own, so we introduce ourselves the way
/// Claude Code does. The version is asked for once per process (it costs a
/// process launch) and cached; a machine where the CLI is not on PATH still gets
/// a plausible User-Agent rather than none.
/// </summary>
public sealed class ClaudeVersionProbe
{
    public const string Fallback = "2.1.0";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner _runner;
    private readonly string _claudeExecutable;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public ClaudeVersionProbe(IProcessRunner runner, string claudeExecutable = "claude")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _claudeExecutable = string.IsNullOrWhiteSpace(claudeExecutable) ? "claude" : claudeExecutable;
    }

    /// <summary>"claude-code/2.1.0" — the cached version, or the fallback until it is known.</summary>
    public string UserAgent => "claude-code/" + (_cached ?? Fallback);

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (_cached is string cached)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is string alreadyCached)
                return alreadyCached;

            string version = Fallback;
            try
            {
                ProcessResult result = await _runner
                    .RunAsync(_claudeExecutable, ["--version"], ProbeTimeout, ct).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    // `claude --version` prints something like "2.1.0 (Claude Code)".
                    string[] parts = result.StandardOutput
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length > 0 && parts[0].Length > 0)
                        version = parts[0];
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                version = Fallback;
            }

            _cached = version;
            return version;
        }
        finally
        {
            _gate.Release();
        }
    }
}
