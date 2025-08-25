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
            hubUrl: _options.HubUrl,
            nodeSecret: _options.NodeSecret,
            nodeId: _options.NodeId,
            baseUrl: baseUrl,
            apps: _options.PoolConfig.Keys.ToArray(),
            capacity: _options.PoolConfig.Values.Sum(),
            labels: _options.Labels.ToDictionary(k => k.Key, v => v.Value));
    }
}
