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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.HubClient.Tests;

public class HubClientLogsTests
{
    [Test]
    public async Task SendApiLogsAsync_Null_Or_Empty_Texts_No_Request_Sent()
    {
        int calls = 0;
        var handler = new CapturingHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);

        await client.SendApiLogsAsync("b-1", null);
        await client.SendApiLogsAsync("b-1", new string?[] { null, " ", "", "\t" });

        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public async Task SendApiLogsAsync_Filters_Whitespace_And_Posts_Grouped_Items()
    {
        string? path = null;
        JsonArray? body = null;
        var handler = new CapturingHandler(req =>
        {
            path = req.RequestUri!.PathAndQuery;
            var str = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            body = JsonNode.Parse(str) as JsonArray;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new Agenix.PlaywrightGrid.HubClient.HubClient("http://localhost:5100", "runner-secret", handler);

        await client.SendApiLogsAsync("b-1", new[] { "a", " ", "b", "", "c" });

        Assert.That(path, Is.EqualTo("/results/browser/b-1/api-logs"));
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Count, Is.EqualTo(3));
        Assert.That(body![0]!["text"]!.GetValue<string>(), Is.EqualTo("a"));
        Assert.That(body![1]!["text"]!.GetValue<string>(), Is.EqualTo("b"));
        Assert.That(body![2]!["text"]!.GetValue<string>(), Is.EqualTo("c"));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
