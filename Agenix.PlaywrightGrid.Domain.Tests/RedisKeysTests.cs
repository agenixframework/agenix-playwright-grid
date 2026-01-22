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
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class RedisKeysTests
{
    [Test]
    public void Available_InUse_With_LabelKey_AreStable()
    {
        var key = "AppA:Chromium:UAT"; // Valid label
        Assert.That(RedisKeys.Available(key), Is.EqualTo("available:AppA:Chromium:UAT"));
        Assert.That(RedisKeys.InUse(key), Is.EqualTo("inuse:AppA:Chromium:UAT"));
    }

    [Test]
    public void Maintenance_Keys_Compose_Correctly()
    {
        var key = "AppB:Firefox:Prod";
        Assert.That(RedisKeys.MaintenanceFlag(key), Is.EqualTo("maintenance:AppB:Firefox:Prod"));
        Assert.That(RedisKeys.MaintenanceTarget(key), Is.EqualTo("maintenance:target:AppB:Firefox:Prod"));
        Assert.That(RedisKeys.MaintenanceSince(key), Is.EqualTo("maintenance:since:AppB:Firefox:Prod"));
        Assert.That(RedisKeys.MaintenanceSnapInuse(key), Is.EqualTo("maintenance:snap_inuse:AppB:Firefox:Prod"));
    }

    [Test]
    public void Node_And_Alive_Sanitize_Id()
    {
        var raw = "worker 1:region/eu"; // contains space, colon, slash
        var node = RedisKeys.Node(raw);
        var alive = RedisKeys.NodeAlive(raw);
        Assert.That(node, Does.StartWith("node:"));
        Assert.That(alive, Does.StartWith("node_alive:"));
        // Ensure forbidden chars are replaced with '-'
        Assert.That(node.Contains(' '), Is.False);
        Assert.That(node.Contains(':'), Is.True); // prefix only
        Assert.That(node.LastIndexOf(':'), Is.EqualTo(4)); // only the prefix colon
        Assert.That(node, Does.Not.Contain("/"));
        Assert.That(alive, Does.Not.Contain("/"));
    }

    [Test]
    public void BorrowTtl_Sanitizes_BrowserId()
    {
        var raw = "bid:abc xyz";
        var key = RedisKeys.BorrowTtl(raw);
        Assert.That(key, Does.StartWith("borrow_ttl:"));
        Assert.That(key, Does.Not.Contain(" "));
        // After prefix, no additional ':' should remain
        Assert.That(key.LastIndexOf(':'), Is.EqualTo("borrow_ttl".Length));
    }

    [Test]
    public void Results_Keys_AreStable()
    {
        var runId = "run_123";
        Assert.That(RedisKeys.ResultsRunsByStart(), Is.EqualTo("results:runs:byStart"));
        Assert.That(RedisKeys.ResultsRun(runId), Is.EqualTo("results:run:run_123"));
        Assert.That(RedisKeys.ResultsRunName(runId), Is.EqualTo("results:runname:run_123"));
        Assert.That(RedisKeys.ResultsTests(runId), Is.EqualTo("results:tests:run_123"));
        Assert.That(RedisKeys.ResultsCmd(runId), Is.EqualTo("results:cmd:run_123"));
        Assert.That(RedisKeys.ResultsCmdCount(runId), Is.EqualTo("results:cmdcount:run_123"));
    }

    [Test]
    public void Audit_Keys_AreStable()
    {
        Assert.That(RedisKeys.AuditEntries(), Is.EqualTo("audit:entries"));
        Assert.That(RedisKeys.AuditSecretsRunnerFingerprint(), Is.EqualTo("audit:secrets:runner:fp"));
        Assert.That(RedisKeys.AuditSecretsNodeFingerprint(), Is.EqualTo("audit:secrets:node:fp"));
    }

    [Test]
    public void Available_Throws_On_Invalid_Label()
    {
        Assert.Throws<ArgumentException>(() => RedisKeys.Available(":invalid"));
        Assert.Throws<ArgumentException>(() => RedisKeys.Available(""));
    }
}
