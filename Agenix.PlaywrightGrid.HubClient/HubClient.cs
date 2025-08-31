using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
/// Minimal HTTP client for connecting test runners to the Playwright Grid hub.
/// Reads HUB_URL and HUB_RUNNER_SECRET from the environment by default, with constructor overrides.
/// </summary>
public sealed class HubClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Polly.Retry.AsyncRetryPolicy<HttpResponseMessage> _retry;

    /// <summary>
    /// Typed client constructor for DI: prefer registering via IServiceCollection.AddHttpClient<HubClient>().
    /// Resilience (retries) is provided via per-call Polly policy to ensure safe re-sends for POST bodies.
    /// </summary>
    public HubClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = false;
        _retry = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r =>
                r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
    }

    /// <summary>
    /// Create a HubClient configured for the Grid hub.
    /// </summary>
    /// <param name="hubUrl">Optional hub base URL. If null, uses env HUB_URL or http://localhost:5100.</param>
    /// <param name="runnerSecret">Optional secret header value. If null, uses env HUB_RUNNER_SECRET or runner-secret.</param>
    /// <param name="handler">Optional custom message handler.</param>
    /// <param name="timeout">Optional request timeout (defaults to 15s).</param>
    /// <exception cref="ArgumentNullException"></exception>
    public HubClient(string? hubUrl = null, string? runnerSecret = null, HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        var rawUrl = hubUrl ?? throw new ArgumentNullException(nameof(hubUrl));

        // Sanitize invalid/unspecified hosts that cannot be used for outbound connections (e.g., 0.0.0.0, *, +)
        string? baseUrl = null;
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
        {
            if (string.Equals(parsed.Host, "0.0.0.0", StringComparison.Ordinal) ||
                string.Equals(parsed.Host, "*", StringComparison.Ordinal) ||
                string.Equals(parsed.Host, "+", StringComparison.Ordinal))
            {
                var b = new UriBuilder(parsed) { Host = "localhost" };
                baseUrl = b.Uri.ToString().TrimEnd('/');
            }
            else
            {
                baseUrl = parsed.ToString().TrimEnd('/');
            }
        }

        if (baseUrl is null)
        {
            throw new ArgumentException("Invalid hub URL", nameof(hubUrl));
        }

        var secret = (runnerSecret ?? Environment.GetEnvironmentVariable("HUB_RUNNER_SECRET") ?? "runner-secret")
            .Trim();

        var httpHandler = handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, // prevents dropping custom headers across redirects
            UseProxy = false
        };
        _http = new HttpClient(httpHandler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Remove("x-hub-secret");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", secret);
        _ownsClient = true;
        _retry = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r =>
                r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
    }

    /// <summary>
    /// Create a HubClient using IConfiguration via HubUrlProvider (env HUB_URL has precedence over config Hub:Url).
    /// Throws if neither is configured.
    /// </summary>
    public HubClient(IConfiguration configuration, string? runnerSecret = null, HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
        : this(HubUrlProvider.Get(configuration), runnerSecret, handler, timeout)
    {
    }

    /// <summary>
    /// Releases all resources used by the HubClient instance.
    /// </summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Checks the health status of the Hub by sending a request to the /health endpoint.
    /// </summary>
    /// <returns>A boolean indicating whether the Hub is healthy (true if the /health endpoint returns a success status code, false otherwise).</returns>
    public async Task<bool> HealthAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a single Playwright API/protocol log line from the test runner to the hub for the given browserId.
    /// Requires HUB_RUNNER_SECRET and that the browserId is already attributed to a run (BorrowAsync with runId).
    /// Use the convenience overloads to omit the direction; they default to "runner".
    /// </summary>
    /// <param name="browserId">The hub-issued browser/session identifier.</param>
    /// <param name="text">The log line to send; empty/whitespace is ignored.</param>
    /// <param name="timestampUtc">Optional timestamp (UTC). If null, DateTime.UtcNow is used.</param>
    /// <param name="direction">Optional direction tag, e.g., "runner" or "server".</param>
    public async Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc = null,
        string? direction = null)
    {
        if (string.IsNullOrWhiteSpace(browserId))
        {
            throw new ArgumentNullException(nameof(browserId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var url = $"/results/browser/{WebUtility.UrlEncode(browserId)}/api-logs";
        var body = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["ts"] = (timestampUtc ?? DateTime.UtcNow).ToString("O")
        };
        if (!string.IsNullOrWhiteSpace(direction))
        {
            body["direction"] = direction;
        }

        var resp = await _retry.ExecuteAsync(() => _http.PostAsJsonAsync(url, body));
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Convenience overload: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text)
    {
        return SendApiLogAsync(browserId, text, null, "runner");
    }

    /// <summary>
    /// Convenience overload: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc)
    {
        return SendApiLogAsync(browserId, text, timestampUtc, "runner");
    }

    /// <summary>
    /// Sends multiple Playwright API/protocol logs from the test runner in one batch.
    /// This overload accepts IEnumerableand wraps each as an object with direction="runner".
    /// </summary>
    public async Task SendApiLogsAsync(string browserId, IEnumerable<string> texts)
    {
        if (string.IsNullOrWhiteSpace(browserId))
        {
            throw new ArgumentNullException(nameof(browserId));
        }

        if (texts is null)
        {
            return;
        }

        var items = texts.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new Dictionary<string, object?> { ["text"] = s, ["direction"] = "runner" })
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var url = $"/results/browser/{WebUtility.UrlEncode(browserId)}/api-logs";
        var resp = await _retry.ExecuteAsync(() => _http.PostAsJsonAsync(url, items));
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Requests a browser session from the hub for the provided label key.
    /// Returns a tuple of browserId, wsEndpoint, labelKey, and browserType.
    /// </summary>
    /// <param name="labelKey">Label key describing the desired session capacity (e.g., App:Browser:Env).</param>
    /// <param name="runId">Optional run identifier to be attributed by the hub (sent as query runId).</param>
    /// <returns>A tuple with the borrowed browser id, WebSocket endpoint, the echoed label key and optional browser type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public async Task<(string browserId, string wsEndpoint, string labelKey, string? browserType)> BorrowAsync(
        string labelKey, string? runId = null)
    {
        var body = new Dictionary<string, string> { ["labelKey"] = labelKey };
        var url = string.IsNullOrWhiteSpace(runId)
            ? "/session/borrow"
            : $"/session/borrow?runId={WebUtility.UrlEncode(runId)}";

        var resp = await _retry.ExecuteAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            if (!string.IsNullOrWhiteSpace(runId))
            {
                req.Headers.TryAddWithoutValidation("Correlation-Id", runId);
            }
            return _http.SendAsync(req);
        });

        switch (resp.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new InvalidOperationException("401 Unauthorized: x-hub-secret does not match HUB_RUNNER_SECRET.");
            case HttpStatusCode.Forbidden:
                throw new InvalidOperationException("403 Forbidden: access denied by hub.");
        }

        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        var browserId = GetStr(root, "browserId") ?? throw new InvalidOperationException("Missing browserId");
        var ws = GetStr(root, "webSocketEndpoint") ??
                 GetStr(root, "wsEndpoint") ?? throw new InvalidOperationException("Missing ws endpoint");
        var browserType = GetStr(root, "browserType");

        return (browserId, ws, labelKey, browserType);

        static string? GetStr(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }

    /// <summary>
    /// Returns a borrowed browser session to the hub.
    /// </summary>
    /// <param name="labelKey">The key associated with the browser's label.</param>
    /// <param name="browserId">The unique identifier of the borrowed browser session.</param>
    /// <param name="runId">Optional run identifier to be attributed by the hub (sent as query runId).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReturnAsync(string labelKey, string browserId, string? runId = null)
    {
        var body = new Dictionary<string, string> { ["labelKey"] = labelKey, ["browserId"] = browserId };

        var url = string.IsNullOrWhiteSpace(runId)
            ? "/session/return"
            : $"/session/return?runId={WebUtility.UrlEncode(runId)}";
        var resp = await _retry.ExecuteAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            if (!string.IsNullOrWhiteSpace(runId))
            {
                req.Headers.TryAddWithoutValidation("Correlation-Id", runId);
            }
            return _http.SendAsync(req);
        });
        resp.EnsureSuccessStatusCode();
    }
}
