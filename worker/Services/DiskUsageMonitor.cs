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

using Prometheus;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

/// <summary>
///     Periodically measures disk/inode usage and performs conservative cleanup of old browser caches/traces
///     when high watermarks are breached. Emits Prometheus metrics and structured logs with node id context.
/// </summary>
public sealed class DiskUsageMonitor
{
    private static readonly Gauge DiskBytesTotal = Metrics.CreateGauge("worker_disk_bytes_total", "Total bytes on target filesystem", "node");
    private static readonly Gauge DiskBytesFree = Metrics.CreateGauge("worker_disk_bytes_free", "Free bytes on target filesystem", "node");
    private static readonly Gauge DiskBytesUsed = Metrics.CreateGauge("worker_disk_bytes_used", "Used bytes on target filesystem", "node");
    private static readonly Gauge DiskUsageRatio = Metrics.CreateGauge("worker_disk_usage_ratio", "Disk usage ratio (0..1) on target filesystem", "node");

    private static readonly Gauge InodesTotal = Metrics.CreateGauge("worker_inodes_total", "Total inodes on target filesystem (Linux)", "node");
    private static readonly Gauge InodesFree = Metrics.CreateGauge("worker_inodes_free", "Free inodes on target filesystem (Linux)", "node");
    private static readonly Gauge InodesUsed = Metrics.CreateGauge("worker_inodes_used", "Used inodes on target filesystem (Linux)", "node");
    private static readonly Gauge InodesUsageRatio = Metrics.CreateGauge("worker_inodes_usage_ratio", "Inode usage ratio (0..1) on target filesystem (Linux)", "node");

    private static readonly Counter CleanupDeletedFiles = Metrics.CreateCounter("worker_cleanup_deleted_files_total", "Number of files deleted by cleanup sweeps", "node", "reason");
    private static readonly Counter CleanupDeletedBytes = Metrics.CreateCounter("worker_cleanup_deleted_bytes_total", "Total bytes deleted by cleanup sweeps", "node", "reason");

    private readonly ILogger _log;
    private readonly WorkerOptions _options;

    public DiskUsageMonitor(WorkerOptions options)
    {
        _options = options;
        _log = LoggerFactory.Create(b => b.AddSimpleConsole()).CreateLogger("diskmon");
    }

    public Task RunAsync(CancellationToken ct)
    {
        if (!_options.DiskMonitorEnabled)
        {
            _log.LogInformation("[diskmon] Disk monitor disabled via DISK_MONITOR_ENABLED=0");
            return Task.CompletedTask;
        }
        return Task.Run(() => LoopAsync(ct), ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var node = _options.NodeId;
        var interval = TimeSpan.FromSeconds(Math.Max(10, _options.DiskMonitorIntervalSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Choose a representative path for FS stats
                var targetDirs = ParseTargetDirs(_options.CleanupTargetDirs);
                var probePath = targetDirs.FirstOrDefault() ?? Path.GetTempPath();
                if (string.IsNullOrWhiteSpace(probePath)) probePath = "/";

                // Disk bytes via DriveInfo (cross-platform)
                var (total, free) = GetDiskBytes(probePath);
                var used = total - free;
                double ratio = total > 0 ? Math.Clamp(used / total, 0, 1) : 0d;
                DiskBytesTotal.WithLabels(node).Set(total);
                DiskBytesFree.WithLabels(node).Set(free);
                DiskBytesUsed.WithLabels(node).Set(used);
                DiskUsageRatio.WithLabels(node).Set(ratio);

                // Inodes via statvfs on Linux
                if (UnixFsStats.TryGetInodeStats(probePath, out var inTotal, out var inFree))
                {
                    var inUsed = inTotal >= inFree ? inTotal - inFree : 0UL;
                    double inRatio = inTotal > 0 ? Math.Clamp((double)inUsed / inTotal, 0, 1) : 0d;
                    InodesTotal.WithLabels(node).Set(inTotal);
                    InodesFree.WithLabels(node).Set(inFree);
                    InodesUsed.WithLabels(node).Set(inUsed);
                    InodesUsageRatio.WithLabels(node).Set(inRatio);
                }

                var pct = (int)Math.Round(ratio * 100);
                if (pct >= _options.DiskUsageCriticalPercent)
                {
                    _log.LogError("[diskmon] CRITICAL disk usage {Pct}% on {Probe}. node={Node}", pct, probePath, node);
                }
                else if (pct >= _options.DiskUsageHighWatermarkPercent)
                {
                    _log.LogWarning("[diskmon] HIGH disk usage {Pct}% on {Probe}. node={Node}", pct, probePath, node);
                }

                // Cleanup if above high watermark
                if (pct >= _options.DiskUsageHighWatermarkPercent)
                {
                    await CleanupAsync(targetDirs, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[diskmon] Loop error: {Message}", ex.Message);
            }

            try { await Task.Delay(interval, ct); } catch { /* ignored */ }
        }
    }

    private async Task CleanupAsync(List<string> targetDirs, CancellationToken ct)
    {
        var node = _options.NodeId;
        if (targetDirs.Count == 0)
        {
            // Default safe temp subdir
            var d = Path.Combine(Path.GetTempPath(), "pw-traces");
            targetDirs = [d];
        }

        var minAge = TimeSpan.FromMinutes(Math.Max(1, _options.CleanupMinFileAgeMinutes));
        var capBytes = _options.CleanupMaxDeleteMbPerSweep * 1024L * 1024L;
        var deletedFiles = 0;
        long deletedBytes = 0;

        foreach (var fi in CleanupPlanner.PlanDeletions(targetDirs, minAge, capBytes, DateTime.UtcNow))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var len = fi.Length;
                fi.Delete();
                deletedFiles++;
                deletedBytes += len;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "[diskmon] Failed to delete {File}: {Message}", fi.FullName, ex.Message);
            }
        }

        if (deletedFiles > 0)
        {
            _log.LogInformation("[diskmon] Cleanup deleted {Files} files, {Bytes} bytes. node={Node}", deletedFiles, deletedBytes, node);
            CleanupDeletedFiles.WithLabels(node, "pressure").Inc(deletedFiles);
            CleanupDeletedBytes.WithLabels(node, "pressure").Inc(deletedBytes);
        }

        await Task.CompletedTask;
    }

    private static (double total, double free) GetDiskBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? Path.DirectorySeparatorChar.ToString();
            var di = DriveInfo.GetDrives().FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase))
                     ?? DriveInfo.GetDrives().FirstOrDefault();
            return di == null ? (0, 0) : (di.TotalSize, di.AvailableFreeSpace);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static List<string> ParseTargetDirs(string? s)
    {
        return (s ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
