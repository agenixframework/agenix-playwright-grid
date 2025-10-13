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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Resources;

/// <summary>
///     Async interface for test item operations.
/// </summary>
public interface IAsyncTestItemResource
{
    /// <summary>
    ///     Finishes a test item by its UUID.
    /// </summary>
    Task<MessageResponse> FinishAsync(string uuid, FinishTestItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts a new test item.
    /// </summary>
    Task<TestItemCreatedResponse> StartAsync(StartTestItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts a child test item under the specified parent UUID.
    /// </summary>
    Task<TestItemCreatedResponse> StartAsync(string uuid, StartTestItemRequest model,
        CancellationToken cancellationToken = default);
}
