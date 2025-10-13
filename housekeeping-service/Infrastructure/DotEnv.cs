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

namespace IngestionService.Infrastructure;

/// <summary>
///     Loads environment variables from .env file for local development.
/// </summary>
public static class DotEnv
{
    public static void Load()
    {
        if (Environment.GetEnvironmentVariable("DISABLE_DOTENV") == "1")
        {
            return;
        }

        // Search for .env in current directory and parent directories (up to 3 levels)
        var currentDir = Directory.GetCurrentDirectory();
        var envFile = FindEnvFile(currentDir);

        if (envFile == null || !File.Exists(envFile))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(envFile))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                Environment.SetEnvironmentVariable(parts[0], parts[1]);
            }
        }
    }

    private static string? FindEnvFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (var i = 0; i < 4 && dir != null; i++) // Search up to 3 parent levels
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
            {
                return envPath;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
