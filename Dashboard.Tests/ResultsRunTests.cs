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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Dashboard.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using TestContext = Bunit.TestContext;

namespace Dashboard.Tests;

public class ResultsRunTests
{
    private TestContext _ctx;

    [SetUp]
    public void Setup()
    {
        _ctx = new TestContext();
        // Strict mode ensures JS calls must be pre-registered
        _ctx.JSInterop.Mode = JSRuntimeMode.Strict;
        _ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ctx.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(new NotFoundHandler()));
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void Host_Includes_AppJs_And_AppJs_Exposes_CopyText()
    {
        var root = LocateRepoRoot();
        var hostPath = Path.Combine(root, "dashboard", "Pages", "_Host.cshtml");
        var jsPath = Path.Combine(root, "dashboard", "wwwroot", "js", "app.js");
        Assert.That(File.Exists(hostPath), Is.True, "_Host.cshtml not found");
        Assert.That(File.Exists(jsPath), Is.True, "app.js not found");

        var host = File.ReadAllText(hostPath);
        StringAssert.Contains("js/app.js", host);

        var js = File.ReadAllText(jsPath);
        StringAssert.Contains("window.copyText", js);
    }

    [Test]
    public void ResultsRun_Filtering_By_Kind_Works_Via_OnKindInput()
    {
        // Arrange
        var runId = "abcd1234";
        var cut = _ctx.RenderComponent<ResultsRun>(ps => ps.Add(p => p.runId, runId));

        // Wait until simulated commands are rendered
        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".list-group-item");
            Assert.That(items.Count, Is.GreaterThan(1));
        }, TimeSpan.FromSeconds(5));

        var initialCount = cut.FindAll(".list-group-item").Count;

        // Act: invoke OnKindInput via reflection to simulate oninput
        var mi = typeof(ResultsRun).GetMethod("OnKindInput",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(mi, Is.Not.Null, "OnKindInput method not found");
        cut.InvokeAsync(() => mi!.Invoke(cut.Instance, new object[] { new ChangeEventArgs { Value = "Borrow" } }));

        // Assert: list is filtered down (simulated data has exactly one Borrow)
        cut.WaitForAssertion(() =>
        {
            var filteredItems = cut.FindAll(".list-group-item");
            Assert.That(filteredItems.Count, Is.LessThan(initialCount));
            Assert.That(filteredItems.Count, Is.EqualTo(1));
            StringAssert.Contains("Borrowed Chromium endpoint", filteredItems[0].TextContent);
        }, TimeSpan.FromSeconds(5));
    }


    [Test]
    [Ignore("It's hanging")]
    public async Task ResultsRun_Copy_Invokes_JSInterop_And_Shows_Alert()
    {
        // Arrange
        var runId = "abcd1234";
        // Setup expected JS calls
        var copySetup = _ctx.JSInterop.SetupVoid("copyText", args => true);
        var alertSetup = _ctx.JSInterop.SetupVoid("alert", args => true);

        var cut = _ctx.RenderComponent<ResultsRun>(ps => ps.Add(p => p.runId, runId));

        // Narrow list to a single known item to make click deterministic
        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".list-group-item");
            Assert.That(items.Count, Is.GreaterThan(0), "Expected at least one list item");
        }, TimeSpan.FromSeconds(5));

        // Narrow using OnKindInput (explicit method now available)
        var mi = typeof(ResultsRun).GetMethod("OnKindInput",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(mi, Is.Not.Null, "OnKindInput method not found");
        await cut.InvokeAsync(() => mi!.Invoke(cut.Instance, [new ChangeEventArgs { Value = "Borrow" }]));

        // Wait for filter to apply
        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".list-group-item");
            Assert.That(items.Count, Is.GreaterThan(0), "Expected at least one item after filtering");
        }, TimeSpan.FromSeconds(5));

        // Get filtered commands via reflection
        var prop = typeof(ResultsRun).GetProperty("FilteredCommands",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(prop, Is.Not.Null, "FilteredCommands property not found");
        var list = (List<CommandLogEventDto>)prop!.GetValue(cut.Instance)!;
        Assert.That(list, Is.Not.Null, "FilteredCommands returned null");
        Assert.That(list.Count, Is.GreaterThan(0), "Expected at least one filtered command");

        // Use the first command event for copy test
        var ev = list[0];
        var miCopy = typeof(ResultsRun).GetMethod("Copy",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(miCopy, Is.Not.Null, "Copy method not found");

        // Invoke Copy method
        await cut.InvokeAsync(() => (Task)miCopy!.Invoke(cut.Instance, new object[] { ev })!);

        // Assert: JS was invoked for copy and alert
        cut.WaitForAssertion(() =>
        {
            Assert.That(copySetup.Invocations.Count, Is.EqualTo(1), "copyText should be called once");
            Assert.That(alertSetup.Invocations.Count, Is.EqualTo(1), "alert should be called once");

            // Validate the text passed to copyText contains expected bits
            var invocation = copySetup.Invocations.Single();
            var textArg = invocation.Arguments[0]?.ToString() ?? string.Empty;
            Assert.That(textArg, Does.Contain("["), "Text should contain square bracket");
        }, TimeSpan.FromSeconds(5));
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        var di = new DirectoryInfo(dir);
        while (di != null && !File.Exists(Path.Combine(di.FullName, "PlaywrightGrid.sln")))
        {
            di = di.Parent;
        }

        if (di == null)
        {
            Assert.Fail("Could not locate repository root (PlaywrightGrid.sln)");
        }

        return di.FullName;
    }

    private class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Return 404 for any request to force the component to use simulated data
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }
}
