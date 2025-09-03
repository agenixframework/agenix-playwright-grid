#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
///     Minimal HTTP client for connecting test runners to the Playwright Grid hub.
///     Reads HUB_URL and HUB_RUNNER_SECRET from the environment by default, with constructor overrides.
/// </summary>
public sealed class HubClient : IDisposable
{
    private readonly Channel<LogEntry> _logChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _senderTask;

    private readonly int _logBatchSize;
    private readonly int _logFlushMs;
    private readonly int _logChannelCap;
    private readonly double _maxBatchesPerSecond;

    private DateTime _lastSend = DateTime.MinValue;

    private readonly struct LogEntry
    {
        public LogEntry(string browserId, string text, DateTime tsUtc, string? direction)
        {
            BrowserId = browserId;
            Text = text;
            TsUtc = tsUtc;
            Direction = direction;
        }

        public string BrowserId { get; }
        public string Text { get; }
        public DateTime TsUtc { get; }
        public string? Direction { get; }
    }
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retry;

    /// <summary>
    ///     Typed client constructor for DI: prefer registering via IServiceCollection.AddHttpClient()
    ///     Resilience (retries) is provided via per-call Polly policy to ensure safe re-sends for POST bodies.
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

        // Configure async log buffering defaults from the environment
        _logBatchSize = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_BATCH_SIZE"), 50);
        _logFlushMs = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_FLUSH_MS"), 200);
        _logChannelCap = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_CHANNEL_CAP"), 1000);
        _maxBatchesPerSecond = TryParsePositiveDouble(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_MAX_BATCHES_PER_SEC"), 10);

        var opts = new BoundedChannelOptions(_logChannelCap)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        };
        _logChannel = Channel.CreateBounded<LogEntry>(opts);
        _senderTask = Task.Run(async () => await SenderLoopAsync(_cts.Token).ConfigureAwait(false));
    }

    /// <summary>
    ///     Create a HubClient configured for the Grid hub.
    /// </summary>
    /// <param name="hubUrl">Optional hub base URL. If null, uses env HUB_URL or http://localhost:5100.</param>
    /// <param name="runnerSecret">Optional secret header value. If null, uses env HUB_RUNNER_SECRET or runner-secret.</param>
    /// <param name="handler">Optional custom message handler.</param>
    /// <param name="timeout">Optional request timeout (defaults to 15s).</param>
    /// <exception cref="ArgumentNullException"></exception>
    public HubClient(string? hubUrl = null, string? runnerSecret = null, HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        // If hubUrl is not provided, fall back to environment HUB_URL or default localhost:5100 as documented
        var rawUrl = !string.IsNullOrWhiteSpace(hubUrl)
            ? hubUrl
            : (Environment.GetEnvironmentVariable("HUB_URL") ?? "http://127.0.0.1:5100");

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

        // Configure async log buffering defaults from environment
        _logBatchSize = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_BATCH_SIZE"), 50);
        _logFlushMs = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_FLUSH_MS"), 200);
        _logChannelCap = TryParsePositiveInt(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_CHANNEL_CAP"), 1000);
        _maxBatchesPerSecond = TryParsePositiveDouble(Environment.GetEnvironmentVariable("HUBCLIENT_LOG_MAX_BATCHES_PER_SEC"), 10);

        var opts = new BoundedChannelOptions(_logChannelCap)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        };
        _logChannel = Channel.CreateBounded<LogEntry>(opts);
        _senderTask = Task.Run(async () => await SenderLoopAsync(_cts.Token).ConfigureAwait(false));
    }

    /// <summary>
    ///     Create a HubClient using IConfiguration via HubUrlProvider (env HUB_URL has precedence over config Hub:Url).
    ///     Throws if neither is configured.
    /// </summary>
    public HubClient(IConfiguration configuration, string? runnerSecret = null, HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
        : this(HubUrlProvider.Get(configuration), runnerSecret, handler, timeout)
    {
    }

    /// <summary>
    ///     Releases all resources used by the HubClient instance.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            // Do not block on background async sender to avoid sync-over-async deadlocks.
            // SenderLoopAsync will best-effort flush on cancellation; any errors are swallowed.
        }
        catch { /* no-op */ }

        if (_ownsClient)
        {
            _http.Dispose();
        }
        _cts.Dispose();
    }

    private static async Task EnsureSuccessOrThrowDomainAsync(HttpResponseMessage resp, string operation, CancellationToken cancellationToken = default)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        var status = resp.StatusCode;
        string details = string.Empty;
        try
        {
            details = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore read failures
        }

        var msgBase = $"{(int)status} {status} during {operation}.";
        switch (status)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new AuthenticationException($"{msgBase} Verify HUB_RUNNER_SECRET and permissions.");
            case HttpStatusCode.ServiceUnavailable:
            case HttpStatusCode.TooManyRequests:
                throw new CapacityUnavailableException($"{msgBase} Capacity unavailable or rate limited.");
            default:
                throw new ProtocolException(string.IsNullOrWhiteSpace(details) ? msgBase : $"{msgBase} Body: {Truncate(details, 1024)}");
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max] + "…";
    }

    private static int TryParsePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var n) && n > 0 ? n : fallback;
    }

    private static double TryParsePositiveDouble(string? value, double fallback)
    {
        return double.TryParse(value, out var n) && n > 0 ? n : fallback;
    }

    private async Task SenderLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _logChannel.Reader;
        var buffer = new List<LogEntry>(_logBatchSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for at least one item
                var first = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                buffer.Add(first);

                var deadline = DateTime.UtcNow.AddMilliseconds(_logFlushMs);

                // Drain up to batch size or until flush deadline
                while (buffer.Count < _logBatchSize)
                {
                    if (reader.TryRead(out var next))
                    {
                        buffer.Add(next);
                        continue;
                    }

                    var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) break;

                    try { await Task.Delay(remaining, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }

                // Group by browserId and send batches
                await SendGroupedAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Clear();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow to keep background sender alive
                buffer.Clear();
            }
        }

        // Best-effort flush any remaining items
        try
        {
            while (reader.TryRead(out var next))
            {
                buffer.Add(next);
                if (buffer.Count >= _logBatchSize)
                {
                    await SendGroupedAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
            {
                await SendGroupedAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                buffer.Clear();
            }
        }
        catch { /* ignore on shutdown */ }
    }

    private async Task SendGroupedAsync(List<LogEntry> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;

        // Group into per-browser batches
        var groups = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            if (!groups.TryGetValue(it.BrowserId, out var list))
            {
                list = new List<Dictionary<string, object?>>();
                groups[it.BrowserId] = list;
            }

            var obj = new Dictionary<string, object?>
            {
                ["text"] = it.Text,
                ["ts"] = it.TsUtc.ToString("O")
            };
            if (!string.IsNullOrWhiteSpace(it.Direction))
            {
                obj["direction"] = it.Direction;
            }
            list.Add(obj);
        }

        var minInterval = _maxBatchesPerSecond > 0 ? TimeSpan.FromSeconds(1 / _maxBatchesPerSecond) : TimeSpan.Zero;

        foreach (var kvp in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Rate limit between HTTP sends
            if (minInterval > TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;
                var sinceLast = now - _lastSend;
                if (sinceLast < minInterval)
                {
                    try { await Task.Delay(minInterval - sinceLast, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }

            var browserId = kvp.Key;
            var payload = kvp.Value.ToArray();
            var url = $"/results/browser/{WebUtility.UrlEncode(browserId)}/api-logs";

            try
            {
                var resp = await _retry.ExecuteAsync(ct => _http.PostAsJsonAsync(url, payload, ct), cancellationToken).ConfigureAwait(false);
                _lastSend = DateTime.UtcNow;
                // do not throw to caller; swallow errors
                try { await EnsureSuccessOrThrowDomainAsync(resp, "SendApiLogsBuffered", cancellationToken).ConfigureAwait(false); } catch { }
            }
            catch
            {
                // swallow errors to avoid impacting runner
            }
        }
    }

    /// <summary>
    ///     Checks the health status of the Hub by sending a request to the /health endpoint.
    /// </summary>
    /// <returns>
    ///     A boolean indicating whether the Hub is healthy (true if the /health endpoint returns a success status code,
    ///     false otherwise).
    /// </returns>
    public async Task<bool> HealthAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/health").ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Sends a single Playwright API/protocol log line from the test runner to the hub for the given browserId.
    ///     Requires HUB_RUNNER_SECRET and that the browserId is already attributed to a run (BorrowAsync with runId).
    ///     Use the convenience overloads to omit the direction; they default to "runner".
    /// </summary>
    /// <param name="browserId">The hub-issued browser/session identifier.</param>
    /// <param name="text">The log line to send; empty/whitespace is ignored.</param>
    /// <param name="timestampUtc">Optional timestamp (UTC). If null, DateTime.UtcNow is used.</param>
    /// <param name="direction">Optional direction tag, e.g., "runner" or "server".</param>
    public async Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc = null,
        string? direction = null)
    {
        await SendApiLogAsync(browserId, text, timestampUtc, direction, CancellationToken.None);
    }

    /// <summary>
    ///     Sends a single Playwright API/protocol log line with cancellation support.
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc, string? direction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(browserId))
        {
            throw new ArgumentNullException(nameof(browserId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        var ts = timestampUtc ?? DateTime.UtcNow;
        // Fire-and-forget enqueue; drop if buffer is full
        _logChannel.Writer.TryWrite(new LogEntry(browserId, text, ts, direction));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Convenience overload: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text)
    {
        return SendApiLogAsync(browserId, text, null, "runner");
    }

    /// <summary>
    ///     Convenience overload: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc)
    {
        return SendApiLogAsync(browserId, text, timestampUtc, "runner");
    }

    /// <summary>
    ///     Convenience overload with cancellation: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text, CancellationToken cancellationToken)
    {
        return SendApiLogAsync(browserId, text, null, "runner", cancellationToken);
    }

    /// <summary>
    ///     Convenience overload with cancellation: defaults direction to "runner".
    /// </summary>
    public Task SendApiLogAsync(string browserId, string text, DateTime? timestampUtc, CancellationToken cancellationToken)
    {
        return SendApiLogAsync(browserId, text, timestampUtc, "runner", cancellationToken);
    }

    /// <summary>
    ///     Sends multiple Playwright API/protocol logs from the test runner in one batch.
    ///     This overload accepts IEnumerableand wraps each as an object with direction="runner".
    /// </summary>
    public async Task SendApiLogsAsync(string browserId, IEnumerable<string>? texts)
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
        var resp = await _retry.ExecuteAsync(() => _http.PostAsJsonAsync(url, items)).ConfigureAwait(false);
        await EnsureSuccessOrThrowDomainAsync(resp, "SendApiLogs").ConfigureAwait(false);
    }

    /// <summary>
    ///     Requests a browser session from the hub for the provided label key.
    ///     Returns a tuple of browserId, wsEndpoint, labelKey, and browserType.
    /// </summary>
    /// <param name="labelKey">Label key describing the desired session capacity (e.g., App:Browser:Env).</param>
    /// <param name="runId">Optional run identifier to be attributed by the hub (sent as query runId).</param>
    /// <returns>A tuple with the borrowed browser id, WebSocket endpoint, the echoed label key and optional browser type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public async Task<(string browserId, string wsEndpoint, string labelKey, string? browserType)> BorrowAsync(
        string labelKey, string? runId = null)
    {
        return await BorrowAsync(labelKey, runId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Requests a browser session with cancellation support.
    /// </summary>
    public async Task<(string browserId, string wsEndpoint, string labelKey, string? browserType)> BorrowAsync(
        string labelKey, string? runId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string> { ["labelKey"] = labelKey };
        var url = string.IsNullOrWhiteSpace(runId)
            ? "/session/borrow"
            : $"/session/borrow?runId={WebUtility.UrlEncode(runId)}";

        var resp = await _retry.ExecuteAsync(ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            if (!string.IsNullOrWhiteSpace(runId))
            {
                req.Headers.TryAddWithoutValidation("Correlation-Id", runId);
            }

            return _http.SendAsync(req, ct);
        }, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessOrThrowDomainAsync(resp, "Borrow", cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            var browserId = GetStr(root, "browserId");
            var ws = GetStr(root, "webSocketEndpoint") ?? GetStr(root, "wsEndpoint");
            var browserType = GetStr(root, "browserType");

            if (string.IsNullOrWhiteSpace(browserId))
            {
                throw new ProtocolException("Missing browserId in hub response.");
            }
            if (string.IsNullOrWhiteSpace(ws))
            {
                throw new ProtocolException("Missing ws endpoint in hub response.");
            }

            return (browserId, ws, labelKey, browserType);
        }
        catch (ProtocolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProtocolException("Failed to parse hub response for Borrow.", ex);
        }

        static string? GetStr(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }

    /// <summary>
    ///     Returns a borrowed browser session to the hub.
    /// </summary>
    /// <param name="labelKey">The key associated with the browser's label.</param>
    /// <param name="browserId">The unique identifier of the borrowed browser session.</param>
    /// <param name="runId">Optional run identifier to be attributed by the hub (sent as query runId).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReturnAsync(string labelKey, string browserId, string? runId = null)
    {
        await ReturnAsync(labelKey, browserId, runId, CancellationToken.None);
    }

    /// <summary>
    ///     Returns a borrowed browser session to the hub with cancellation support.
    /// </summary>
    public async Task ReturnAsync(string labelKey, string browserId, string? runId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string> { ["labelKey"] = labelKey, ["browserId"] = browserId };

        var url = string.IsNullOrWhiteSpace(runId)
            ? "/session/return"
            : $"/session/return?runId={WebUtility.UrlEncode(runId)}";
        var resp = await _retry.ExecuteAsync(ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            if (!string.IsNullOrWhiteSpace(runId))
            {
                req.Headers.TryAddWithoutValidation("Correlation-Id", runId);
            }

            return _http.SendAsync(req, ct);
        }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowDomainAsync(resp, "Return", cancellationToken).ConfigureAwait(false);
    }
}
