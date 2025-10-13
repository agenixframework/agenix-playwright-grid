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

using System.Text;
using System.Text.Json;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IngestionService.Infrastructure;

/// <summary>
///     RabbitMQ consumer with connection management, retry logic, and dead letter queue support.
/// </summary>
public sealed class RabbitMqConsumer : IRabbitMqConsumer
{
    private readonly IConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private IConnection? _connection;

    public RabbitMqConsumer(IConfiguration config, ILogger<RabbitMqConsumer> logger)
    {
        _config = config;
        _logger = logger;

        var maxRetries = config.GetValue("AGENIX_INGESTION_MAX_RETRY_ATTEMPTS", 3);
        var retryDelayMs = config.GetValue("AGENIX_INGESTION_RETRY_DELAY_MS", 1000);

        // Configure JSON deserialization to match publisher's camelCase naming policy
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                maxRetries,
                attempt => TimeSpan.FromMilliseconds(retryDelayMs * Math.Pow(2, attempt - 1)),
                (ex, delay, attempt, ctx) =>
                {
                    _logger.LogWarning(ex, "Retry attempt {Attempt} after {Delay}ms", attempt, delay.TotalMilliseconds);
                });
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }

    public IConnection GetConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        var url = _config["RABBITMQ_URL"] ?? "amqp://localhost:5672";
        var factory = new ConnectionFactory
        {
            Uri = new Uri(url),
            UserName = _config["RABBITMQ_USERNAME"] ?? "guest",
            Password = _config["RABBITMQ_PASSWORD"] ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection("ingestion-service");
        _logger.LogInformation("Connected to RabbitMQ: {Url}", url);

        return _connection;
    }

    public IModel CreateChannel(string queueName, int prefetchCount = 100)
    {
        var conn = GetConnection();
        var channel = conn.CreateModel();

        // Declare queue with DLQ
        var args = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" }, { "x-dead-letter-routing-key", "agenix-test-platform.dlq" }
        };

        channel.QueueDeclare(queueName, true, false, false, args);
        channel.QueueDeclare("agenix-test-platform.dlq", true, false, false);

        channel.BasicQos(0, (ushort)prefetchCount, false);

        _logger.LogInformation("Created channel for queue {Queue} with prefetch {Prefetch}", queueName, prefetchCount);
        return channel;
    }

    public async Task ConsumeAsync<T>(
        string queueName,
        Func<T, CancellationToken, Task> handler,
        CancellationToken ct) where T : class
    {
        var channel = CreateChannel(queueName, _config.GetValue("RABBITMQ_PREFETCH_COUNT", 100));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (sender, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<T>(json, _jsonOptions);

                if (evt == null)
                {
                    _logger.LogWarning("Failed to deserialize message from {Queue}", queueName);
                    channel.BasicNack(ea.DeliveryTag, false, false); // Send to DLQ
                    return;
                }

                await _retryPolicy.ExecuteAsync(async () => await handler(evt, ct));

                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message from {Queue} after retries", queueName);
                channel.BasicNack(ea.DeliveryTag, false, false); // Send to DLQ
            }
        };

        channel.BasicConsume(queueName, false, consumer);

        _logger.LogInformation("Started consuming from {Queue}", queueName);

        // Keep consuming until cancellation
        await Task.Delay(Timeout.Infinite, ct);
    }
}
