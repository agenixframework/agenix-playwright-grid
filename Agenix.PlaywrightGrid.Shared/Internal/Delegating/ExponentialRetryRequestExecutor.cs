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
///     Invokes given func with retry strategy and exponential delay between attempts.
/// </summary>
public class ExponentialRetryRequestExecutor : BaseRequestExecutor
{
    private readonly IRequestExecutionThrottler _concurrentThrottler;

    /// <summary>
    ///     Initializes new instance of <see cref="ExponentialRetryRequestExecutor" />.
    /// </summary>
    /// <param name="maxRetryAttempts">Maximum number of attempts.</param>
    /// <param name="baseIndex">Exponential base index for delay.</param>
    public ExponentialRetryRequestExecutor(uint maxRetryAttempts, uint baseIndex) :
        this(maxRetryAttempts, baseIndex, null, null)
    {
    }

    /// <summary>
    ///     Initializes new instance of <see cref="ExponentialRetryRequestExecutor" />.
    /// </summary>
    /// <param name="maxRetryAttempts">Maximum number of attempts.</param>
    /// <param name="baseIndex">Exponential base index for delay.</param>
    /// <param name="throttler">Limits concurrent execution of requests.</param>
    /// <param name="httpStatusCodes">Http status codes to be retried.</param>
    public ExponentialRetryRequestExecutor(uint maxRetryAttempts, uint baseIndex, IRequestExecutionThrottler throttler,
        HttpStatusCode[] httpStatusCodes)
    {
        if (maxRetryAttempts < 1)
        {
            throw new ArgumentException("Maximum attempts cannot be less than 1.", nameof(maxRetryAttempts));
        }

        _concurrentThrottler = throttler;
        MaxRetryAttempts = maxRetryAttempts;
        BaseIndex = baseIndex;
        HttpStatusCodes = httpStatusCodes;
    }

    private ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<ExponentialRetryRequestExecutor>();

    /// <summary>
    ///     Maximum number of attempts
    /// </summary>
    public uint MaxRetryAttempts { get; }

    /// <summary>
    ///     Exponential base index for delay
    /// </summary>
    public uint BaseIndex { get; }

    /// <summary>
    ///     Http status codes to be retried.
    /// </summary>
    public HttpStatusCode[] HttpStatusCodes { get; }

    /// <inheritdoc />
    public override async Task<T> ExecuteAsync<T>(Func<Task<T>> func, Action<Exception> beforeNextAttempt = null,
        IStatisticsCounter statisticsCounter = null, [CallerMemberName] string logicalOperationName = null)
    {
        T? result = default;
        List<Exception> exceptions = [];

        for (var i = 0; i < MaxRetryAttempts; i++)
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
                var delay = (int)Math.Pow(BaseIndex, i + MaxRetryAttempts);

                if (i < MaxRetryAttempts - 1)
                {
                    TraceLogger.Error(
                        $"Error while invoking '{logicalOperationName}' operation. Current attempt: {i}. Waiting {delay} seconds and retrying it.\n{exp}");
                    exceptions.Add(new HttpRequestException(
                        $"'{logicalOperationName}' threw an exception. Next attempt in {delay} second(s).", exp));

                    await Task.Delay(delay * 1000).ConfigureAwait(false);

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
