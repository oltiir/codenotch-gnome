namespace Codenotch.Core.Tests;

/// Loads files from the fixtures/ directory copied beside the test binary
/// (see the csproj's CopyToOutputDirectory item).
public static class Fixtures
{
    private static readonly string Dir = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string Path(string name) => System.IO.Path.Combine(Dir, name);

    public static string Text(string name) => File.ReadAllText(Path(name));
}
