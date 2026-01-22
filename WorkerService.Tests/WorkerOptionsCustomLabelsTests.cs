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

using NUnit.Framework;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class WorkerOptionsCustomLabelsTests
{
    [SetUp]
    public void ClearEnv()
    {
        // Clear related env vars to avoid leakage between tests
        Environment.SetEnvironmentVariable("NODE_LABEL_ALLOWED_KEYS", null);
        Environment.SetEnvironmentVariable("NODE_LABEL_VALUES_CHANNEL", null);
        Environment.SetEnvironmentVariable("NODE_LABEL_VALUES_HEADLESS", null);
        Environment.SetEnvironmentVariable("NODE_LABELS", null);
        Environment.SetEnvironmentVariable("NODE_LABEL_CHANNEL", null);
        Environment.SetEnvironmentVariable("NODE_LABEL_HEADLESS", null);
        Environment.SetEnvironmentVariable("NODE_REGION", null);
        Environment.SetEnvironmentVariable("POOL_CONFIG", null);
    }

    [Test]
    public void CustomLabels_Defaults_Channel_And_Headless_AreAccepted_AndUnknownIgnored()
    {
        var prevWsHost = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST");
        var prevWsPort = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT");
        try
        {
            // Arrange
            Environment.SetEnvironmentVariable("NODE_LABELS", "channel=beta;headless=true;foo=bar");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            // Act
            var opts = WorkerOptions.FromEnvironment();

            // Assert
            Assert.That(opts.Labels["channel"], Is.EqualTo("beta"));
            Assert.That(opts.Labels["headless"], Is.EqualTo("true"));
            Assert.That(opts.Labels.ContainsKey("foo"), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", prevWsHost);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", prevWsPort);
        }
    }

    [Test]
    public void CustomLabels_ValuesOutsideWhitelist_CoalesceToOther()
    {
        var prevWsHost = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST");
        var prevWsPort = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT");
        try
        {
            // Arrange
            Environment.SetEnvironmentVariable("NODE_LABELS", "channel=nightly;headless=maybe");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            // Act
            var opts = WorkerOptions.FromEnvironment();

            // Assert
            Assert.That(opts.Labels["channel"], Is.EqualTo("other"));
            Assert.That(opts.Labels["headless"], Is.EqualTo("other"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", prevWsHost);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", prevWsPort);
        }
    }

    [Test]
    public void CustomLabels_AllowedKeysAndValues_Override_Defaults()
    {
        var prevWsHost = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST");
        var prevWsPort = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT");
        try
        {
            // Arrange
            Environment.SetEnvironmentVariable("NODE_LABEL_ALLOWED_KEYS", "channel;team");
            Environment.SetEnvironmentVariable("NODE_LABEL_VALUES_CHANNEL", "stable,nightly,other");
            Environment.SetEnvironmentVariable("NODE_LABELS", "channel=nightly;team=purple;headless=true");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            // Act
            var opts = WorkerOptions.FromEnvironment();

            // Assert
            Assert.That(opts.Labels.ContainsKey("headless"), Is.False, "headless was not in allowed keys");
            Assert.That(opts.Labels["channel"], Is.EqualTo("nightly"));
            // team only allowed default -> other
            Assert.That(opts.Labels["team"], Is.EqualTo("other"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", prevWsHost);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", prevWsPort);
        }
    }
}
