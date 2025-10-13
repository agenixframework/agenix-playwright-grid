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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Interface for chunked logging with automatic flush capabilities.
///     Buffers log items and sends them in batches to reduce API calls.
/// </summary>
public interface IChunkedLogger : IAsyncDisposable
{
    /// <summary>
    ///     Adds a log item to the buffer.
    ///     If the buffer reaches the configured chunk size, automatically flushes to the API.
    /// </summary>
    /// <param name="log">The log item to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddLogAsync(CreateLogItemRequest log, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Flushes all buffered logs to the API.
    ///     This method is idempotent and safe to call multiple times.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
