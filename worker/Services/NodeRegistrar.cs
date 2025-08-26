using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class NodeRegistrar
{
    private readonly IHubClient _hub;
    private readonly WorkerOptions _options;

    public NodeRegistrar(IHubClient hub, WorkerOptions options)
    {
        _hub = hub;
        _options = options;
    }

    public async Task RegisterAsync()
    {
        var baseUrl = $"http://{Environment.GetEnvironmentVariable("HOSTNAME") ?? _options.NodeId}:5000";
        await _hub.RegisterAsync(
            _options.HubUrl,
            _options.NodeSecret,
            _options.NodeId,
            baseUrl,
            _options.PoolConfig.Keys.ToArray(),
            _options.PoolConfig.Values.Sum(),
            _options.Labels.ToDictionary(k => k.Key, v => v.Value));
    }
}
