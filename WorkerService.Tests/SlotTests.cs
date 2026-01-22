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

using System.Diagnostics;
using NUnit.Framework;
using WorkerService.Services;

namespace WorkerService.Tests;

public class SlotTests
{
    [Test]
    public void Slot_Record_AssignsProperties()
    {
        using var proc = new Process();
        var started = TestTime.FixedUtc();
        var slot = new Slot(proc, "Chromium", "ws://internal", "ws://public", started);

        Assert.That(slot.Proc, Is.SameAs(proc));
        Assert.That(slot.BrowserType, Is.EqualTo("Chromium"));
        Assert.That(slot.InternalWs, Is.EqualTo("ws://internal"));
        Assert.That(slot.PublicWs, Is.EqualTo("ws://public"));
        Assert.That(slot.StartedAt, Is.EqualTo(started));
    }
}
