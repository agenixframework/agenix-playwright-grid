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

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class LabelMatcherEdgeTests
{
    private static LabelKey L(string s)
    {
        var opts = new LabelKeyParsingOptions { EnforceBrowserSecond = false };
        if (!LabelKey.TryParse(s, out var lk, opts))
        {
            Assert.Fail($"Invalid test label: {s}");
        }

        return lk!;
    }

    [Test]
    public void Wildcards_Enabled_Exact_Length_Chooses_Lexicographically_First()
    {
        var requested = L("AppZ:*:UAT");
        var available = new[] { L("AppZ:Chromium:UAT"), L("AppZ:Firefox:UAT") };
        var matcher = new LabelMatcher(new LabelMatchingOptions
        {
            WildcardsEnabled = true,
            TrailingFallbackEnabled = false,
            PrefixExpansionEnabled = false
        });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Not.Null);
        // Among exact-length matches, Normalized order picks Chromium before Firefox
        Assert.That(match!.Normalized, Is.EqualTo("AppZ:Chromium:UAT"));
    }

    [Test]
    public void Fallback_Beats_PrefixExpansion_When_Both_Enabled()
    {
        var requested = L("AppA:Chromium:UAT:EU");
        var available = new[] { L("AppA:Chromium:UAT"), L("AppA:Chromium:UAT:EU:extra") };
        var matcher = new LabelMatcher(new LabelMatchingOptions
        {
            TrailingFallbackEnabled = true,
            PrefixExpansionEnabled = true,
            WildcardsEnabled = false
        });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Not.Null);
        // Trailing fallback to 3 segments should be preferred before expanding to longer prefixes
        Assert.That(match!.Normalized, Is.EqualTo("AppA:Chromium:UAT"));
    }
}
