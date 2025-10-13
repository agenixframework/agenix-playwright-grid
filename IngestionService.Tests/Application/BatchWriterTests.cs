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

using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Application;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace IngestionService.Tests.Application;

[TestFixture]
public class BatchWriterTests
{
    private Mock<ILogger> _loggerMock;
    private List<string> _flushedItems;
    private Func<List<string>, CancellationToken, Task> _writeFunc;
    private int _flushCount;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger>();
        _flushedItems = new List<string>();
        _flushCount = 0;
        _writeFunc = (items, ct) =>
        {
            _flushedItems.AddRange(items);
            _flushCount++;
            return Task.CompletedTask;
        };
    }

    [Test]
    public async Task AddAsync_Flushes_WhenSizeThresholdReached()
    {
        // Arrange
        var maxBatchSize = 3;
        var maxBatchAge = TimeSpan.FromHours(1);
        using var writer = new BatchWriter<string>(_writeFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);

        // Act
        await writer.AddAsync("item1");
        await writer.AddAsync("item2");

        // Assert
        Assert.That(_flushCount, Is.EqualTo(0));
        Assert.That(_flushedItems, Is.Empty);

        // Act
        await writer.AddAsync("item3");

        // Assert
        Assert.That(_flushCount, Is.EqualTo(1));
        Assert.That(_flushedItems.Count, Is.EqualTo(3));
        Assert.That(_flushedItems, Contains.Item("item1"));
        Assert.That(_flushedItems, Contains.Item("item2"));
        Assert.That(_flushedItems, Contains.Item("item3"));
    }

    [Test]
    public async Task CheckAndFlushAsync_Flushes_WhenTimeoutReached()
    {
        // Arrange
        var maxBatchSize = 10;
        var maxBatchAge = TimeSpan.FromMilliseconds(200);
        using var writer = new BatchWriter<string>(_writeFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);

        // Act
        await writer.AddAsync("item1");

        // Wait for timer to trigger (checks every 100ms)
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.That(_flushCount, Is.AtLeast(1));
        Assert.That(_flushedItems.Count, Is.EqualTo(1));
        Assert.That(_flushedItems[0], Is.EqualTo("item1"));
    }

    [Test]
    public async Task FlushAsync_Flushes_Manually()
    {
        // Arrange
        var maxBatchSize = 10;
        var maxBatchAge = TimeSpan.FromHours(1);
        using var writer = new BatchWriter<string>(_writeFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);

        // Act
        await writer.AddAsync("item1");
        await writer.FlushAsync();

        // Assert
        Assert.That(_flushCount, Is.EqualTo(1));
        Assert.That(_flushedItems.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task AddAsync_IsThreadSafe()
    {
        // Arrange
        var maxBatchSize = 100;
        var maxBatchAge = TimeSpan.FromHours(1);
        using var writer = new BatchWriter<string>(_writeFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);
        var itemCount = 1000;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < itemCount; i++)
        {
            var item = $"item{i}";
            tasks.Add(Task.Run(() => writer.AddAsync(item)));
        }

        await Task.WhenAll(tasks);
        await writer.FlushAsync();

        // Assert
        Assert.That(_flushedItems.Count, Is.EqualTo(itemCount));
        var distinctItems = _flushedItems.Distinct().Count();
        Assert.That(distinctItems, Is.EqualTo(itemCount));
    }

    [Test]
    public void AddAsync_AfterDispose_DoesNothing()
    {
        // Arrange
        var maxBatchSize = 10;
        var maxBatchAge = TimeSpan.FromHours(1);
        var writer = new BatchWriter<string>(_writeFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);
        writer.Dispose();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await writer.AddAsync("item1"));
        Assert.That(_flushedItems, Is.Empty);
    }

    [Test]
    public async Task FlushInternalAsync_HandlesException_AndThrows()
    {
        // Arrange
        var expectedException = new Exception("Flush failed");
        Func<List<string>, CancellationToken, Task> failingWriteFunc = (items, ct) => throw expectedException;

        var maxBatchSize = 1;
        var maxBatchAge = TimeSpan.FromHours(1);
        using var writer = new BatchWriter<string>(failingWriteFunc, maxBatchSize, maxBatchAge, _loggerMock.Object);

        // Act & Assert
        var ex = Assert.ThrowsAsync<Exception>(async () => await writer.AddAsync("item1"));
        Assert.That(ex, Is.SameAs(expectedException));

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to flush batch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
