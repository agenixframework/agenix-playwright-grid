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

using IngestionService.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using NUnit.Framework;
using StackExchange.Redis;

namespace IngestionService.Tests.Infrastructure;

[TestFixture]
public class RedisLogTokenCacheTests
{
    private Mock<IDatabase> _redisMock;
    private Mock<NpgsqlDataSource> _dataSourceMock;
    private Mock<ILogger<RedisLogTokenCache>> _loggerMock;
    private TimeSpan _ttl = TimeSpan.FromMinutes(10);

    [SetUp]
    public void SetUp()
    {
        _redisMock = new Mock<IDatabase>();
        _dataSourceMock = new Mock<NpgsqlDataSource>();
        _loggerMock = new Mock<ILogger<RedisLogTokenCache>>();
    }


    [Test]
    public async Task GetOrCreateTokenAsync_ReturnsFromInMemoryCache_IfEnabled()
    {
        // Arrange
        // We need a way to avoid hitting the DB on the first call, or mock it.
        // For now, let's mock Redis to return a value so it doesn't hit DB.
        var cache = new RedisLogTokenCache(_redisMock.Object, null!, _ttl, true, 1000, _loggerMock.Object);
        var message = "Test message";
        var level = "INFO";

        _redisMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), CommandFlags.None))
            .ReturnsAsync((RedisValue)message);

        // First call to populate in-memory cache from Redis
        await cache.GetOrCreateTokenAsync(message, level);

        // Reset mocks to track only second call
        _redisMock.Invocations.Clear();

        // Act
        var hash = await cache.GetOrCreateTokenAsync(message, level);

        // Assert
        Assert.That(hash, Is.Not.Null);
        // Should NOT call Redis StringGetAsync because it's in memory
        _redisMock.Verify(x => x.StringGetAsync(It.IsAny<RedisKey>(), CommandFlags.None), Times.Never);
    }

    [Test]
    public async Task GetOrCreateTokenAsync_ReturnsFromRedis_IfInRedis()
    {
        // Arrange
        var cache = new RedisLogTokenCache(_redisMock.Object, null!, _ttl, false, 1000, _loggerMock.Object);
        var message = "Test message";
        var level = "INFO";

        _redisMock.Setup(x => x.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains("log_token")), CommandFlags.None))
            .ReturnsAsync((RedisValue)message);

        // Act
        var hash = await cache.GetOrCreateTokenAsync(message, level);

        // Assert
        Assert.That(hash, Is.Not.Null);
        _redisMock.Verify(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), _ttl, ExpireWhen.Always, CommandFlags.None), Times.Once);
    }
}
