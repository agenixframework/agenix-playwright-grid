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

using System.Net;
using System.Runtime.CompilerServices;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using Agenix.PlaywrightGrid.Shared.Reporter.Statistics;
using ReportPortal.Client;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <summary>
///     Invokes given func with retry strategy and linear delay between attempts.
/// </summary>
public class LinearRetryRequestExecutor : BaseRequestExecutor
{
    private readonly IRequestExecutionThrottler _concurrentThrottler;

    /// <summary>
    ///     Initializes new instance of <see cref="LinearRetryRequestExecutor" />.
    /// </summary>
    /// <param name="maxRetryAttempts">Maximum number of attempts.</param>
    /// <param name="delay">Delay between attempts (in milliseconds).</param>
    public LinearRetryRequestExecutor(uint maxRetryAttempts, uint delay) :
        this(maxRetryAttempts, delay, null, null)
    {
    }

    /// <summary>
    ///     Initializes new instance of <see cref="LinearRetryRequestExecutor" />.
    /// </summary>
    /// <param name="maxRetryAttempts">Maximum number of attempts.</param>
    /// <param name="delay">Delay between attempts (in milliseconds).</param>
    /// <param name="throttler">Limits concurrent execution of requests.</param>
    /// <param name="httpStatusCodes">Http status codes to be retried.</param>
    public LinearRetryRequestExecutor(uint maxRetryAttempts, uint delay, IRequestExecutionThrottler throttler,
        HttpStatusCode[] httpStatusCodes)
    {
        if (maxRetryAttempts < 1)
        {
            throw new ArgumentException("Maximum attempts cannot be less than 1.", nameof(maxRetryAttempts));
        }

        _concurrentThrottler = throttler;
        MaxRetryAttemps = maxRetryAttempts;
        Delay = delay;
        HttpStatusCodes = httpStatusCodes;
    }

    private ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<LinearRetryRequestExecutor>();

    /// <summary>
    ///     Maximum number of attempts.
    /// </summary>
    public uint MaxRetryAttemps { get; }

    /// <summary>
    ///     How many milliseconds to wait between attempts.
    /// </summary>
    public uint Delay { get; }

    /// <summary>
    ///     Http status codes to be retried.
    /// </summary>
    public HttpStatusCode[] HttpStatusCodes { get; }

    /// <inheritdoc />
    public override async Task<T> ExecuteAsync<T>(Func<Task<T>> func, Action<Exception> beforeNextAttempt = null,
        IStatisticsCounter statisticsCounter = null, [CallerMemberName] string logicalOperationName = null)
    {
        T result = default;
        List<Exception> exceptions = [];

        for (var i = 0; i < MaxRetryAttemps; i++)
        {
            try
            {
                if (_concurrentThrottler != null)
                {
                    await _concurrentThrottler.ReserveAsync().ConfigureAwait(false);
                }

                TraceLogger.Verbose($"{logicalOperationName} Current attempt: {i}");
                result = await base.ExecuteAsync(func, beforeNextAttempt, statisticsCounter, logicalOperationName)
                    .ConfigureAwait(false);
                break;
            }
            catch (Exception exp) when (exp is TaskCanceledException ||
                                        exp is HttpRequestException ||
                                        Array.IndexOf(HttpStatusCodes, (exp as ServiceException)?.HttpStatusCode) > -1)
            {
                if (i < MaxRetryAttemps - 1)
                {
                    TraceLogger.Error(
                        $"Error while invoking '{logicalOperationName}' operation. Current attempt: {i}. Waiting {Delay} milliseconds and retrying it.\n{exp}");
                    exceptions.Add(new HttpRequestException(
                        $"'{logicalOperationName}' threw an exception. Next attempt in {Delay / 1000} second(s).",
                        exp));

                    await Task.Delay((int)Delay).ConfigureAwait(false);

                    beforeNextAttempt?.Invoke(exp);
                }
                else
                {
                    TraceLogger.Error(
                        $"Error while invoking '{logicalOperationName}' operation. Current attempt: {i}.\n{exp}");
                    exceptions.Add(new HttpRequestException(
                        $"'{logicalOperationName}' threw an exception. Limit of retries has been reached.", exp));

                    throw new RetryExecutionException(logicalOperationName, exceptions);
                }
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
        }

        return result;
    }
}
