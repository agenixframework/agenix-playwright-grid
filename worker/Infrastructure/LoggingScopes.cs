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

internal static class LoggingScopes
{
    public static IDisposable Begin(ILogger logger, string? runId = null, string? browserId = null, string? runName = null)
    {
        var dict = new Dictionary<string, object?>(3);
        if (!string.IsNullOrWhiteSpace(runId)) dict["runId"] = runId;
        if (!string.IsNullOrWhiteSpace(browserId)) dict["browserId"] = browserId;
        if (!string.IsNullOrWhiteSpace(runName)) dict["runName"] = runName;
        if (dict.Count == 0)
        {
            return NullScope.Instance;
        }
        var scope = logger.BeginScope(dict);
        return scope ?? NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
