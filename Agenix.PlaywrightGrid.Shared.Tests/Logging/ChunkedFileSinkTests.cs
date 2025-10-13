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
using FluentAssertions;
using Serilog;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Logging;

public class ChunkedFileSinkTests : IDisposable
{
    private readonly string _tempDir;

    public ChunkedFileSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void ChunkedFile_ExpandsEnvironmentVariablesInPath()
    {
        // Arrange
        var envVarName = "TEST_LOG_DIR_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Environment.SetEnvironmentVariable(envVarName, _tempDir);

        var logFileName = "test.log";
        var logPath = Path.Combine(_tempDir, logFileName);

        // Use %VAR% format which Environment.ExpandEnvironmentVariables supports on all platforms in .NET
        var pathWithEnv = Path.Combine($"%{envVarName}%", logFileName);

        var logger = new LoggerConfiguration()
            .WriteTo.ChunkedFile(pathWithEnv)
            .CreateLogger();

        // Act
        logger.Information("Test message");
        ((IDisposable)logger).Dispose();

        // Assert
        File.Exists(logPath).Should().BeTrue($"Log file should be created at {logPath}");
        var content = File.ReadAllText(logPath);
        content.Should().Contain("Test message");
    }
}
