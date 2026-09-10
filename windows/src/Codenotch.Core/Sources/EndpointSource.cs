using System.Net.Http;
using System.Text.Json;
using Codenotch.Core.Model;

namespace Codenotch.Core.Sources;

/// <summary>
/// Reads a `codexbar serve` endpoint, the way the GNOME, COSMIC and waybar ports
/// do. This is the parity path: a WSL user running the real CodexBar gets byte
/// for byte what Linux gets, including its pace lines.
/// </summary>
public sealed class EndpointSource : IUsageSource
{
    private readonly HttpClient _http;
    private readonly string _endpointUrl;

    public EndpointSource(HttpClient http, string endpointUrl)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(endpointUrl))
            throw new ArgumentException("An endpoint URL is required.", nameof(endpointUrl));
        _endpointUrl = endpointUrl.Trim();
    }

    public string Id => UsageSourceIds.Endpoint;

    public bool IsAvailable() => true;

    public string EndpointUrl => _endpointUrl;

    /// <summary>The host the UI names in the offline callout.</summary>
    public string HostLabel => Uri.TryCreate(_endpointUrl, UriKind.Absolute, out Uri? uri) ? uri.Host : _endpointUrl;

    public async Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpointUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // extension.js throws `HTTP ${status}` here and shows it verbatim.
                return [ErrorEntry.For("codexbar", $"HTTP {(int)response.StatusCode}",
                                       kind: "http", code: (int)response.StatusCode)];
            }

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return UsageEnvelopeParser.ProvidersFrom(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return [Unreachable("timed out")];
        }
        catch (HttpRequestException ex)
        {
            return [Unreachable(ex.Message)];
        }
        catch (JsonException ex)
        {
            return [Unreachable(ex.Message)];
        }
        catch (InvalidOperationException ex)
        {
            return [Unreachable(ex.Message)];
        }
    }

    private ProviderEntry Unreachable(string reason)
        => ErrorEntry.For("codexbar", $"Can't reach {HostLabel}: {reason}", kind: "transport");
}
