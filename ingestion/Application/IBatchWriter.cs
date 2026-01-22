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

namespace IngestionService.Application;

/// <summary>
///     Interface for batch writers that flush accumulated events to storage.
/// </summary>
public interface IBatchWriter<in T>
{
    /// <summary>
    ///     Add an event to the batch. Automatically flushes when batch is full or timeout reached.
    /// </summary>
    Task AddAsync(T item, CancellationToken ct = default);

    /// <summary>
    ///     Manually flush all pending events to storage.
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}
