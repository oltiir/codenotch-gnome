using Codenotch.Core.Model;

namespace Codenotch.Core.Sources;

/// <summary>
/// One place usage can come from. Sources always answer in the CodexBar wire
/// shape, so the built-in vendor calls and a `codexbar serve` endpoint feed the
/// exact same presentation path.
/// </summary>
public interface IUsageSource
{
    /// <summary>"claude" | "codex" | "endpoint".</summary>
    string Id { get; }

    /// <summary>
    /// Cheap and synchronous, no I/O beyond File.Exists: is this source usable on
    /// this machine? A provider whose credential file is absent is simply not
    /// shown, mirroring install.sh's enable_if.
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Never throws except <see cref="OperationCanceledException"/>. Failures come
    /// back as entries carrying Error.Message, because the UI has a place to draw
    /// those and no place to draw an exception.
    /// </summary>
    Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default);
}

public static class UsageSourceIds
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Endpoint = "endpoint";
}

internal static class ErrorEntry
{
    public static ProviderEntry For(string provider, string message, string? kind = null, int? code = null)
        => new()
        {
            Provider = provider,
            Error = new ErrorDto { Message = message, Kind = kind, Code = code },
        };
}
