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

using RabbitMQ.Client;

namespace IngestionService.Infrastructure;

/// <summary>
///     Interface for RabbitMQ consumer with connection management, retry logic, and dead letter queue support.
/// </summary>
public interface IRabbitMqConsumer : IDisposable
{
    IConnection GetConnection();
    IModel CreateChannel(string queueName, int prefetchCount = 100);
    Task ConsumeAsync<T>(string queueName, Func<T, CancellationToken, Task> handler, CancellationToken ct) where T : class;
}
