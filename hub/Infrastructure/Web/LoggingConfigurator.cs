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

using System.Diagnostics.CodeAnalysis;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Applies environment-driven logging configuration for Hub.
///     Supported keys:
///     - LOG_LEVEL: Trace|Debug|Information|Warning|Error|Critical|None
///     - LOG_LEVEL_OVERRIDES: comma/semicolon/newline separated list of Category=Level pairs
///     Also respects standard .NET: Logging__LogLevel__Default and Logging__LogLevel__{Category} if present.
/// </summary>
internal static class LoggingConfigurator
{
    public static void ApplyFromEnvironment(ILoggingBuilder logging, IConfiguration config)
    {
        // Default level
        var defaultLevelStr = config["LOG_LEVEL"] ?? config["LOGLEVEL"] ??
            config["Logging:LogLevel:Default"] ?? config["Logging__LogLevel__Default"];
        if (TryParseLevel(defaultLevelStr, out var defaultLevel))
        {
            logging.SetMinimumLevel(defaultLevel);
        }

        // Overrides via LOG_LEVEL_OVERRIDES
        var overrides = config["LOG_LEVEL_OVERRIDES"];
        if (!string.IsNullOrWhiteSpace(overrides))
        {
            foreach (var raw in overrides.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = raw.Trim();
                var eqIdx = entry.IndexOf('=');
                if (eqIdx <= 0 || eqIdx == entry.Length - 1)
                {
                    continue;
                }

                var category = entry[..eqIdx].Trim();
                var levelStr = entry[(eqIdx + 1)..].Trim();
                if (category.Length == 0)
                {
                    continue;
                }

                if (!TryParseLevel(levelStr, out var level))
                {
                    continue;
                }

                logging.AddFilter(category, level);
            }
        }

        // Overrides via standard Logging__LogLevel__{Category}
        foreach (var kvp in config.AsEnumerable())
        {
            if (kvp.Key.StartsWith("Logging:LogLevel:", StringComparison.OrdinalIgnoreCase))
            {
                var cat = kvp.Key["Logging:LogLevel:".Length..];
                if (cat.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseLevel(kvp.Value, out var lvl))
                {
                    logging.AddFilter(cat, lvl);
                }
            }
            else if (kvp.Key.StartsWith("Logging__LogLevel__", StringComparison.OrdinalIgnoreCase))
            {
                var cat = kvp.Key["Logging__LogLevel__".Length..];
                if (cat.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseLevel(kvp.Value, out var lvl))
                {
                    logging.AddFilter(cat, lvl);
                }
            }
        }
    }

    public static bool TryParseLevel(string? value, [NotNullWhen(true)] out LogLevel level)
    {
        level = LogLevel.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (int.TryParse(value, out var num) && Enum.IsDefined(typeof(LogLevel), num))
        {
            level = (LogLevel)num;
            return true;
        }

        return Enum.TryParse(value, true, out level);
    }
}
