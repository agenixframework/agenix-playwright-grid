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

using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;

namespace WorkerService.Tools;

internal static class PoolConfigValidator
{
    private sealed record PoolSummary(
        int EffectiveTotalCapacity,
        IDictionary<string, int> ByBrowser,
        IDictionary<string, int> Pools,
        string Region,
        string Os,
        string Source);

    public static int Run(string[] args)
    {
        // Args: validate-pool-config [--pool "..."] [--json]
        var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        var poolArgIndex = Array.FindIndex(args, a => string.Equals(a, "--pool", StringComparison.OrdinalIgnoreCase));
        string? poolArg = null;
        if (poolArgIndex >= 0 && poolArgIndex + 1 < args.Length)
        {
            poolArg = args[poolArgIndex + 1];
        }

        var source = "ENV:POOL_CONFIG";
        var poolConfig = poolArg ?? Environment.GetEnvironmentVariable("POOL_CONFIG") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(poolConfig))
        {
            // Fall back to WorkerOptions default for predictability
            poolConfig = "AppA:Chromium:Staging=3";
            source = "(default)";
        }

        var region = Environment.GetEnvironmentVariable("NODE_REGION") ?? "local";
        var os = Environment.GetEnvironmentVariable("NODE_OS") ?? "linux"; // WorkerOptions.DetectContainerOs() not public; best-effort

        var errors = new List<string>();
        var warnings = new List<string>();
        var pools = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenOriginal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in poolConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !int.TryParse(kv[1], out var count))
            {
                errors.Add($"Malformed entry '{part}'. Expected format Label=Count.");
                continue;
            }
            var rawKey = kv[0];
            if (!seenOriginal.Add(rawKey))
            {
                // Duplicate in source string; will be collapsed after normalization
            }

            LabelKey? lk;
            string key;
            if (!LabelKey.TryParse(rawKey, out lk))
            {
                // Back-compat: accept raw keys but record a warning
                key = rawKey;
                warnings.Add($"Non-canonical label key '{rawKey}' accepted as-is (does not follow App:Browser:Env schema with Browser second).");
            }
            else
            {
                key = lk!.Normalized;
            }

            if (!pools.TryAdd(key, count))
            {
                pools[key] += count; // collapse duplicates after normalization
            }
        }

        if (pools.Count == 0)
        {
            errors.Add("No valid pools found after parsing POOL_CONFIG.");
        }

        // Compute per-browser totals
        var byBrowser = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, cnt) in pools)
        {
            if (LabelKey.TryParse(key, out var lk, new LabelKeyParsingOptions { EnforceBrowserSecond = false }))
            {
                var browser = string.IsNullOrWhiteSpace(lk!.Browser) ? "Chromium" : lk.Browser;
                byBrowser[browser] = (byBrowser.TryGetValue(browser, out var v) ? v : 0) + Math.Max(0, cnt);
            }
            else
            {
                // Fallback: best effort by splitting
                var parts = key.Split(':', StringSplitOptions.TrimEntries);
                var browser = parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "Chromium";
                byBrowser[browser] = (byBrowser.TryGetValue(browser, out var v) ? v : 0) + Math.Max(0, cnt);
            }
        }

        var total = pools.Values.Sum(v => Math.Max(0, v));
        var summary = new PoolSummary(total, byBrowser, pools, region, os, source);

        if (json)
        {
            var jsonText = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(jsonText);
        }
        else
        {
            Console.WriteLine("POOL_CONFIG validation summary");
            Console.WriteLine($"  Source: {source}");
            Console.WriteLine($"  Region: {region}, OS: {os}");
            Console.WriteLine("  Pools:");
            foreach (var kv in pools.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {kv.Key} = {kv.Value}");
            }
            Console.WriteLine("  ByBrowser:");
            foreach (var kv in byBrowser.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {kv.Key} = {kv.Value}");
            }
            Console.WriteLine($"  EffectiveTotalCapacity: {total}");
        }

        if (errors.Count > 0)
        {
            if (!json)
            {
                Console.Error.WriteLine("Validation errors:");
                foreach (var e in errors)
                {
                    Console.Error.WriteLine("  - " + e);
                }
            }
            else
            {
                var obj = new { Errors = errors };
                Console.WriteLine(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            return 1;
        }

        return 0;
    }
}
