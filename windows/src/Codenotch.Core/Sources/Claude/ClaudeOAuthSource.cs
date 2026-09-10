using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;

namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// Reads Claude usage straight from the vendor, because CodexBar ships no Windows
/// CLI. We use the token Claude Code already wrote and send it only to
/// api.anthropic.com over HTTPS; nothing is stored and nothing is logged.
/// </summary>
public sealed class ClaudeOAuthSource : IUsageSource
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "oauth-2025-04-20";

    private readonly HttpClient _http;
    private readonly string _credentialsPath;
    private readonly ClaudeRefreshPolicy _refresh;
    private readonly RateLimitGate _gate;
    private readonly ClaudeVersionProbe _version;
    private readonly IClock _clock;

    public ClaudeOAuthSource(HttpClient http, string credentialsPath,
                             ClaudeRefreshPolicy refresh, RateLimitGate gate,
                             ClaudeVersionProbe version, IClock clock)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentialsPath = credentialsPath ?? throw new ArgumentNullException(nameof(credentialsPath));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string Id => UsageSourceIds.Claude;

    /// <summary>No credential file means Claude Code was never signed in here, so we do not show a row.</summary>
    public bool IsAvailable()
    {
        try
        {
            return File.Exists(_credentialsPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default)
    {
        ClaudeCredentials credentials;
        try
        {
            credentials = ClaudeCredentialsFile.Read(_credentialsPath);
        }
        catch (ClaudeCredentialException ex)
        {
            return [Error(ex.UserMessage)];
        }
        catch (Exception)
        {
            return [Error(ClaudeCredentialException.MessageFor(ClaudeCredentialProblem.Unreadable))];
        }

        DateTimeOffset now = _clock.UtcNow;
        if (credentials.IsExpired(now))
        {
            (RefreshOutcome outcome, ClaudeCredentials refreshed) = await _refresh
                .EnsureFreshAsync(credentials, () => ClaudeCredentialsFile.Read(_credentialsPath), ct)
                .ConfigureAwait(false);
            if (outcome is not (RefreshOutcome.NotNeeded or RefreshOutcome.Refreshed))
                return [Error(ClaudeRefreshPolicy.ExpiredMessage)];
            credentials = refreshed;
        }

        string key = RateLimitGate.KeyForToken(credentials.AccessToken);
        if (_gate.IsBlocked(key, out DateTimeOffset until))
        {
            // Never bypassed, not even by "Refresh now" (D3).
            string wait = UsageText.Countdown(until.ToString("o", CultureInfo.InvariantCulture), _clock.UtcNow);
            return [Error(wait.Length > 0 ? $"rate limited; retrying in {wait}" : "rate limited")];
        }

        string version = await _version.GetVersionAsync(ct).ConfigureAwait(false);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credentials.AccessToken);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            // CodexBar also sends Content-Type on this GET. HttpClient refuses
            // content headers on a request with no body -- TryAddWithoutValidation
            // returns false and drops it -- and giving a GET an empty body just to
            // carry the header would add a Content-Length of its own. The endpoint
            // does not need it, so the header is deliberately absent.
            request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
            request.Headers.TryAddWithoutValidation("User-Agent", "claude-code/" + version);

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            int code = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return [Error("unauthorized, run `claude` to sign in again")];

            if (code == 429)
            {
                response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values);
                _gate.RecordRateLimit(key, RetryAfter.Parse(values?.FirstOrDefault(), _clock.UtcNow));
                return [Error("rate limited")];
            }

            if (!response.IsSuccessStatusCode)
                return [Error($"HTTP {code}", code)];

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ClaudeUsageResponse? usage;
            try
            {
                usage = JsonSerializer.Deserialize<ClaudeUsageResponse>(body, CodexBarJson.Options);
            }
            catch (JsonException)
            {
                return [Error("invalid response")];
            }
            if (usage is null)
                return [Error("invalid response")];

            _gate.RecordSuccess(key);
            return [ClaudeUsageMapper.ToEntry(usage, _clock.UtcNow)];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return [Error("timed out")];
        }
        catch (HttpRequestException ex)
        {
            return [Error($"network error: {ex.Message}")];
        }
        catch (InvalidOperationException ex)
        {
            return [Error($"network error: {ex.Message}")];
        }
    }

    private static ProviderEntry Error(string message, int? code = null)
        => ErrorEntry.For(UsageSourceIds.Claude, message, kind: "provider", code: code);
}
