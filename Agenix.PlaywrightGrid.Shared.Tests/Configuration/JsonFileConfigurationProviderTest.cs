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

using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Configuration.Providers;

namespace Agenix.PlaywrightGrid.Shared.Tests.Configuration;

public class JsonFileConfigurationProviderTest : IDisposable
{
    private readonly string _tempFilePath;

    public JsonFileConfigurationProviderTest()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Fact]
    public void ShouldLoadSimpleJsonConfiguration()
    {
        // Arrange
        var json = """
        {
          "server": {
            "url": "https://example.com",
            "project": "test-project"
          }
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().ContainKey("server:url");
        properties["server:url"].Should().Be("https://example.com");
        properties["server:project"].Should().Be("test-project");
    }

    [Fact]
    public void ShouldLoadNestedJsonConfiguration()
    {
        // Arrange
        var json = """
        {
          "PlaywrightGrid": {
            "Server": {
              "Url": "https://grid.example.com",
              "Project": "my-project"
            },
            "Launch": {
              "Name": "Test Launch"
            }
          }
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().ContainKey("PlaywrightGrid:Server:Url");
        properties.Should().ContainKey("PlaywrightGrid:Server:Project");
        properties.Should().ContainKey("PlaywrightGrid:Launch:Name");
        properties["PlaywrightGrid:Server:Url"].Should().Be("https://grid.example.com");
    }

    [Fact]
    public void ShouldLoadArrayValues()
    {
        // Arrange
        var json = """
        {
          "launch": {
            "attributes": ["tag1", "tag2", "tag3"]
          }
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().ContainKey("launch:attributes:0");
        properties.Should().ContainKey("launch:attributes:1");
        properties.Should().ContainKey("launch:attributes:2");
        properties["launch:attributes:0"].Should().Be("tag1");
        properties["launch:attributes:1"].Should().Be("tag2");
        properties["launch:attributes:2"].Should().Be("tag3");
    }

    [Fact]
    public void ShouldLoadBooleanValues()
    {
        // Arrange
        var json = """
        {
          "enabled": true,
          "disabled": false
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties["enabled"].Should().Be("True");
        properties["disabled"].Should().Be("False");
    }

    [Fact]
    public void ShouldLoadNumericValues()
    {
        // Arrange
        var json = """
        {
          "timeout": 30,
          "retryCount": 3,
          "percentage": 75.5
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties["timeout"].Should().Be("30");
        properties["retryCount"].Should().Be("3");
        properties["percentage"].Should().Be("75.5");
    }

    [Fact]
    public void ShouldHandleNullValues()
    {
        // Arrange
        var json = """
        {
          "key1": "value",
          "key2": null
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties["key1"].Should().Be("value");
        properties["key2"].Should().Be(string.Empty);
    }

    [Fact]
    public void ShouldReturnEmptyDictionaryWhenFileNotFoundAndOptional()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-file.json");
        var provider = new JsonFileConfigurationProvider(":", nonExistentPath, optional: true);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().BeEmpty();
    }

    [Fact]
    public void ShouldThrowWhenFileNotFoundAndNotOptional()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-file.json");
        var provider = new JsonFileConfigurationProvider(":", nonExistentPath, optional: false);

        // Act & Assert
        var act = () => provider.Load();
        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"*{nonExistentPath}*");
    }

    [Fact]
    public void ShouldThrowOnInvalidJson()
    {
        // Arrange
        File.WriteAllText(_tempFilePath, "{ invalid json }");
        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act & Assert
        var act = () => provider.Load();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{_tempFilePath}*");
    }

    [Fact]
    public void ShouldLoadComplexConfiguration()
    {
        // Arrange
        var json = """
        {
          "$schema": "./schema.json",
          "enabled": true,
          "server": {
            "url": "https://grid.agenix.com",
            "project": "test-project",
            "apiKey": "12345678-90ab-cdef-1234-567890abcdef"
          },
          "launch": {
            "name": "Regression Suite",
            "description": "Daily tests",
            "attributes": ["env:prod", "team:qa"]
          },
          "testRun": {
            "defaultLabelKey": "MyApp:Chromium:UAT",
            "attributes": ["smoke", "regression"]
          },
          "timeout": {
            "seconds": 60
          }
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties["$schema"].Should().Be("./schema.json");
        properties["enabled"].Should().Be("True");
        properties["server:url"].Should().Be("https://grid.agenix.com");
        properties["server:project"].Should().Be("test-project");
        properties["server:apiKey"].Should().Be("12345678-90ab-cdef-1234-567890abcdef");
        properties["launch:name"].Should().Be("Regression Suite");
        properties["launch:description"].Should().Be("Daily tests");
        properties["launch:attributes:0"].Should().Be("env:prod");
        properties["launch:attributes:1"].Should().Be("team:qa");
        properties["testRun:defaultLabelKey"].Should().Be("MyApp:Chromium:UAT");
        properties["timeout:seconds"].Should().Be("60");
    }

    [Fact]
    public void ShouldHandleEmptyObject()
    {
        // Arrange
        File.WriteAllText(_tempFilePath, "{}");
        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().BeEmpty();
    }

    [Fact]
    public void ShouldHandleEmptyArray()
    {
        // Arrange
        var json = """
        {
          "emptyArray": []
        }
        """;
        File.WriteAllText(_tempFilePath, json);

        var provider = new JsonFileConfigurationProvider(":", _tempFilePath, false);

        // Act
        var properties = provider.Load();

        // Assert
        properties.Should().BeEmpty();
    }

    [Fact]
    public void ShouldThrowOnNullFilePath()
    {
        // Act & Assert
        var act = () => new JsonFileConfigurationProvider(":", null!, false);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("filePath");
    }
}
