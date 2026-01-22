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
using Agenix.PlaywrightGrid.Shared.Configuration;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <inheritdoc />
/// <summary>
///     Initializes a new instance of <see cref="RequestExecutorFactory" />
/// </summary>
/// <param name="configuration">
///     Configuration object for considering when structs new <see cref="IRequestExecutor" />
///     instance.
/// </param>
public class RequestExecutorFactory(IConfiguration configuration) : IRequestExecutorFactory
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public IRequestExecutor Create()
    {
        var throttler = new RequestExecutionThrottleFactory(_configuration).Create();

        var defaultStrategyValue = "exponential";

        var retryStrategy = _configuration.GetValue("Server:Retry:Strategy", defaultStrategyValue) ??
                            defaultStrategyValue;

        IRequestExecutor executor;
        switch (retryStrategy.ToLowerInvariant())
        {
            case "none":
                executor = new NoneRetryRequestExecutor(throttler);
                break;
            case "exponential":
                var maxExponentialAttempts = _configuration.GetValue("Server:Retry:MaxAttempts", 3U);
                var baseExponentialIndex = _configuration.GetValue("Server:Retry:BaseIndex", 2U);
                var httpStatusCodes = _configuration.GetValues<HttpStatusCode>("Server:Retry:HttpStatusCodes", null);
                executor = new ExponentialRetryRequestExecutor(maxExponentialAttempts, baseExponentialIndex, throttler,
                    httpStatusCodes.ToArray());
                break;
            case "linear":
                var maxLinearAttempts = _configuration.GetValue("Server:Retry:MaxAttempts", 3U);
                var linearDelay = _configuration.GetValue("Server:Retry:Delay", 5U * 1000);
                httpStatusCodes = _configuration.GetValues<HttpStatusCode>("Server:Retry:HttpStatusCodes", null);
                executor = new LinearRetryRequestExecutor(maxLinearAttempts, linearDelay, throttler,
                    httpStatusCodes.ToArray());
                break;
            default:
                throw new Exception($"Unknown '{retryStrategy}' retry strategy.");
        }

        return executor;
    }
}
