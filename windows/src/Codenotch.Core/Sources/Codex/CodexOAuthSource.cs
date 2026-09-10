using System.Net;
using System.Net.Http;
using System.Text.Json;
using Codenotch.Core.Model;

namespace Codenotch.Core.Sources.Codex;

/// <summary>
/// Reads Codex usage straight from the vendor. No token refresh in v1: the Codex
/// CLI rotates its own tokens on use, and re-reading auth.json on the next poll
/// picks that up for free.
/// </summary>
public sealed class CodexOAuthSource : IUsageSource
{
    public const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    private readonly HttpClient _http;
    private readonly string _authJsonPath;
    private readonly IClock _clock;

    public CodexOAuthSource(HttpClient http, string authJsonPath, IClock clock)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _authJsonPath = authJsonPath ?? throw new ArgumentNullException(nameof(authJsonPath));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string Id => UsageSourceIds.Codex;

    public bool IsAvailable()
    {
        try
        {
            return File.Exists(_authJsonPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default)
    {
        CodexCredentials credentials;
        try
        {
            credentials = CodexCredentialsFile.Read(_authJsonPath);
        }
        catch (CodexCredentialException ex)
        {
            return [Error(ex.UserMessage)];
        }
        catch (Exception)
        {
            return [Error(CodexCredentialException.MessageFor(CodexCredentialProblem.Unreadable))];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credentials.AccessToken);
            if (!string.IsNullOrWhiteSpace(credentials.AccountId))
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", "Codenotch");

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return [Error("unauthorized, run `codex login`")];

            int code = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return [Error($"HTTP {code}", code)];

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            CodexUsageResponse? usage;
            try
            {
                usage = JsonSerializer.Deserialize<CodexUsageResponse>(body, CodexBarJson.Options);
            }
            catch (JsonException)
            {
                return [Error("invalid response")];
            }
            if (usage is null)
                return [Error("invalid response")];

            ProviderEntry entry = CodexUsageMapper.ToEntry(usage);
            PaceAttachment.Attach(entry, _clock.UtcNow,
                CodexUsageMapper.DefaultPrimaryWindowMinutes,
                CodexUsageMapper.DefaultSecondaryWindowMinutes);
            return [entry];
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
        => ErrorEntry.For(UsageSourceIds.Codex, message, kind: "provider", code: code);
}
