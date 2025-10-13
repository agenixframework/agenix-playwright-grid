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

using HousekeepingService.Shared;
using NUnit.Framework;

namespace HousekeepingService.Tests.Shared;

[TestFixture]
public class RetentionSettingsTests
{
    [Test]
    public void RetentionSettings_ShouldInitializeWithCorrectValues()
    {
        // Arrange & Act
        var settings = new RetentionSettings
        {
            ProjectKey = "test-project",
            KeepLaunchesDays = 30,
            KeepLogsDays = 7,
            KeepAttachmentsDays = 14,
            KeepAuditDays = 90,
            LaunchInactivityTimeout = "1h"
        };

        // Assert
        Assert.That(settings.ProjectKey, Is.EqualTo("test-project"));
        Assert.That(settings.KeepLaunchesDays, Is.EqualTo(30));
        Assert.That(settings.KeepLogsDays, Is.EqualTo(7));
        Assert.That(settings.KeepAttachmentsDays, Is.EqualTo(14));
        Assert.That(settings.KeepAuditDays, Is.EqualTo(90));
        Assert.That(settings.LaunchInactivityTimeout, Is.EqualTo("1h"));
    }
}
