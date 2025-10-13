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

using Agenix.PlaywrightGrid.Shared.Configuration;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <inheritdoc />
/// <summary>
///     Initialize an instance with incoming configuration.
/// </summary>
/// <param name="configuration">Configuration for considering to create an instance.</param>
public class RequestExecutionThrottleFactory(IConfiguration configuration) : IRequestExecutionThrottleFactory
{
    private const int MAX_CONCURRENT_REQUESTS = 10;

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public IRequestExecutionThrottler Create()
    {
        var maxConcurrentRequests = _configuration.GetValue("Server:MaximumConnectionsNumber", MAX_CONCURRENT_REQUESTS);

        return new RequestExecutionThrottler(maxConcurrentRequests);
    }
}
