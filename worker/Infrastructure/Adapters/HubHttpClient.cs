using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkerService.Application.Ports;

namespace WorkerService.Infrastructure.Adapters;

public sealed class HubHttpClient : IHubClient
{
    public async Task<bool> RegisterAsync(
        string hubUrl,
        string nodeSecret,
        string nodeId,
        string baseUrl,
        IEnumerable<string> apps,
        int capacity,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken ct = default)
    {
        var url = $"{hubUrl.TrimEnd('/')}/node/register";
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Remove("x-hub-secret");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", nodeSecret);

        var body = new
        {
            NodeId = nodeId,
            BaseUrl = baseUrl,
            Apps = apps.ToArray(),
            Capacity = capacity,
            Labels = labels
        };

        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var resp = await client.PostAsJsonAsync(url, body, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        $"[Register] {resp.StatusCode} {System.Text.Json.JsonSerializer.Serialize(body)}-> {url}");
                    return true;
                }

                Console.WriteLine($"[Register] Failed {(int)resp.StatusCode} {resp.StatusCode}: {text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Register] Error attempt {attempt}: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), ct);
            }
            catch { }
        }

        Console.WriteLine("[Register] Giving up (hub unreachable or rejected).");
        return false;
    }
}
