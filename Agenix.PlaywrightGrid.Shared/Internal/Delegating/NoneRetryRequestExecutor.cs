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

using System.Runtime.CompilerServices;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using Agenix.PlaywrightGrid.Shared.Reporter.Statistics;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <summary>
///     Invokes given func.
/// </summary>
/// <remarks>
///     Initializes new instance of <see cref="NoneRetryRequestExecutor" />.
/// </remarks>
/// <param name="throttler">Limits concurrent execution of requests.</param>
public class NoneRetryRequestExecutor(IRequestExecutionThrottler throttler) : BaseRequestExecutor
{
    private readonly IRequestExecutionThrottler _concurrentThrottler = throttler;

    private ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<NoneRetryRequestExecutor>();

    /// <inheritdoc />
    public override async Task<T> ExecuteAsync<T>(Func<Task<T>> func, Action<Exception> beforeNextAttempt = null,
        IStatisticsCounter statisticsCounter = null, [CallerMemberName] string logicalOperationName = null)
    {
        T result = default;

        try
        {
            if (_concurrentThrottler != null)
            {
                await _concurrentThrottler.ReserveAsync().ConfigureAwait(false);
            }

            TraceLogger.Verbose($"{logicalOperationName}");

            result = await base.ExecuteAsync(func, beforeNextAttempt, statisticsCounter, logicalOperationName)
                .ConfigureAwait(false);
        }
        catch (Exception exp)
        {
            TraceLogger.Error($"Unexpected exception: {exp}");
            throw;
        }
        finally
        {
            _concurrentThrottler?.Release();
        }

        return result;
    }
}
