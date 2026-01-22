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

using Dashboard.Models;
using NUnit.Framework;

namespace Dashboard.Tests.Models;

[TestFixture]
public class RetryContextTests
{
    [Test]
    public void Construction_SetsAllProperties()
    {
        // Arrange
        var currentAttempt = 2;
        var maxAttempts = 3;
        var nextDelay = TimeSpan.FromSeconds(4);

        // Act
        var context = new RetryContext
        {
            CurrentAttempt = currentAttempt,
            MaxAttempts = maxAttempts,
            NextDelay = nextDelay
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(context.CurrentAttempt, Is.EqualTo(currentAttempt));
            Assert.That(context.MaxAttempts, Is.EqualTo(maxAttempts));
            Assert.That(context.NextDelay, Is.EqualTo(nextDelay));
        });
    }

    [Test]
    public void Immutability_UsingWithExpression_CreatesNewInstanceAndKeepsOriginal()
    {
        // Arrange
        var original = new RetryContext
        {
            CurrentAttempt = 1,
            MaxAttempts = 3,
            NextDelay = TimeSpan.FromSeconds(2)
        };

        // Act
        var modified = original with { CurrentAttempt = 2 };

        // Assert
        Assert.That(modified.CurrentAttempt, Is.EqualTo(2));
        Assert.That(original.CurrentAttempt, Is.EqualTo(1));
        Assert.That(modified, Is.Not.SameAs(original));

        /*
        // Compile-time immutability check:
        // The following line would fail to compile because properties are init-only:
        // original.CurrentAttempt = 5;
        */
    }
}
