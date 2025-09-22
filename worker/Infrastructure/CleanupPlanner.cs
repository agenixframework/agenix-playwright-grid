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

namespace WorkerService.Infrastructure;

/// <summary>
///     Determines which files to delete during cleanup sweeps under disk/inode pressure.
///     The strategy is conservative: only files older than a minimum age are eligible, and deletions are
///     capped per sweep. Selection is by ascending LastWriteTimeUtc (oldest first).
/// </summary>
public static class CleanupPlanner
{
    public static IEnumerable<FileInfo> PlanDeletions(IEnumerable<string> targetDirs, TimeSpan minAge, long maxDeleteBytes, DateTime utcNow)
    {
        if (maxDeleteBytes <= 0) yield break;

        var minTime = utcNow - minAge;
        var files = new List<FileInfo>();
        foreach (var dir in targetDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists) continue;
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (fi.LastWriteTimeUtc <= minTime)
                        {
                            files.Add(fi);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        long acc = 0;
        foreach (var fi in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            var len = SafeLength(fi);
            if (len <= 0) continue;
            if (acc + len > maxDeleteBytes) yield break;
            acc += len;
            yield return fi;
        }
    }

    private static long SafeLength(FileInfo fi)
    {
        try
        {
            return fi.Length;
        }
        catch
        {
            return 0;
        }
    }
}
