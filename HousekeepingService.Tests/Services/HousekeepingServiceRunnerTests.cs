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

using HousekeepingService.Infrastructure;
using HousekeepingService.Services;
using HousekeepingService.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Npgsql;
using NUnit.Framework;
using StackExchange.Redis;

namespace HousekeepingService.Tests.Services;

[TestFixture]
public class HousekeepingServiceRunnerTests
{
    [Test]
    public void CreateApp_ShouldRegisterRequiredServices()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        using var app = HousekeepingServiceRunner.CreateApp(args, services =>
        {
            // Override services that try to connect to external systems
            services.AddSingleton(new Mock<IConnectionMultiplexer>().Object);
            services.AddSingleton(new Mock<IDatabase>().Object);
            services.AddSingleton(new Mock<IHousekeepingDataSource>().Object);
            services.AddSingleton(new Mock<IMinioStorageService>().Object);
        });
        var serviceProvider = app.Services;

        // Assert
        Assert.That(serviceProvider.GetService<IProjectSettingsReader>(), Is.Not.Null);

        // Check if workers are registered as HostedServices
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.That(hostedServices.Any(s => s is LaunchRetentionWorker), Is.True);
        Assert.That(hostedServices.Any(s => s is LaunchAutoStopWorker), Is.True);
        Assert.That(hostedServices.Any(s => s is LogRetentionWorker), Is.True);
        Assert.That(hostedServices.Any(s => s is AttachmentRetentionWorker), Is.True);
        Assert.That(hostedServices.Any(s => s is AuditRetentionWorker), Is.True);
    }
}
