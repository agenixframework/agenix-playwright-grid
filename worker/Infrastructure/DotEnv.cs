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
using System.IO;

namespace WorkerService.Infrastructure;

/// <summary>
///     Minimal .env loader for local development convenience.
///     Searches for a ".env" file from the current directory upward and loads key=value pairs
///     into process environment variables. Existing variables are not overridden by default.
/// </summary>
internal static class DotEnv
{
    /// <summary>
    ///     Loads environment variables from a .env file if present. No-ops if disabled via DISABLE_DOTENV=1.
    /// </summary>
    /// <param name="path">Optional path to a .env file; if null, will search upwards from CWD.</param>
    /// <param name="overrideExisting">When true, overrides variables already set in the environment.</param>
    public static void Load(string? path = null, bool overrideExisting = false)
    {
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("DISABLE_DOTENV"), "1", StringComparison.Ordinal))
                return;

            var filePath = path ?? FindEnvFile();
            if (filePath == null || !File.Exists(filePath)) return;

            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();

                // Strip optional surrounding quotes for common cases
                if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);

                if (key.Length == 0) continue;

                if (!overrideExisting && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    continue;

                Environment.SetEnvironmentVariable(key, value);
            }
        }
        catch
        {
            // Intentionally swallow errors for dev convenience; .env is optional.
        }
    }

    private static string? FindEnvFile()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }
        return null;
    }
}
