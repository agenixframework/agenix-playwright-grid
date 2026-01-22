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

using Agenix.PlaywrightGrid.Shared.Converters;
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Converters;

public class ItemAttributeFixture
{
    [Theory]
    [InlineData("k1:v1", "k1", "v1")]
    [InlineData("v1", null, "v1")]
    [InlineData(":v1", null, "v1")]
    [InlineData("v1:", null, "v1")]
    [InlineData("k1:v1:v2", "k1", "v1:v2")]
    public void ShouldConvertFromString(string tag, string? expectedKey, string expectedValue)
    {
        var attr = ItemAttributeConverter.ConvertFrom(tag);

        attr.Key.Should().Be(expectedKey);
        attr.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void ShouldUseCustomOptions()
    {
        var attr = ItemAttributeConverter.ConvertFrom("v1", opt => { opt.UndefinedKey = "abc"; });

        attr.Key.Should().Be("abc");
        attr.Value.Should().Be("v1");
    }
}
