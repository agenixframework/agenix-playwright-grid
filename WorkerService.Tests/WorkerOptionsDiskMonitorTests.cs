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

public class WorkerOptionsDiskMonitorTests
{
    [Test]
    public void Parses_Disk_Monitor_Envs_With_Clamping()
    {
        var prev = new Dictionary<string, string?>
        {
            ["DISK_MONITOR_ENABLED"] = Environment.GetEnvironmentVariable("DISK_MONITOR_ENABLED"),
            ["DISK_MONITOR_INTERVAL_SECONDS"] = Environment.GetEnvironmentVariable("DISK_MONITOR_INTERVAL_SECONDS"),
            ["DISK_USAGE_HIGH_PCT"] = Environment.GetEnvironmentVariable("DISK_USAGE_HIGH_PCT"),
            ["DISK_USAGE_CRITICAL_PCT"] = Environment.GetEnvironmentVariable("DISK_USAGE_CRITICAL_PCT"),
            ["INODE_USAGE_HIGH_PCT"] = Environment.GetEnvironmentVariable("INODE_USAGE_HIGH_PCT"),
            ["INODE_USAGE_CRITICAL_PCT"] = Environment.GetEnvironmentVariable("INODE_USAGE_CRITICAL_PCT"),
            ["CLEANUP_TARGET_DIRS"] = Environment.GetEnvironmentVariable("CLEANUP_TARGET_DIRS"),
            ["CLEANUP_MIN_FILE_AGE_MINUTES"] = Environment.GetEnvironmentVariable("CLEANUP_MIN_FILE_AGE_MINUTES"),
            ["CLEANUP_MAX_DELETE_MB_PER_SWEEP"] =
                Environment.GetEnvironmentVariable("CLEANUP_MAX_DELETE_MB_PER_SWEEP"),
            ["AGENIX_WORKER_PUBLIC_WS_HOST"] = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST"),
            ["AGENIX_WORKER_PUBLIC_WS_PORT"] = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT")
        };
        try
        {
            Environment.SetEnvironmentVariable("DISK_MONITOR_ENABLED", "1");
            Environment.SetEnvironmentVariable("DISK_MONITOR_INTERVAL_SECONDS", "5"); // will clamp to 10
            Environment.SetEnvironmentVariable("DISK_USAGE_HIGH_PCT", "101"); // clamp to 100
            Environment.SetEnvironmentVariable("DISK_USAGE_CRITICAL_PCT", "50"); // will bump to >= high
            Environment.SetEnvironmentVariable("INODE_USAGE_HIGH_PCT", "-1"); // clamp to 0
            Environment.SetEnvironmentVariable("INODE_USAGE_CRITICAL_PCT", "90");
            Environment.SetEnvironmentVariable("CLEANUP_TARGET_DIRS", "/tmp/a,/tmp/b");
            Environment.SetEnvironmentVariable("CLEANUP_MIN_FILE_AGE_MINUTES", "0"); // clamp to 1
            Environment.SetEnvironmentVariable("CLEANUP_MAX_DELETE_MB_PER_SWEEP", "999999"); // clamp to 102400
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            var opts = WorkerOptions.FromEnvironment();

            Assert.That(opts.DiskMonitorEnabled, Is.True);
            Assert.That(opts.DiskMonitorIntervalSeconds, Is.GreaterThanOrEqualTo(10));
            Assert.That(opts.DiskUsageHighWatermarkPercent, Is.EqualTo(100));
            Assert.That(opts.DiskUsageCriticalPercent, Is.GreaterThanOrEqualTo(opts.DiskUsageHighWatermarkPercent));
            Assert.That(opts.InodeUsageHighWatermarkPercent, Is.EqualTo(0));
            Assert.That(opts.InodeUsageCriticalPercent, Is.EqualTo(90));
            Assert.That(opts.CleanupTargetDirs, Does.Contain("/tmp/a"));
            Assert.That(opts.CleanupMinFileAgeMinutes, Is.EqualTo(1));
            Assert.That(opts.CleanupMaxDeleteMbPerSweep, Is.EqualTo(102400));
        }
        finally
        {
            foreach (var kv in prev)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }
}
