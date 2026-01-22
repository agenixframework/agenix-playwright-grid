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

using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class RunNameRulesTests
{
    [Test]
    public void Null_Or_Whitespace_Is_Treated_As_Absent()
    {
        Assert.That(RunNameRules.TryNormalize(null, out var n1, out var e1), Is.True);
        Assert.That(n1, Is.Null);
        Assert.That(e1, Is.Null);

        Assert.That(RunNameRules.TryNormalize("   ", out var n2, out var e2), Is.True);
        Assert.That(n2, Is.Null);
        Assert.That(e2, Is.Null);
    }

    [Test]
    public void Trims_And_Preserves_Case()
    {
        Assert.That(RunNameRules.TryNormalize("  My Run.Name-01  ", out var n, out var e), Is.True);
        Assert.That(e, Is.Null);
        Assert.That(n, Is.EqualTo("My Run.Name-01"));
    }

    [Test]
    public void Rejects_Control_And_Disallowed_Chars()
    {
        Assert.That(RunNameRules.TryNormalize("Bad@Name", out _, out var e1), Is.False);
        Assert.That(e1, Does.Contain("invalid characters"));

        Assert.That(RunNameRules.TryNormalize("Bad#Name", out _, out var e2), Is.False);
        Assert.That(e2, Does.Contain("invalid characters"));

        // control char (tab) should fail
        Assert.That(RunNameRules.TryNormalize("Bad\tName", out _, out var e3), Is.False);
        Assert.That(e3, Does.Contain("control characters"));
    }

    [Test]
    public void Accepts_Max_Length_And_Rejects_Too_Long()
    {
        var ok = new string('A', RunNameRules.MaxLength);
        Assert.That(RunNameRules.TryNormalize(ok, out var n1, out var e1), Is.True);
        Assert.That(n1, Is.EqualTo(ok));
        Assert.That(e1, Is.Null);

        var tooLong = new string('B', RunNameRules.MaxLength + 1);
        Assert.That(RunNameRules.TryNormalize(tooLong, out _, out var e2), Is.False);
        Assert.That(e2, Does.Contain("maximum length"));
    }
}
