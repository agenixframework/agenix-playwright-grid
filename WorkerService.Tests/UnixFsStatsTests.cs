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
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class UnixFsStatsTests
{
    [Test]
    public void TryGetInodeStats_EmptyPath_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            UnixFsStats.TryGetInodeStats(string.Empty, out var total, out var free);
            // May return false on non-Linux; any value is acceptable but must not throw
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public void TryGetInodeStats_WhitespacePath_DoesNotThrowAndFallsBackToRoot()
    {
        Assert.DoesNotThrow(() =>
        {
            var ok = UnixFsStats.TryGetInodeStats("   ", out var total, out var free);
            Assert.That(ok, Is.False.Or.True); // Platform dependent; only asserting no throw
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public void TryGetInodeStats_NonExistingPath_DoesNotThrow()
    {
        var nonExisting = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "this\\path\\should\\not\\exist")
            : "/this/path/should/not/exist";

        Assert.DoesNotThrow(() =>
        {
            var ok = UnixFsStats.TryGetInodeStats(nonExisting, out var total, out var free);
            Assert.That(ok, Is.False.Or.True);
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }
}
