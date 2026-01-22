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

using Agenix.PlaywrightGrid.Shared.Helpers;
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests;

public class MimeTypeTest
{
    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/png", ".Png")]
    [InlineData("image/png", "png")]
    [InlineData("application/octet-stream", ".unknown")]
    public void GetMimeType(string expectedMime, string fileExtension)
    {
        MimeTypes.MimeTypeMap.GetMimeType(fileExtension).Should().Be(expectedMime);
    }

    [Fact]
    public void ShouldThrowArgumentNullException()
    {
        Action act = () => MimeTypes.MimeTypeMap.GetMimeType(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
