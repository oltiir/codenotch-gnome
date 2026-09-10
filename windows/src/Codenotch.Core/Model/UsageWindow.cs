namespace Codenotch.Core.Model;

/// <summary>
/// Which heading a window sits under on claude.ai's usage panel. Session rows
/// come first; everything else is under "Weekly limits".
/// </summary>
public enum WindowGroup
{
    Session,
    Weekly,
}

/// <summary>
/// One rate window, named the way claude.ai names them: "Current session",
/// "All models", "Fable".
///
/// <paramref name="ResetsAt"/> stays the raw ISO string the wire carried, exactly
/// like windowsFrom() in extension.js. The presentation helpers are string-in,
/// string-out, which keeps them table-driven in the tests and means a timestamp
/// we cannot parse degrades to "no reset line" instead of a crash.
/// </summary>
public sealed record UsageWindow(string Key, WindowGroup Group, string Label, int Used, string? ResetsAt);
