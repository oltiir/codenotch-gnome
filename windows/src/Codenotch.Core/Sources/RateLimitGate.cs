using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Codenotch.Core.Sources;

/// <summary>
/// CodexBar's rate-limit gate. Once a vendor answers 429 we stop calling it for
/// as long as it asked (or five minutes if it did not say), and — per D3 —
/// "Refresh now" does not bypass this. Hammering an endpoint that just told us
/// to back off is how an account gets a longer ban, not a fresher number.
/// </summary>
public sealed class RateLimitGate
{
    private static readonly TimeSpan DefaultCooldownInterval = TimeSpan.FromMinutes(5);

    private readonly IClock _clock;
    private readonly TimeSpan _cooldown;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _blocked = new(StringComparer.Ordinal);

    public RateLimitGate(IClock clock, TimeSpan? defaultCooldown = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _cooldown = defaultCooldown ?? DefaultCooldownInterval;
    }

    public bool IsBlocked(string key, out DateTimeOffset until)
    {
        until = default;
        if (string.IsNullOrEmpty(key))
            return false;
        if (!_blocked.TryGetValue(key, out DateTimeOffset stored))
            return false;
        if (stored <= _clock.UtcNow)
        {
            _blocked.TryRemove(key, out _);
            return false;
        }
        until = stored;
        return true;
    }

    /// <summary>
    /// A second 429 never shortens an existing block: the longer of the two wins,
    /// so a vendor that sends a small Retry-After while we are already backing off
    /// cannot talk us into calling sooner.
    /// </summary>
    public void RecordRateLimit(string key, DateTimeOffset? retryAfter)
    {
        if (string.IsNullOrEmpty(key))
            return;
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset until = retryAfter is DateTimeOffset ra && ra > now ? ra : now + _cooldown;
        _blocked.AddOrUpdate(key, until, (_, existing) => existing > until ? existing : until);
    }

    public void RecordSuccess(string key)
    {
        if (!string.IsNullOrEmpty(key))
            _blocked.TryRemove(key, out _);
    }

    /// <summary>
    /// SHA-256 hex of the UTF-8 token, so the dictionary key is not the secret and
    /// a crash dump or a debugger watch window does not hand out an access token.
    /// </summary>
    public static string KeyForToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

public static class RetryAfter
{
    /// <summary>The longest Retry-After we will honour; anything larger is a broken header.</summary>
    private const double MaxDeltaSeconds = 24 * 60 * 60;

    /// <summary>
    /// Both spellings RFC 9110 allows: delta-seconds ("120") or an HTTP-date
    /// ("Tue, 08 Sep 2026 04:00:00 GMT"). null when absent or unparsable.
    /// </summary>
    public static DateTimeOffset? Parse(string? header, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;
        string value = header.Trim();

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            // double.TryParse accepts "Infinity" and "1e30", and AddSeconds throws
            // on either. A vendor that sends nonsense must still get gated, so an
            // absurd delta is capped at a day rather than taking the fetch down.
            if (double.IsNaN(seconds))
                return null;
            if (seconds <= 0)
                return now;
            return now.AddSeconds(Math.Min(seconds, MaxDeltaSeconds));
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
            return date;

        return null;
    }
}
