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

using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class DtoSerializationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        // Explicitly mirror Web defaults: camelCase + ignore null
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public void BorrowRequestDto_Serializes_With_RunName_When_Present()
    {
        var dto = new BorrowRequestDto { LabelKey = "AppB:Chromium:UAT", RunId = "rid-123", RunName = "My Run" };

        var json = JsonSerializer.Serialize(dto, WebJson);
        TestContext.WriteLine(json);
        // Assert camelCase property names and runName included
        Assert.That(json, Does.Contain("\"labelKey\":\"AppB:Chromium:UAT\""));
        Assert.That(json, Does.Contain("\"runId\":\"rid-123\""));
        Assert.That(json, Does.Contain("\"runName\":\"My Run\""));
    }

    [Test]
    public void BorrowRequestDto_Omits_RunName_When_Null()
    {
        var dto = new BorrowRequestDto { LabelKey = "AppB:Chromium:UAT", RunId = "rid-456", RunName = null };

        var json = JsonSerializer.Serialize(dto, WebJson);
        TestContext.WriteLine(json);
        Assert.That(json, Does.Contain("\"labelKey\":\"AppB:Chromium:UAT\""));
        Assert.That(json, Does.Contain("\"runId\":\"rid-456\""));
        Assert.That(json, Does.Not.Contain("runName"));
    }

    [Test]
    public void BorrowResponseDto_Serializes_RunName_When_Present_And_Omits_When_Null()
    {
        var withName = new BorrowResponseDto
        {
            BrowserId = "b1",
            WebSocketEndpoint = "ws://h/ws/b1",
            LabelKey = "AppB:Chromium:UAT",
            BrowserType = "chromium",
            RunName = "Some Display Name"
        };
        var withNameJson = JsonSerializer.Serialize(withName, WebJson);
        TestContext.WriteLine(withNameJson);
        Assert.That(withNameJson, Does.Contain("\"browserId\":\"b1\""));
        Assert.That(withNameJson, Does.Contain("\"webSocketEndpoint\":\"ws://h/ws/b1\""));
        Assert.That(withNameJson, Does.Contain("\"labelKey\":\"AppB:Chromium:UAT\""));
        Assert.That(withNameJson, Does.Contain("\"browserType\":\"chromium\""));
        Assert.That(withNameJson, Does.Contain("\"runName\":\"Some Display Name\""));

        var withoutName = new BorrowResponseDto
        {
            BrowserId = "b2",
            WebSocketEndpoint = "ws://h/ws/b2",
            LabelKey = "AppB:Chromium:UAT",
            BrowserType = null,
            RunName = null
        };
        var withoutNameJson = JsonSerializer.Serialize(withoutName, WebJson);
        TestContext.WriteLine(withoutNameJson);
        Assert.That(withoutNameJson, Does.Contain("\"browserId\":\"b2\""));
        Assert.That(withoutNameJson, Does.Contain("\"webSocketEndpoint\":\"ws://h/ws/b2\""));
        Assert.That(withoutNameJson, Does.Contain("\"labelKey\":\"AppB:Chromium:UAT\""));
        Assert.That(withoutNameJson, Does.Not.Contain("browserType"));
        Assert.That(withoutNameJson, Does.Not.Contain("runName"));
    }

    [Test]
    public void Run_And_RunSummary_Serialize_With_Optional_RunName()
    {
        var run = new Run { RunId = "r1", RunName = null };
        var runJson = JsonSerializer.Serialize(run, WebJson);
        TestContext.WriteLine(runJson);
        Assert.That(runJson, Does.Contain("\"runId\":\"r1\""));
        Assert.That(runJson, Does.Not.Contain("runName"));

        var summary = new RunSummary
        {
            RunId = "r2",
            RunName = "Display Name",
            App = "AppA",
            Browser = "Chromium",
            Env = "UAT",
            Status = "Running",
            TotalTests = 10,
            Passed = 5,
            Failed = 1
        };
        var summaryJson = JsonSerializer.Serialize(summary, WebJson);
        TestContext.WriteLine(summaryJson);
        Assert.That(summaryJson, Does.Contain("\"runId\":\"r2\""));
        Assert.That(summaryJson, Does.Contain("\"runName\":\"Display Name\""));
        Assert.That(summaryJson, Does.Contain("\"app\":\"AppA\""));
        Assert.That(summaryJson, Does.Contain("\"browser\":\"Chromium\""));
        Assert.That(summaryJson, Does.Contain("\"env\":\"UAT\""));
        Assert.That(summaryJson, Does.Contain("\"status\":\"Running\""));
    }
}
