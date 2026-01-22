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

using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using NUnit.Framework;

namespace IngestionService.Tests.Infrastructure;

[TestFixture]
public class PostgresBatchWriterTests
{
    private IConfiguration _config;
    private NpgsqlDataSource _dataSource;
    private Mock<ILogger<PostgresBatchWriter>> _loggerMock;
    private ChunkedLogger<PostgresBatchWriter> _chunkedLogger;
    private Mock<ILogTokenCache> _logTokenCacheMock;
    private Mock<ICommandTokenCache> _commandTokenCacheMock;

    [SetUp]
    public void SetUp()
    {
        var myConfiguration = new Dictionary<string, string>
        {
            {"AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED", "true"},
            {"AGENIX_INGESTION_COMMAND_TOKEN_OPTIMIZATION_ENABLED", "true"}
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration!)
            .Build();

        _dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test");
        _loggerMock = new Mock<ILogger<PostgresBatchWriter>>();
        _chunkedLogger = new ChunkedLogger<PostgresBatchWriter>(_loggerMock.Object, new ChunkedLoggerOptions());

        _logTokenCacheMock = new Mock<ILogTokenCache>();
        _commandTokenCacheMock = new Mock<ICommandTokenCache>();
    }

    [Test]
    public async Task WriteLogItemsAsync_UsesTokenOptimization_WhenEnabled()
    {
        // Arrange
        var writer = new PostgresBatchWriter(
            _config,
            _dataSource,
            _loggerMock.Object,
            _chunkedLogger,
            _logTokenCacheMock.Object,
            _commandTokenCacheMock.Object);

        var events = new List<LogItemEvent>
        {
            new LogItemEvent { Message = "Test log", Level = "INFO" }
        };

        // We expect it to call GetOrCreateTokenAsync
        _logTokenCacheMock.Setup(x => x.GetOrCreateTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("some-hash");

        // It will then try to open a connection. We can stop there or mock the connection.
        // If we don't mock the connection, it might throw when trying to call OpenConnectionAsync.
        // But we just want to verify the token cache call.

        // Act & Assert (it will probably fail at connection opening, which is fine if we verify the mock before)
        try
        {
            await writer.WriteLogItemsAsync(events, CancellationToken.None);
        }
        catch (Exception)
        {
            // Ignore connection errors
        }

        // Assert
        _logTokenCacheMock.Verify(x => x.GetOrCreateTokenAsync("Test log", "INFO", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task WriteCommandsAsync_SkipsEvents_WithInvalidJson()
    {
        // Arrange
        var writer = new PostgresBatchWriter(
            _config,
            _dataSource,
            _loggerMock.Object,
            _chunkedLogger,
            _logTokenCacheMock.Object,
            _commandTokenCacheMock.Object);

        var events = new List<CommandEvent>
        {
            new CommandEvent { DataJson = "invalid-json" },
            new CommandEvent { DataJson = "{\"message\": \"valid\"}" }
        };

        _commandTokenCacheMock.Setup(x => x.GetOrCreateTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("some-hash");

        // Act
        try
        {
            await writer.WriteCommandsAsync(events, CancellationToken.None);
        }
        catch (Exception)
        {
            // Ignore connection errors
        }

        // Assert
        // Should only call token cache for the valid one
        _commandTokenCacheMock.Verify(x => x.GetOrCreateTokenAsync("valid", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _commandTokenCacheMock.Verify(x => x.GetOrCreateTokenAsync("", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
