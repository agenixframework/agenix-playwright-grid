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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Requests;

public class StartLaunchRequestTest
{
    [Fact]
    public void ShouldCreateStartLaunchRequestWithRequiredProperties()
    {
        var request = new StartLaunchRequest
        {
            Name = "Test Launch"
        };

        request.Name.Should().Be("Test Launch");
        request.Attributes.Should().BeNull();
    }

    [Fact]
    public void ShouldAllowOptionalDescription()
    {
        var request = new StartLaunchRequest
        {
            Name = "Test Launch",
            Description = "Launch description"
        };

        request.Description.Should().Be("Launch description");
    }

    [Fact]
    public void ShouldAllowAttributes()
    {
        var attributes = new List<ItemAttribute>
        {
            new ItemAttribute { Key = "env", Value = "prod" },
            new ItemAttribute { Key = "build", Value = "123" }
        };
        var request = new StartLaunchRequest
        {
            Name = "Test Launch",
            Attributes = attributes
        };

        request.Attributes.Should().BeEquivalentTo(attributes);
    }

    [Fact]
    public void ShouldAllowOptionalAttributes()
    {
        // Attributes are now optional
        var request = new StartLaunchRequest
        {
            Name = "Test Launch"
        };

        request.Attributes.Should().BeNull();
    }
}
