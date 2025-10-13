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
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Application.Ports;
using RabbitMQ.Client;

namespace PlaywrightHub.Infrastructure.Adapters.Messaging;

/// <summary>
///     RabbitMQ event publisher with durable queues and DLQ support.
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly IModel _channel;
    private readonly IConnection _connection;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly ChunkedLogger _chunkedLogger;

    public RabbitMqEventPublisher(string connectionString, ILogger<RabbitMqEventPublisher> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        _chunkedLogger = new ChunkedLogger(logger, nameof(RabbitMqEventPublisher));

        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection("agenix-test-platform-hub");
        _channel = _connection.CreateModel();

        DeclareInfrastructure();

        _chunkedLogger.LogMilestone(EventCodes.EventPublisher.ChannelCreated,
            "RabbitMQ publisher connected: {Uri} connectionName=agenix-test-platform-hub",
            HideSensitiveUri(connectionString));
    }

    public void Dispose()
    {
        _channel.Close();
        _channel.Dispose();
        _connection.Close();
        _connection.Dispose();

        _chunkedLogger.LogMilestone(EventCodes.EventPublisher.ChannelClosed,
            "connectionClosed=true");
    }

    private static string HideSensitiveUri(string uriString)
    {
        try
        {
            var uri = new Uri(uriString);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}/***";
        }
        catch
        {
            return "***";
        }
    }

    public Task PublishTestItemEventAsync(TestItemEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        // NEW: Log BEFORE spawning background thread
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.TestItemPublished, // EVT01
            "itemId={ItemId} launchId={LaunchId} eventType={EventType}",
            evt.ItemId, evt.LaunchId, evt.EventType);

        // Fire-and-forget with logging wrapper
        _ = Task.Run(async () =>
        {
            try
            {
                await PublishEventAsync("agenix-test-platform.test-items", evt, evt.CorrelationId, parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} itemId={ItemId} correlationId={CorrelationId}",
                    ex.Message, evt.ItemId, evt.CorrelationId);

                _logger.LogWarning(ex, "Failed to publish test item event {CorrelationId}",
                    evt.CorrelationId);
            }
        }, ct);

        return Task.CompletedTask;
    }

    public Task PublishTestItemEventAsync(TestItemAutoStoppedEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.TestItemPublished, // EVT01
            "itemId={ItemId} launchId={LaunchId} reason={Reason}",
            evt.ItemId, evt.LaunchId, evt.AutoStopReason);

        var correlationId = Guid.NewGuid().ToString();

        _ = Task.Run(async () =>
        {
            try
            {
                await PublishEventAsync("agenix-test-platform.test-items", evt, correlationId, parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} itemId={ItemId} correlationId={CorrelationId}",
                    ex.Message, evt.ItemId, correlationId);

                _logger.LogWarning(ex, "Failed to publish test item auto-stop event {CorrelationId}",
                    correlationId);
            }
        }, ct);

        return Task.CompletedTask;
    }

    public Task PublishCommandEventAsync(CommandEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.CommandPublished, // EVT02
            "eventType={EventType} runId={RunId}",
            evt.EventType, evt.RunId);

        _ = Task.Run(async () =>
        {
            try
            {
                await PublishEventAsync("agenix-test-platform.commands", evt, evt.CorrelationId, parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} runId={RunId}",
                    ex.Message, evt.RunId);

                _logger.LogWarning(ex, "Failed to publish command event");
            }
        }, ct);

        return Task.CompletedTask;
    }

    public Task PublishLogItemEventAsync(LogItemEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.LogItemPublished, // EVT03
            "launchId={LaunchId} level={Level} metadataSize={MetadataSize}",
            evt.LaunchId, evt.Level, evt.MetadataJson?.Length ?? 0);

        _ = Task.Run(async () =>
        {
            try
            {
                await PublishEventAsync("agenix-test-platform.log-items", evt, evt.CorrelationId, parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} correlationId={CorrelationId}",
                    ex.Message, evt.CorrelationId);

                _logger.LogWarning(ex, "Failed to publish log item event");
            }
        }, ct);

        return Task.CompletedTask;
    }

    public Task PublishAuditEventAsync(AuditEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.AuditPublished, // EVT04
            "action={Action} actor={Actor}",
            evt.Action, evt.Actor);

        _ = Task.Run(async () =>
        {
            try
            {
                await PublishEventAsync("agenix-test-platform.audit", evt, evt.CorrelationId, parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} action={Action}",
                    ex.Message, evt.Action);

                _logger.LogWarning(ex, "Failed to publish audit event");
            }
        }, ct);

        return Task.CompletedTask;
    }

    public Task PublishArtifactUploadEventAsync(ArtifactUploadEvent evt, Guid? parentOperationId = null, CancellationToken ct = default)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.EventPublisher.ArtifactPublished, // EVT05
            "artifactId={ArtifactId} fileName={FileName} fileSize={FileSize}",
            evt.ArtifactId, evt.FileName, evt.FileSize);

        _ = Task.Run(async () =>
        {
            try
            {
                // Note: ArtifactUploadEvent doesn't have CorrelationId, using ArtifactId
                await PublishEventAsync("agenix-test-platform.artifacts", evt, evt.ArtifactId.ToString(), parentOperationId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.EventPublisher.PublishFailed, // EVT10
                    ex,
                    "error={Error} artifactId={ArtifactId}",
                    ex.Message, evt.ArtifactId);

                _logger.LogWarning(ex, "Failed to publish artifact event");
            }
        }, ct);

        return Task.CompletedTask;
    }

    private void DeclareInfrastructure()
    {
        try
        {
            var dlqArgs = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" }, { "x-dead-letter-routing-key", "agenix-test-platform.dlq" }
            };

            _channel.ExchangeDeclare("agenix-test-platform", ExchangeType.Topic, durable: true);
            _chunkedLogger.LogMilestone(EventCodes.EventPublisher.ExchangeDeclared,
                "exchange=agenix-test-platform type=topic durable=true");

            _channel.QueueDeclare("agenix-test-platform.test-items", true, false, false, dlqArgs);
            _chunkedLogger.LogMilestone(EventCodes.EventPublisher.QueueDeclared,
                "queue=agenix-test-platform.test-items");

            _channel.QueueDeclare("agenix-test-platform.commands", true, false, false, dlqArgs);
            _chunkedLogger.LogMilestone(EventCodes.EventPublisher.AdditionalQueueDeclared,
                "queue=agenix-test-platform.commands");

            _channel.QueueDeclare("agenix-test-platform.log-items", true, false, false, dlqArgs);
            _channel.QueueDeclare("agenix-test-platform.audit", true, false, false, dlqArgs);
            _channel.QueueDeclare("agenix-test-platform.artifacts", true, false, false, dlqArgs);
            _channel.QueueDeclare("agenix-test-platform.dlq", true, false, false);
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogMilestone(EventCodes.EventPublisher.PublishFailed,
                ex,
                "error={Error} operation=DeclareInfrastructure",
                ex.Message);

            throw new InvalidOperationException("Failed to declare RabbitMQ infrastructure", ex);
        }
    }

    private async Task PublishEventAsync(string queueName, object evt, string correlationId, Guid? parentOperationId = null)
    {
        using var operation = _chunkedLogger.BeginOperation("RabbitMqPublish:Event",
            new Dictionary<string, object>
            {
                ["queueName"] = queueName,
                ["correlationId"] = correlationId,
                ["eventType"] = evt.GetType().Name
            },
            parentOperationId);

        try
        {
            // Serialize event
            var json = JsonSerializer.Serialize(evt, _jsonOptions);
            var body = Encoding.UTF8.GetBytes(json);
            _chunkedLogger.LogDebug(EventCodes.EventPublisher.MessageSizeLogged, "messageSize={Size} bytes", json.Length);

            // Publish to RabbitMQ using the default exchange and queue name as a routing key
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.CorrelationId = correlationId;
            properties.ContentType = "application/json";

            _channel.BasicPublish(
                exchange: string.Empty,
                routingKey: queueName,
                mandatory: false,
                basicProperties: properties,
                body: body);

            operation.Complete();

            _chunkedLogger.LogMilestone(
                EventCodes.EventPublisher.PublishConfirmed,
                "queue={Queue} correlationId={CorrelationId}",
                queueName, correlationId);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            operation.Fail(ex, ErrorType.DependencyFailure, DependencyName.RabbitMQ);

            _logger.LogError(ex, "RabbitMQ publish failed for {CorrelationId}", correlationId);

            _chunkedLogger.LogMilestone(
                EventCodes.EventPublisher.ConnectionLost,
                ex,
                "error={Error} queue={Queue}",
                ex.Message, queueName);

            throw;
        }
    }
}
