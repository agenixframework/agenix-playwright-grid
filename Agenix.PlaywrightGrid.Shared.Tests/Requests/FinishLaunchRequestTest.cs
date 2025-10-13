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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Requests;

public class FinishLaunchRequestTest
{
    [Fact]
    public void ShouldCreateFinishLaunchRequestWithEndTime()
    {
        var endTime = DateTime.UtcNow;
        var request = new FinishLaunchRequest
        {
            EndTime = endTime
        };

        request.EndTime.Should().Be(endTime);
    }

    [Fact]
    public void ShouldDefaultEndTimeToUtcNow()
    {
        var request = new FinishLaunchRequest();

        // Allow a small time difference (within 1 second)
        request.EndTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }
}
