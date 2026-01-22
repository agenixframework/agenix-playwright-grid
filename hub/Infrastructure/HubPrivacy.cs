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

namespace PlaywrightHub.Infrastructure;

/// <summary>
///     Centralized helper for privacy-oriented logging behavior in the Hub.
///     Currently supports optional redaction of <c>RunName</c> in logs to prevent
///     accidental leakage of sensitive information.
/// </summary>
internal static class HubPrivacy
{
    private static volatile bool _redactRunName;

    /// <summary>
    ///     Initializes privacy settings from the application configuration.
    ///     Recognizes the following environment/configuration keys:
    ///     - <c>HUB_REDACT_RUNNAME</c>: boolean ("true"/"1") to enable redaction of RunName in logs.
    /// </summary>
    public static void Initialize(IConfiguration configuration)
    {
        var value = configuration["AGENIX_HUB_REDACT_RUNNAME"];
        _redactRunName = value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                               value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Returns a version of the provided <paramref name="runName" /> that is safe for logging
    ///     according to the configured privacy policy.
    /// </summary>
    /// <param name="runName">The optional run name supplied by the client.</param>
    /// <returns>The original <paramref name="runName" /> or "&lt;redacted&gt;" when redaction is enabled.</returns>
    public static string? RedactRunName(string? runName)
    {
        if (string.IsNullOrWhiteSpace(runName))
        {
            return runName; // preserve null/empty
        }

        return _redactRunName ? "<redacted>" : runName;
    }
}
