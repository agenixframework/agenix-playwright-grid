using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WorkerService.Application.Ports;

public interface IHubClient
{
    Task<bool> RegisterAsync(
        string hubUrl,
        string nodeSecret,
        string nodeId,
        string baseUrl,
        IEnumerable<string> apps,
        int capacity,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken ct = default);
}
