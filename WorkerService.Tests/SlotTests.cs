using System;
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
        var started = DateTime.UtcNow;
        var slot = new Slot(proc, "Chromium", "ws://internal", "ws://public", started);

        Assert.That(slot.Proc, Is.SameAs(proc));
        Assert.That(slot.BrowserType, Is.EqualTo("Chromium"));
        Assert.That(slot.InternalWs, Is.EqualTo("ws://internal"));
        Assert.That(slot.PublicWs, Is.EqualTo("ws://public"));
        Assert.That(slot.StartedAt, Is.EqualTo(started).Within(TimeSpan.FromMilliseconds(1)));
    }
}
