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

namespace Dashboard;

/// <summary>
///     Feature flags for Dashboard runtime. Meant to allow safe rollout of UI features.
///     Values are read from environment variables or configuration keys at startup.
///     Env keys: DASHBOARD_FEATURE_FILTERS, DASHBOARD_FEATURE_VIRTUALIZATION, DASHBOARD_FEATURE_LIVE_FEED
/// </summary>
public sealed class DashboardFeatureFlags
{
    public bool FiltersEnabled { get; init; } = true;
    public bool VirtualizationEnabled { get; init; } = true;
    public bool LiveFeedEnabled { get; init; } = true;

    public static DashboardFeatureFlags FromConfiguration(IConfiguration cfg)
    {
        return new DashboardFeatureFlags
        {
            FiltersEnabled = GetBool(cfg, "DASHBOARD_FEATURE_FILTERS", true),
            VirtualizationEnabled = GetBool(cfg, "DASHBOARD_FEATURE_VIRTUALIZATION", true),
            LiveFeedEnabled = GetBool(cfg, "DASHBOARD_FEATURE_LIVE_FEED", true)
        };
    }

    private static bool GetBool(IConfiguration cfg, string key, bool defaultValue)
    {
        var v = cfg[key];
        if (string.IsNullOrWhiteSpace(v))
        {
            return defaultValue;
        }

        return v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
