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

using NUnit.Framework;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class CleanupPlannerTests
{
    [Test]
    public void Selects_Oldest_First_Up_To_MaxBytes()
    {
        var tmp = Directory.CreateTempSubdirectory("cleanup-planner-test");
        try
        {
            var now = DateTime.UtcNow;
            // Create files with different ages and sizes
            var f1 = Path.Combine(tmp.FullName, "a.txt");
            var f2 = Path.Combine(tmp.FullName, "b.txt");
            var f3 = Path.Combine(tmp.FullName, "c.txt");
            File.WriteAllBytes(f1, new byte[100]);
            File.WriteAllBytes(f2, new byte[200]);
            File.WriteAllBytes(f3, new byte[300]);
            File.SetLastWriteTimeUtc(f1, now.AddHours(-5)); // oldest
            File.SetLastWriteTimeUtc(f2, now.AddHours(-4));
            File.SetLastWriteTimeUtc(f3, now.AddMinutes(-10)); // too new if minAge 30m

            var dirs = new[] { tmp.FullName };
            var minAge = TimeSpan.FromMinutes(30);
            var maxBytes = 250; // should pick only f1 (100) and f2 is 200 but would exceed 250 -> stop after f1
            var candidates = CleanupPlanner.PlanDeletions(dirs, minAge, maxBytes, now).ToList();

            Assert.That(candidates.Count, Is.EqualTo(1));
            Assert.That(candidates[0].FullName, Is.EqualTo(f1));
        }
        finally
        {
            try { tmp.Delete(true); } catch { /* ignore */ }
        }
    }
}
