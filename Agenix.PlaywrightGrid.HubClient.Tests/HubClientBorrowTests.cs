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

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.HubClient.Tests;

public class HubClientBorrowTests
{
    [Test]
    public async Task BorrowAsync_NoRunIdOrName_Sends_MinimalBody_And_Parses_WebSocketEndpoint()
    {
        var handler = new CapturingHandler(req =>
        {
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(req.RequestUri!.PathAndQuery, Is.EqualTo("/session/borrow"));
            var body = req.ReadJsonBody();
            Assert.That(body!["labelKey"]!.GetValue<string>(), Is.EqualTo("AppB:Chromium:UAT"));
            Assert.That(body.ContainsKey("runName"), Is.False);
            return JsonResponse(new
            {
                browserId = "b1",
                webSocketEndpoint = "ws://host:5200/ws/b1",
                labelKey = "AppB:Chromium:UAT",
                browserType = "chromium"
            });
        });

        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);
        var (browserId, ws, label, type) = await client.BorrowAsync("AppB:Chromium:UAT");

        Assert.That(browserId, Is.EqualTo("b1"));
        Assert.That(ws, Is.EqualTo("ws://host:5200/ws/b1"));
        Assert.That(label, Is.EqualTo("AppB:Chromium:UAT"));
        Assert.That(type, Is.EqualTo("chromium"));
    }

    [Test]
    public async Task BorrowAsync_RunId_In_Query_And_Header()
    {
        var handler = new CapturingHandler(req =>
        {
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(req.RequestUri!.PathAndQuery, Is.EqualTo("/session/borrow?runId=r-123"));
            Assert.That(req.Headers.Contains("Correlation-Id"), Is.True);
            Assert.That(req.Headers.GetValues("Correlation-Id"), Does.Contain("r-123"));
            var body = req.ReadJsonBody();
            Assert.That(body!["labelKey"]!.GetValue<string>(), Is.EqualTo("AppB:Chromium:UAT"));
            Assert.That(body.ContainsKey("runName"), Is.False);
            return JsonResponse(new { browserId = "b2", wsEndpoint = "ws://h/ws/b2" });
        });

        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);
        var (browserId, ws, _, _) = await client.BorrowAsync("AppB:Chromium:UAT", "r-123");
        Assert.That(browserId, Is.EqualTo("b2"));
        Assert.That(ws, Is.EqualTo("ws://h/ws/b2"));
    }

    [Test]
    public async Task BorrowAsync_RunName_Whitespace_Is_Omitted_From_Body()
    {
        var handler = new CapturingHandler(req =>
        {
            var body = req.ReadJsonBody();
            Assert.That(body!.ContainsKey("runName"), Is.False);
            return JsonResponse(new { browserId = "b3", webSocketEndpoint = "ws://h/ws/b3" });
        });

        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);
        var _ = await client.BorrowAsync("AppB:Chromium:UAT", "r-123", "   ");
    }

    [Test]
    public async Task BorrowAsync_RunName_Populated_Is_Included_As_Is()
    {
        var handler = new CapturingHandler(req =>
        {
            var body = req.ReadJsonBody();
            Assert.That(body!["runName"]!.GetValue<string>(), Is.EqualTo("  My Name  "));
            return JsonResponse(new { browserId = "b4", webSocketEndpoint = "ws://h/ws/b4" });
        });

        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);
        var _ = await client.BorrowAsync("AppB:Chromium:UAT", "r-123", "  My Name  ");
    }

    [Test]
    public async Task BorrowAsync_Overloads_Chain_To_Final_Method()
    {
        int calls = 0;
        var handler = new CapturingHandler(req =>
        {
            calls++;
            return JsonResponse(new { browserId = "b5", webSocketEndpoint = "ws://h/ws/b5" });
        });
        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);

        var t1 = await client.BorrowAsync("L1");
        var t2 = await client.BorrowAsync("L2", "r-1");
        var t3 = await client.BorrowAsync("L3", "r-2", CancellationToken.None);
        var t4 = await client.BorrowAsync("L4", "r-3", "Name", CancellationToken.None);

        Assert.That(calls, Is.EqualTo(4));
        Assert.That(t1.browserId, Is.EqualTo("b5"));
        Assert.That(t2.browserId, Is.EqualTo("b5"));
        Assert.That(t3.browserId, Is.EqualTo("b5"));
        Assert.That(t4.browserId, Is.EqualTo("b5"));
    }

    private static HttpResponseMessage JsonResponse(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public int Calls { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_handler(request));
        }
    }
}

internal static class HttpRequestMessageExtensions
{
    public static JsonObject? ReadJsonBody(this HttpRequestMessage req)
    {
        if (req.Content == null) return null;
        var str = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return string.IsNullOrWhiteSpace(str) ? null : JsonNode.Parse(str) as JsonObject;
    }
}
