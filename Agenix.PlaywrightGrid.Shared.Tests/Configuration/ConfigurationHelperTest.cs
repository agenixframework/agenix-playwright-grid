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

public class ConfigurationHelperTest : IDisposable
{
    private readonly string _tempJsonFile;

    public ConfigurationHelperTest()
    {
        _tempJsonFile = Path.Combine(Path.GetTempPath(), $"test-grid-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempJsonFile))
        {
            File.Delete(_tempJsonFile);
        }
    }

    [Fact]
    public void ShouldGetServerUrlFromNewKey()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.ServerUrl] = "https://grid.example.com"
            })
            .Build();

        // Act
        var url = ConfigurationHelper.GetServerUrl(config);

        // Assert
        url.Should().Be("https://grid.example.com");
    }

    [Fact]
    public void ShouldFallbackToLegacyHubUrl()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.HubUrl] = "https://legacy.example.com"
            })
            .Build();

        // Act
        var url = ConfigurationHelper.GetServerUrl(config);

        // Assert
        url.Should().Be("https://legacy.example.com");
    }

    [Fact]
    public void ShouldPreferNewKeyOverLegacy()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.ServerUrl] = "https://new.example.com",
                [ConfigurationKeys.HubUrl] = "https://legacy.example.com"
            })
            .Build();

        // Act
        var url = ConfigurationHelper.GetServerUrl(config);

        // Assert
        url.Should().Be("https://new.example.com");
    }

    [Fact]
    public void ShouldThrowWhenServerUrlNotConfigured()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act & Assert
        var act = () => ConfigurationHelper.GetServerUrl(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Server URL not configured*");
    }

    [Fact]
    public void ShouldGetProjectKey()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.ServerProject] = "my-project"
            })
            .Build();

        // Act
        var projectKey = ConfigurationHelper.GetProjectKey(config);

        // Assert
        projectKey.Should().Be("my-project");
    }

    [Fact]
    public void ShouldGetApiKey()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.ServerApiKey] = "api-key-123"
            })
            .Build();

        // Act
        var apiKey = ConfigurationHelper.GetApiKey(config);

        // Assert
        apiKey.Should().Be("api-key-123");
    }

    [Fact]
    public void ShouldReturnNullWhenApiKeyNotConfigured()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var apiKey = ConfigurationHelper.GetApiKey(config);

        // Assert
        apiKey.Should().BeNull();
    }

    [Fact]
    public void ShouldReturnTrueWhenEnabledNotConfigured()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var enabled = ConfigurationHelper.IsEnabled(config);

        // Assert
        enabled.Should().BeTrue(); // Default is true
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldGetEnabledValue(bool configValue, bool expected)
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.Enabled] = configValue
            })
            .Build();

        // Act
        var enabled = ConfigurationHelper.IsEnabled(config);

        // Assert
        enabled.Should().Be(expected);
    }

    [Fact]
    public void ShouldGetDefaultLabelKey()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.TestItemDefaultLabelKey] = "MyApp:Chromium:UAT"
            })
            .Build();

        // Act
        var labelKey = ConfigurationHelper.GetDefaultLabelKey(config);

        // Assert
        labelKey.Should().Be("MyApp:Chromium:UAT");
    }

    [Fact]
    public void ShouldGetLaunchAttributes()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [$"{ConfigurationKeys.LaunchAttributes}:0"] = "tag1",
                [$"{ConfigurationKeys.LaunchAttributes}:1"] = "tag2",
                [$"{ConfigurationKeys.LaunchAttributes}:2"] = "tag3"
            })
            .Build();

        // Act
        var attributes = ConfigurationHelper.GetLaunchAttributes(config);

        // Assert
        attributes.Should().HaveCount(3);
        attributes.Should().ContainInOrder("tag1", "tag2", "tag3");
    }

    [Fact]
    public void ShouldReturnEmptyArrayWhenNoLaunchAttributes()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var attributes = ConfigurationHelper.GetLaunchAttributes(config);

        // Assert
        attributes.Should().BeEmpty();
    }

    [Fact]
    public void ShouldGetTimeoutSeconds()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.TimeoutSeconds] = 60
            })
            .Build();

        // Act
        var timeout = ConfigurationHelper.GetTimeoutSeconds(config);

        // Assert
        timeout.Should().Be(60);
    }

    [Fact]
    public void ShouldReturnDefaultTimeoutWhenNotConfigured()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var timeout = ConfigurationHelper.GetTimeoutSeconds(config);

        // Assert
        timeout.Should().Be(30); // Default
    }

    [Fact]
    public void ShouldLoadFromJsonFile()
    {
        // Arrange
        var json = """
        {
          "PlaywrightGrid": {
            "Server": {
              "Url": "https://grid.example.com",
              "Project": "test-project"
            },
            "TestItem": {
              "DefaultLabelKey": "App:Chromium:UAT"
            }
          }
        }
        """;
        File.WriteAllText(_tempJsonFile, json);

        // Act
        var config = ConfigurationHelper.FromJsonFile(_tempJsonFile);

        // Assert
        ConfigurationHelper.GetServerUrl(config).Should().Be("https://grid.example.com");
        ConfigurationHelper.GetProjectKey(config).Should().Be("test-project");
        ConfigurationHelper.GetDefaultLabelKey(config).Should().Be("App:Chromium:UAT");
    }

    [Fact]
    public void ShouldHandleOptionalMissingJsonFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-file.json");

        // Act
        var config = ConfigurationHelper.FromJsonFile(nonExistentPath, optional: true);

        // Assert
        config.Should().NotBeNull();
        config.Properties.Should().BeEmpty();
    }

    [Fact]
    public void ShouldLoadFromMultipleSources()
    {
        // Arrange
        var json = """
        {
          "PlaywrightGrid": {
            "Server": {
              "Url": "https://grid.example.com",
              "Project": "json-project"
            }
          }
        }
        """;
        File.WriteAllText(_tempJsonFile, json);

        var defaults = new Dictionary<string, object>
        {
            ["PlaywrightGrid:Server:Project"] = "default-project",
            ["PlaywrightGrid:Launch:Name"] = "Default Launch"
        };

        // Act
        var config = ConfigurationHelper.FromMultipleSources(_tempJsonFile, defaults);

        // Assert
        // JSON overrides defaults
        ConfigurationHelper.GetProjectKey(config).Should().Be("json-project");
        // Default is used when not in JSON
        ConfigurationHelper.GetLaunchName(config).Should().Be("Default Launch");
    }

    [Fact]
    public void ShouldGetSuiteAttributes()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [$"{ConfigurationKeys.SuiteAttributes}:0"] = "smoke",
                [$"{ConfigurationKeys.SuiteAttributes}:1"] = "regression"
            })
            .Build();

        // Act
        var attributes = ConfigurationHelper.GetSuiteAttributes(config);

        // Assert
        attributes.Should().HaveCount(2);
        attributes.Should().ContainInOrder("smoke", "regression");
    }

    [Fact]
    public void ShouldGetTestItemAttributes()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [$"{ConfigurationKeys.TestItemAttributes}:0"] = "automated",
                [$"{ConfigurationKeys.TestItemAttributes}:1"] = "priority:high"
            })
            .Build();

        // Act
        var attributes = ConfigurationHelper.GetTestItemAttributes(config);

        // Assert
        attributes.Should().HaveCount(2);
        attributes.Should().ContainInOrder("automated", "priority:high");
    }

    [Fact]
    public void ShouldGetRetryCount()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.RetryCount] = 5
            })
            .Build();

        // Act
        var retryCount = ConfigurationHelper.GetRetryCount(config);

        // Assert
        retryCount.Should().Be(5);
    }

    [Fact]
    public void ShouldGetMaxConcurrency()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemory(new Dictionary<string, object>
            {
                [ConfigurationKeys.MaxConcurrency] = 20
            })
            .Build();

        // Act
        var maxConcurrency = ConfigurationHelper.GetMaxConcurrency(config);

        // Assert
        maxConcurrency.Should().Be(20);
    }
}
