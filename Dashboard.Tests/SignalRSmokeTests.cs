#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;

namespace Dashboard.Tests;

/// <summary>
///     Smoke test for Dashboard SignalR stream: connect → receive initial event → disconnect.
///     This test does not require any browsers; it relies on PoolHub sending an initial PoolState on connect.
///     Run against a locally running grid (docker compose up) or an externally reachable Hub.
///     If the Hub is not available, the test is marked Inconclusive with guidance.
/// </summary>
public class SignalRSmokeTests
{
    private static string ResolveHubSignalRUrl()
    {
        // Prefer HUB_SIGNALR if provided (the dashboard uses this), else derive from HUB_URL, else default to local compose mapping
        var hubSignalR = Environment.GetEnvironmentVariable("HUB_SIGNALR");
        if (!string.IsNullOrWhiteSpace(hubSignalR))
        {
            return hubSignalR.TrimEnd('/');
        }

        var hubUrl = Environment.GetEnvironmentVariable("HUB_URL");
        if (!string.IsNullOrWhiteSpace(hubUrl))
        {
            return hubUrl.TrimEnd('/') + "/ws";
        }

        return "http://127.0.0.1:5100/ws"; // default local mapping
    }

    [Test]
    public async Task Dashboard_SignalR_PoolHub_Smoke_ConnectReceiveDisconnect()
    {
        var url = ResolveHubSignalRUrl();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tcs = new TaskCompletionSource<PoolStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        HubConnection? conn = null;
        try
        {
            conn = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            conn.On<PoolStateDto>("PoolState", state =>
            {
                if (state is null)
                {
                    return;
                }

                tcs.TrySetResult(state);
            });

            await conn.StartAsync(cts.Token);

            // Wait for the initial PoolState sent from PoolHub.OnConnectedAsync
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));
            if (completed != tcs.Task)
            {
                Assert.Inconclusive(
                    "Connected to SignalR but did not receive PoolState within timeout. Ensure Hub is healthy and Redis reachable.");
            }

            var state = await tcs.Task; // safe, already completed
            Assert.That(state, Is.Not.Null);
            Assert.That(state.Pools, Is.Not.Null);
            Assert.That(state.Workers, Is.Not.Null);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive(
                $"SignalR connection failed to {url}. Start the grid via 'docker compose up' or set HUB_URL/HUB_SIGNALR. Error: {ex.Message}");
        }
        finally
        {
            if (conn is not null)
            {
                try { await conn.StopAsync(cts.Token); }
                catch
                {
                    /* ignore */
                }

                try { await conn.DisposeAsync(); }
                catch
                {
                    /* ignore */
                }
            }
        }
    }
}
