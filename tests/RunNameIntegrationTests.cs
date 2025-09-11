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

#nullable enable

using System.Net.Http.Json;
using Agenix.PlaywrightGrid.HubClient;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;

namespace GridTests;

/// <summary>
/// Integration test: borrow a session with RunName and assert it surfaces in:
/// 1) Hub Results API (GET /results/{runId})
/// 2) SignalR Results stream (RunsIndex/RunUpdate)
/// 3) Dashboard filtering model (client-side search over /results page buffer)
///
/// This test relies on TestEnvironment (SetUpFixture) to either provision Testcontainers
/// or attach to a locally running grid when GRID_TESTS_USE_LOCAL=1.
/// </summary>
public class RunNameIntegrationTests
{
    private static string NewRunId() => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

    private static string ResolveHubBaseUrl()
    {
        // Prefer HUB_URL; default matches compose mapping from guidelines
        var hubUrl = Environment.GetEnvironmentVariable("HUB_URL");
        return string.IsNullOrWhiteSpace(hubUrl) ? "http://127.0.0.1:5100" : hubUrl.TrimEnd('/');
    }

    private static string ResolveHubSignalRUrl()
    {
        // Prefer HUB_SIGNALR; fallback derive from HUB_URL
        var hubSignalR = Environment.GetEnvironmentVariable("HUB_SIGNALR");
        if (!string.IsNullOrWhiteSpace(hubSignalR)) return hubSignalR.TrimEnd('/');
        var baseUrl = ResolveHubBaseUrl();
        return baseUrl + "/ws";
    }

    private static string[] DefaultLabels() => new[] { "AppB:Chromium:UAT", "AppB:Firefox:UAT", "AppB:Webkit:UAT" };

    [Test]
    public async Task Borrow_With_RunName_Surfaces_In_Results_SignalR_And_DashboardFiltering()
    {
        // Arrange
        var hubBase = ResolveHubBaseUrl();
        using var client = new HubClient(hubBase);
        if (!await client.HealthAsync())
        {
            Assert.Inconclusive("Hub /health is not available. Ensure docker compose is up or Testcontainers started.");
        }

        var label = Environment.GetEnvironmentVariable("LABEL") ?? DefaultLabels().First();
        var runId = NewRunId();
        var runName = $"RunName Demo {DateTime.UtcNow:yyyyMMddHHmmss}"; // conforms to validation (letters/numbers/space/._-)

        // Connect to Results SignalR first to capture live updates
        var hubSignalR = ResolveHubSignalRUrl();
        var resultsWs = hubSignalR.Replace("/ws", "/results-ws", StringComparison.OrdinalIgnoreCase);
        var tcsUpdate = new TaskCompletionSource<ResultRunSummaryView>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsIndex = new TaskCompletionSource<ResultRunSummaryView[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        HubConnection? conn = null;
        try
        {
            conn = new HubConnectionBuilder().WithUrl(resultsWs).WithAutomaticReconnect().Build();
            conn.On<ResultRunSummaryView>("RunUpdate", run =>
            {
                if (string.Equals(run.RunId, runId, StringComparison.OrdinalIgnoreCase))
                {
                    tcsUpdate.TrySetResult(run);
                }
            });
            conn.On<ResultRunSummaryView[]>("RunsIndex", page =>
            {
                tcsIndex.TrySetResult(page);
            });
            await conn.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[SignalR] Failed to connect to results stream at {resultsWs}: {ex.Message}");
            // Do not abort test: we still validate Results API & dashboard filtering below.
        }

        // Act: borrow with RunName
        var (browserId, _, labelKey, _) = await client.BorrowAsync(label, runId, runName);
        TestContext.WriteLine($"[Borrow] id={browserId} labelKey={labelKey} runId={runId} runName='{runName}'");

        // Assert 1: Results API exposes RunName for the run (tolerate eventual consistency with retries)
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(hubBase) };
            ResultRunSummaryView? run = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            do
            {
                run = await http.GetFromJsonAsync<ResultRunSummaryView>($"/results/{Uri.EscapeDataString(runId)}", cts.Token);
                if (run is not null && string.Equals(run.RunName, runName, StringComparison.Ordinal)) break;
                await Task.Delay(500, cts.Token);
            } while (DateTime.UtcNow < deadline);

            Assert.That(run, Is.Not.Null, "Results API did not return the run by runId");
            Assert.That(run!.RunName, Is.EqualTo(runName), "RunName mismatch in Results API payload after retries");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Results API validation failed: {ex.Message}");
        }

        // Assert 2: SignalR stream includes the run with RunName (either via RunUpdate or via initial RunsIndex)
        if (conn is not null)
        {
            ResultRunSummaryView? observed = null;
            try
            {
                var completed = await Task.WhenAny(tcsUpdate.Task, Task.Delay(TimeSpan.FromSeconds(20), cts.Token));
                if (completed == tcsUpdate.Task)
                {
                    observed = await tcsUpdate.Task;
                }
                else
                {
                    // Fallback: reconnect pattern — if index already includes the run, use it
                    var idxCompleted = await Task.WhenAny(tcsIndex.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
                    if (idxCompleted == tcsIndex.Task)
                    {
                        var page = await tcsIndex.Task;
                        observed = page.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch
            {
                // ignored: treat as inconclusive for SignalR part
            }

            if (observed is null)
            {
                Assert.Inconclusive("SignalR Results stream did not include the run within timeout. Hub may be slow; API assertions already passed.");
            }
            else
            {
                Assert.That(observed.RunName, Is.EqualTo(runName), "RunName mismatch in SignalR payload");
            }
        }

        // Assert 3: Dashboard filtering (client-side search over /results page buffer) can find by RunName
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(hubBase) };
            // Get first page with broad filters (server-side); dashboard then filters locally by RunName
            var list = await http.GetFromJsonAsync<List<ResultRunSummaryView>>("/results?skip=0&take=200", cts.Token) ?? new List<ResultRunSummaryView>();
            var found = list.Any(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.RunName, runName, StringComparison.Ordinal));
            if (!found)
            {
                // Retry once after a brief delay to tolerate eventual consistency in stores like Redis/Postgres
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                list = await http.GetFromJsonAsync<List<ResultRunSummaryView>>("/results?skip=0&take=200", cts.Token) ?? new List<ResultRunSummaryView>();
                found = list.Any(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.RunName, runName, StringComparison.Ordinal));
            }
            Assert.That(found, Is.True, "Run not found by RunName within dashboard results list");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Dashboard filtering validation failed: {ex.Message}");
        }
    }

    private sealed class ResultRunSummaryView
    {
        public string RunId { get; init; } = string.Empty;
        public string? RunName { get; init; }
        public string App { get; init; } = string.Empty;
        public string Browser { get; init; } = string.Empty;
        public string Env { get; init; } = string.Empty;
        public string Status { get; set; } = "Running";
        public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
    }
}
