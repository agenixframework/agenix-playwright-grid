#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

using System;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class LabelMatcherTests
{
    private static LabelKey L(string s)
    {
        var opts = new LabelKeyParsingOptions { EnforceBrowserSecond = false };
        if (!LabelKey.TryParse(s, out var lk, opts))
        {
            throw new ArgumentException($"Invalid label: {s}");
        }

        return lk!;
    }

    [Test]
    public void Exact_Match_Returns_Requested_Label()
    {
        var requested = L("AppA:Chromium:UAT");
        var available = new[] { L("AppA:Chromium:UAT"), L("AppA:Chromium"), L("Other:Firefox:UAT") };
        var matcher = new LabelMatcher(new LabelMatchingOptions { WildcardsEnabled = false });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Normalized, Is.EqualTo("AppA:Chromium:UAT"));
    }

    [Test]
    public void Trailing_Fallback_Drops_To_MinSegments_Default_2()
    {
        var requested = L("AppA:Chromium:UAT:EU");
        var available = new[] { L("AppA:Chromium"), L("AppA:Chromium:UAT") };
        var matcher =
            new LabelMatcher(
                new LabelMatchingOptions { TrailingFallbackEnabled = true, PrefixExpansionEnabled = false });

        var match = matcher.TryMatch(requested, available);
        // exact missing; fallback from 4->3 matches 3-segment entry
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Normalized, Is.EqualTo("AppA:Chromium:UAT"));
    }

    [Test]
    public void Trailing_Fallback_Respects_MinSegmentsForFallback()
    {
        var requested = L("AppA:Chromium:UAT");
        var available = new[] { L("AppA:Chromium") };
        var matcher = new LabelMatcher(new LabelMatchingOptions
        {
            TrailingFallbackEnabled = true,
            MinSegmentsForFallback = 3,
            PrefixExpansionEnabled = false
        });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Null, "Should not fallback below 3 segments");
    }

    [Test]
    public void Prefix_Expansion_Picks_Shortest_More_Specific_Then_Lexicographical()
    {
        var requested = L("AppB:Chromium");
        var available = new[] { L("AppB:Chromium:UAT:x"), L("AppB:Chromium:UAT:y"), L("AppB:Chromium:UAT:zz") };
        var matcher =
            new LabelMatcher(
                new LabelMatchingOptions { PrefixExpansionEnabled = true, TrailingFallbackEnabled = false });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Not.Null);
        // Shortest length (3 segments). Among equals, lexicographically first is x
        Assert.That(match!.Normalized, Is.EqualTo("AppB:Chromium:UAT:x"));
    }

    [Test]
    public void Wildcards_Disabled_Do_Not_Match_Asterisk_Segments()
    {
        var requested = L("AppA:*:UAT");
        var available = new[] { L("AppA:Firefox:UAT") };
        var matcher = new LabelMatcher(new LabelMatchingOptions
        {
            WildcardsEnabled = false,
            PrefixExpansionEnabled = true,
            TrailingFallbackEnabled = true
        });

        var match = matcher.TryMatch(requested, available);
        Assert.That(match, Is.Null);
    }

    [Test]
    public void Wildcards_Enabled_Match_Any_Segment_In_Exact_And_Prefix()
    {
        var requested = L("AppA:*:UAT");
        var available = new[] { L("AppA:Firefox:UAT"), L("AppA:Chromium:UAT:EU") };
        var matcher = new LabelMatcher(new LabelMatchingOptions
        {
            WildcardsEnabled = true,
            TrailingFallbackEnabled = false,
            PrefixExpansionEnabled = true
        });

        var match = matcher.TryMatch(requested, available);
        // Exact should win (3 segments) before prefix expansion to 4
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Normalized, Is.EqualTo("AppA:Firefox:UAT"));
    }
}
