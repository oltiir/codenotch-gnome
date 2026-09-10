using System.ComponentModel;
using System.Diagnostics;

namespace Codenotch.Core;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>
    /// The executable was not on PATH at all. Distinct from "ran and failed" so
    /// <see cref="Sources.Claude.ClaudeRefreshPolicy"/> can report CliUnavailable
    /// instead of pretending a refresh was attempted.
    /// </summary>
    public static ProcessResult NotFound { get; } = new(-1, string.Empty, string.Empty, false);
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
                                 TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Runs a child process with no console window, capturing both streams. Never
/// throws: a missing executable comes back as <see cref="ProcessResult.NotFound"/>
/// and a hung one as <c>TimedOut</c>. The app only ever shells out to `claude`,
/// and it does so on a background thread while the tray keeps ticking, so a
/// stuck CLI must not take the process with it.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
                                              TimeSpan timeout, CancellationToken ct = default)
    {
        // On Windows a Node CLI installed by npm is `claude.cmd`; a native build is
        // `claude.exe`. Try the bare name first (works when the caller passed a full
        // path or when a shim exists) and fall back to the two Windows spellings.
        foreach (string candidate in Candidates(fileName))
        {
            ProcessResult? result = await TryRunAsync(candidate, arguments, timeout, ct).ConfigureAwait(false);
            if (result is not null)
                return result;
        }
        return ProcessResult.NotFound;
    }

    private static IEnumerable<string> Candidates(string fileName)
    {
        yield return fileName;
        if (Path.HasExtension(fileName))
            yield break;
        yield return fileName + ".cmd";
        yield return fileName + ".exe";
    }

    /// <summary>Returns null when the executable itself could not be found.</summary>
    private static async Task<ProcessResult?> TryRunAsync(string fileName, IReadOnlyList<string> arguments,
                                                          TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        // ArgumentList quotes each argument itself, so no hand-rolled escaping.
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return null;          // not on PATH under this spelling
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        if (process is null)
            return null;

        using (process)
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(process);
                return new ProcessResult(-1, await Drain(stdout).ConfigureAwait(false),
                                             await Drain(stderr).ConfigureAwait(false), true);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
            return new ProcessResult(process.ExitCode,
                                     await Drain(stdout).ConfigureAwait(false),
                                     await Drain(stderr).ConfigureAwait(false), false);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone, or we lost the race with its own exit. Nothing to do.
        }
    }

    private static async Task<string> Drain(Task<string> stream)
    {
        try
        {
            return await stream.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
