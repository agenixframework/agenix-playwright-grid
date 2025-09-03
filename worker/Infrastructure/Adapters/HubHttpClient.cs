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

using System.Text.Json;
using WorkerService.Application.Ports;

namespace WorkerService.Infrastructure.Adapters;

public sealed class HubHttpClient : IHubClient
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = Microsoft.Extensions.Logging.LoggerFactory
        .Create(b => b.AddSimpleConsole())
        .CreateLogger("worker.register");

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
                    Logger.LogInformation("[Register] {status} {body} -> {url}", resp.StatusCode, JsonSerializer.Serialize(body), url);
                    return true;
                }

                Logger.LogWarning("[Register] Failed {statusCode} {status}: {text}", (int)resp.StatusCode, resp.StatusCode, text);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Register] Error attempt {attempt}: {message}", attempt, ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), ct);
            }
            catch { }
        }

        Logger.LogError("[Register] Giving up (hub unreachable or rejected).");
        return false;
    }
}
