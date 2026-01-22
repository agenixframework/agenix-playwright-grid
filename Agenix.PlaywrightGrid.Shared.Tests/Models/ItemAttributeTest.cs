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
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Models;

public class ItemAttributeTest
{
    [Fact]
    public void ShouldCreateItemAttributeWithDefaultConstructor()
    {
        var attribute = new ItemAttribute();

        attribute.Key.Should().BeNull();
        attribute.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void ShouldCreateItemAttributeWithKeyAndValue()
    {
        var attribute = new ItemAttribute { Key = "key", Value = "value" };

        attribute.Key.Should().Be("key");
        attribute.Value.Should().Be("value");
    }

    [Fact]
    public void ShouldCreateItemAttributeWithNullKey()
    {
        var attribute = new ItemAttribute { Key = null!, Value = "value" };

        attribute.Key.Should().BeNull();
        attribute.Value.Should().Be("value");
    }

    [Fact]
    public void ShouldConvertToStringWithKey()
    {
        var attribute = new ItemAttribute { Key = "key", Value = "value" };

        attribute.ToString().Should().Be("key:value");
    }

    [Fact]
    public void ShouldConvertToStringWithoutKey()
    {
        var attribute = new ItemAttribute { Key = null!, Value = "value" };

        attribute.ToString().Should().Be("value");
    }

    [Fact]
    public void ShouldConvertToStringWithEmptyKey()
    {
        var attribute = new ItemAttribute { Key = string.Empty, Value = "value" };

        attribute.ToString().Should().Be("value");
    }

    [Fact]
    public void ShouldAllowSettingProperties()
    {
        var attribute = new ItemAttribute
        {
            Key = "newKey",
            Value = "newValue"
        };

        attribute.Key.Should().Be("newKey");
        attribute.Value.Should().Be("newValue");
    }
}
