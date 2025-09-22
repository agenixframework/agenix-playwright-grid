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

using System.Runtime.InteropServices;

namespace WorkerService.Infrastructure;

/// <summary>
///     Cross-platform helpers to read filesystem statistics. On Linux, uses statvfs to obtain inode counts.
///     On non-Unix platforms, inode stats are not available and the method returns false.
/// </summary>
internal static class UnixFsStats
{
    // ReSharper disable NotAccessedField.Local
    // ReSharper disable InconsistentNaming
    [StructLayout(LayoutKind.Sequential)]
    private struct Statvfs
    {
        public ulong f_bsize;   // file system block size
        public ulong f_frsize;  // fragment size
        public ulong f_blocks;  // size of fs in f_frsize units
        public ulong f_bfree;   // # free blocks
        public ulong f_bavail;  // # free blocks for unprivileged users
        public ulong f_files;   // # inodes
        public ulong f_ffree;   // # free inodes
        public ulong f_favail;  // # free inodes for unprivileged users
        public ulong f_fsid;    // file system ID
        public ulong f_flag;    // mount flags
        public ulong f_namemax; // maximum filename length
    }
    // ReSharper restore InconsistentNaming
    // ReSharper restore NotAccessedField.Local

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int statvfs(string path, out Statvfs buf);

    /// <summary>
    ///     Attempts to read inode stats for the filesystem containing the given path (Linux only).
    ///     Returns false on non-Linux platforms or on any error. Never throws.
    /// </summary>
    public static bool TryGetInodeStats(string? path, out ulong total, out ulong free)
    {
        total = 0;
        free = 0;

        try
        {
            if (!OperatingSystem.IsLinux()) return false;

            // Normalize early and guarantee non-null
            string full;
            try
            {
                var candidate = string.IsNullOrWhiteSpace(path) ? "/" : path;
                full = Path.GetFullPath(candidate);
            }
            catch
            {
                full = "/";
            }

            // Walk up to find the nearest existing file / dir, default to "/"
            var probe = full;
            while (!string.IsNullOrEmpty(probe))
            {
                if (Directory.Exists(probe) || File.Exists(probe))
                    break;

                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, probe, StringComparison.Ordinal))
                {
                    probe = "/";
                    break;
                }
                probe = parent;
            }

            // Ensure non-null/non-empty for P/Invoke
            var effectivePath = string.IsNullOrEmpty(probe) ? "/" : probe;

            var rc = statvfs(effectivePath, out var st);
            if (rc != 0) return false;

            total = st.f_files;
            free = st.f_ffree;
            return total > 0;
        }
        catch
        {
            total = 0;
            free = 0;
            return false;
        }
    }
}
