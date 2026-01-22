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

using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Hosting;

namespace HousekeepingService.Shared;

/// <summary>
///     Base class for all housekeeping workers providing common chunked logging functionality.
/// </summary>
public abstract class HousekeepingWorkerBase : BackgroundService
{
    protected readonly ChunkedLogger ChunkedLogger;
    protected readonly string WorkerName;

    protected HousekeepingWorkerBase(ChunkedLogger chunkedLogger, string workerName)
    {
        ChunkedLogger = chunkedLogger;
        WorkerName = workerName;
    }

    /// <summary>
    ///     Creates a standard operation scope for worker executions with common inputs.
    /// </summary>
    protected IChunkedOperation BeginWorkerOperation(string operationName, Dictionary<string, object>? inputs = null)
    {
        var allInputs = new Dictionary<string, object>
        {
            ["workerName"] = WorkerName,
            ["operation"] = operationName
        };

        if (inputs != null)
        {
            foreach (var kvp in inputs)
            {
                allInputs[kvp.Key] = kvp.Value;
            }
        }

        return ChunkedLogger.BeginOperation($"{WorkerName}.{operationName}", allInputs);
    }

    /// <summary>
    ///     Logs a milestone event for this worker with the worker name prepended.
    /// </summary>
    protected void LogWorkerMilestone(string eventCode, string messageTemplate, params object[] args)
    {
        ChunkedLogger.LogMilestone(eventCode, $"[{WorkerName}] {messageTemplate}", args);
    }
}
