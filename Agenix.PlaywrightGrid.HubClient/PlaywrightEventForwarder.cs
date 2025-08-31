using Microsoft.Playwright;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
/// Optional helper that wires Playwright .NET page events to the Grid Hub via HubClient.SendApiLogAsync.
/// Use to auto-forward human-readable API-level messages (direction = "runner").
/// </summary>
public sealed class PlaywrightEventForwarder : IDisposable, IAsyncDisposable
{
    private readonly IPage _page;
    private readonly HubClient _client;
    private readonly string _browserId;
    private readonly Options _opts;

    private EventHandler<IConsoleMessage>? _onConsole;
    private EventHandler<string>? _onPageError;
    private EventHandler<IRequest>? _onRequest;
    private EventHandler<IResponse>? _onResponse;
    private EventHandler<IRequest>? _onRequestFinished;
    private EventHandler<IRequest>? _onRequestFailed;

    private PlaywrightEventForwarder(IPage page, HubClient client, string browserId, Options opts)
    {
        _page = page;
        _client = client;
        _browserId = browserId;
        _opts = opts;
        Attach();
    }

    /// <summary>
    /// Options controlling which events are forwarded.
    /// </summary>
    public sealed class Options
    {
        public bool Console { get; init; } = true;
        public bool PageError { get; init; } = true;
        public bool Request { get; init; } = true;
        public bool Response { get; init; } = true;
        public bool RequestFinished { get; init; } = false;
        public bool RequestFailed { get; init; } = true;
    }

    /// <summary>
    /// Attaches event listeners to the given page and starts forwarding messages to the hub.
    /// </summary>
    public static PlaywrightEventForwarder Attach(IPage page, HubClient client, string browserId,
        Options? options = null)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (string.IsNullOrWhiteSpace(browserId))
        {
            throw new ArgumentNullException(nameof(browserId));
        }

        return new PlaywrightEventForwarder(page, client, browserId, options ?? new Options());
    }

    private void Attach()
    {
        if (_opts.Console)
        {
            _onConsole = (_, msg) =>
            {
                // Example: console[error]: Something bad
                var text = $"console[{msg.Type}]: {msg.Text}";
                _ = SafeSend(text);
            };
            _page.Console += _onConsole;
        }

        if (_opts.PageError)
        {
            _onPageError = (_, error) =>
            {
                var text = $"pageerror: {error}";
                _ = SafeSend(text);
            };
            _page.PageError += _onPageError;
        }

        if (_opts.Request)
        {
            _onRequest = (_, req) =>
            {
                var text = $"request: {req.Method} {req.Url}";
                _ = SafeSend(text);
            };
            _page.Request += _onRequest;
        }

        if (_opts.Response)
        {
            _onResponse = (_, rsp) =>
            {
                var text = $"response: {rsp.Status} {rsp.Request.Method} {rsp.Url}";
                _ = SafeSend(text);
            };
            _page.Response += _onResponse;
        }

        if (_opts.RequestFinished)
        {
            _onRequestFinished = (_, req) =>
            {
                var text = $"request finished: {req.Method} {req.Url}";
                _ = SafeSend(text);
            };
            _page.RequestFinished += _onRequestFinished;
        }

        if (_opts.RequestFailed)
        {
            _onRequestFailed = (_, req) =>
            {
                var text = $"request failed: {req.Method} {req.Url} failure={req.Failure}";
                _ = SafeSend(text);
            };
            _page.RequestFailed += _onRequestFailed;
        }
    }

    private Task SafeSend(string text)
    {
        try
        {
            return _client.SendApiLogAsync(_browserId, text);
        }
        catch
        {
            // Swallow to avoid impacting tests; this is best-effort logging.
            return Task.CompletedTask;
        }
    }

    private void Detach()
    {
        if (_onConsole is not null)
        {
            _page.Console -= _onConsole;
        }

        if (_onPageError is not null)
        {
            _page.PageError -= _onPageError;
        }

        if (_onRequest is not null)
        {
            _page.Request -= _onRequest;
        }

        if (_onResponse is not null)
        {
            _page.Response -= _onResponse;
        }

        if (_onRequestFinished is not null)
        {
            _page.RequestFinished -= _onRequestFinished;
        }

        if (_onRequestFailed is not null)
        {
            _page.RequestFailed -= _onRequestFailed;
        }

        _onConsole = null;
        _onPageError = null;
        _onRequest = null;
        _onResponse = null;
        _onRequestFinished = null;
        _onRequestFailed = null;
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Detach();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

public static class PlaywrightEventForwarderExtensions
{
    /// <summary>
    /// Convenience extension to attach forwarding to a page.
    /// </summary>
    public static PlaywrightEventForwarder ForwardApiLogs(this IPage page, HubClient client, string browserId,
        PlaywrightEventForwarder.Options? options = null)
    {
        return PlaywrightEventForwarder.Attach(page, client, browserId, options);
    }
}
