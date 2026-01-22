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

using System;
using System.Threading.Tasks;

namespace Dashboard.Services;

/// <summary>
/// Service interface for preventing duplicate concurrent HTTP requests in the dashboard.
/// Ensures that multiple simultaneous calls for the same resource share the same in-flight request.
/// </summary>
public interface IRequestDeduplicationService
{
    /// <summary>
    /// Executes an asynchronous operation with deduplication.
    /// If an operation with the same key is already in-flight, returns the existing task.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="key">A unique key identifying the request, typically in the format {httpMethod}:{endpoint}.</param>
    /// <param name="operation">The asynchronous operation to execute if no in-flight request exists.</param>
    /// <returns>A task representing the asynchronous operation, returning the result of either the new or existing in-flight request.</returns>
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> operation);

    /// <summary>
    /// Explicitly removes a request from the deduplication store.
    /// </summary>
    /// <param name="key">The unique key of the request to clear.</param>
    void Clear(string key);
}
