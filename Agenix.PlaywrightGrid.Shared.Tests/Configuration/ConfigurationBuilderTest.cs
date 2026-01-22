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

using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Configuration.Providers;

namespace Agenix.PlaywrightGrid.Shared.Tests.Configuration;

public class ConfigurationBuilderTest
{
    [Fact]
    public void ShouldBuildEmptyConfiguration()
    {
        // Arrange & Act
        var config = new ConfigurationBuilder()
            .Build();

        // Assert
        config.Should().NotBeNull();
        config.Properties.Should().BeEmpty();
    }

    [Fact]
    public void ShouldBuildConfigurationWithSingleProvider()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        // Act
        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Assert
        config.Properties.Should().HaveCount(2);
        config.GetValue<string>("key1").Should().Be("value1");
        config.GetValue<int>("key2").Should().Be(42);
    }

    [Fact]
    public void ShouldMergeMultipleProviders()
    {
        // Arrange
        var provider1 = new InMemoryConfigurationProvider(new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        });

        var provider2 = new InMemoryConfigurationProvider(new Dictionary<string, object>
        {
            ["key2"] = "overridden",
            ["key3"] = "value3"
        });

        // Act
        var config = new ConfigurationBuilder()
            .Add(provider1)
            .Add(provider2)
            .Build();

        // Assert
        config.Properties.Should().HaveCount(3);
        config.GetValue<string>("key1").Should().Be("value1");
        config.GetValue<string>("key2").Should().Be("overridden"); // Later provider wins
        config.GetValue<string>("key3").Should().Be("value3");
    }

    [Fact]
    public void ShouldReturnDefaultValueForMissingKey()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .Build();

        // Act & Assert
        config.GetValue("missing", "default").Should().Be("default");
        config.GetValue("missing", 123).Should().Be(123);
        config.GetValue("missing", true).Should().BeTrue();
    }

    [Fact]
    public void ShouldThrowForMissingKeyWithoutDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .Build();

        // Act & Assert
        var act = () => config.GetValue<string>("missing");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void ShouldConvertValueTypes()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["stringValue"] = "test",
            ["intValue"] = 42,
            ["doubleValue"] = 3.14,
            ["boolValue"] = true
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act & Assert
        config.GetValue<string>("stringValue").Should().Be("test");
        config.GetValue<int>("intValue").Should().Be(42);
        config.GetValue<double>("doubleValue").Should().Be(3.14);
        config.GetValue<bool>("boolValue").Should().BeTrue();
    }

    [Fact]
    public void ShouldHandleNestedKeys()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["Server:Url"] = "https://example.com",
            ["Server:Project"] = "test-project",
            ["Launch:Name"] = "test-launch"
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act & Assert
        config.GetValue<string>("Server:Url").Should().Be("https://example.com");
        config.GetValue<string>("Server:Project").Should().Be("test-project");
        config.GetValue<string>("Launch:Name").Should().Be("test-launch");
    }

    [Fact]
    public void ShouldGetArrayValues()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["Tags:0"] = "tag1",
            ["Tags:1"] = "tag2",
            ["Tags:2"] = "tag3"
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act
        var tags = config.GetValues<string>("Tags").ToArray();

        // Assert
        tags.Should().HaveCount(3);
        tags.Should().ContainInOrder("tag1", "tag2", "tag3");
    }

    [Fact]
    public void ShouldGetKeyValues()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["Server:Url"] = "https://example.com",
            ["Server:Project"] = "test-project",
            ["Server:ApiKey"] = "key123",
            ["Launch:Name"] = "test-launch"
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act
        var serverConfig = config.GetKeyValues<string>("Server:*").ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Assert
        serverConfig.Should().HaveCount(3);
        serverConfig["Server:Url"].Should().Be("https://example.com");
        serverConfig["Server:Project"].Should().Be("test-project");
        serverConfig["Server:ApiKey"].Should().Be("key123");
    }

    [Fact]
    public void ShouldSupportFluentBuilder()
    {
        // Arrange & Act
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object> { ["key1"] = "value1" })
            .AddInMemory(new Dictionary<string, object> { ["key2"] = "value2" })
            .Build();

        // Assert
        config.GetValue<string>("key1").Should().Be("value1");
        config.GetValue<string>("key2").Should().Be("value2");
    }

    [Fact]
    public void ShouldHandleCaseInsensitiveKeys()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["Server:Url"] = "https://example.com"
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act & Assert
        config.GetValue<string>("server:url").Should().Be("https://example.com");
        config.GetValue<string>("SERVER:URL").Should().Be("https://example.com");
        config.GetValue<string>("Server:Url").Should().Be("https://example.com");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("TRUE", true)]
    [InlineData("FALSE", false)]
    public void ShouldParseBooleanValues(string input, bool expected)
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["boolValue"] = input
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act & Assert
        config.GetValue<bool>("boolValue").Should().Be(expected);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("0", 0)]
    [InlineData("-123", -123)]
    public void ShouldParseIntegerValues(string input, int expected)
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["intValue"] = input
        };

        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationProvider(properties))
            .Build();

        // Act & Assert
        config.GetValue<int>("intValue").Should().Be(expected);
    }
}
