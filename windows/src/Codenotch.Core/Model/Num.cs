namespace Codenotch.Core.Model;

public static class Num
{
    /// <summary>
    /// JS Math.round semantics: half away from zero for positives, toward +inf
    /// overall. .NET's Math.Round(double) is banker's rounding, which would turn
    /// 12.5 into 12 and quietly disagree with extension.js on every .5 percent.
    /// </summary>
    public static long JsRound(double v) => (long)Math.Floor(v + 0.5);
}
