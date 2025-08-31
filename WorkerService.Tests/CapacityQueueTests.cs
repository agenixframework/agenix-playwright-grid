using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PlaywrightHub.Infrastructure.Web;

namespace WorkerService.Tests;

public class CapacityQueueTests
{
    [SetUp]
    public void SetUp()
    {
        EndpointCapacityQueue.Reset();
    }

    [Test]
    public void PerLabelCap_IsEnforced()
    {
        EndpointCapacityQueue.Configure(perLabelCap: 2, perRunCap: 10);

        var (t1, r1) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        var (t2, r2) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-2");
        var (t3, r3) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-3");

        Assert.That(t1, Is.Not.Null);
        Assert.That(r1, Is.Null);
        Assert.That(t2, Is.Not.Null);
        Assert.That(r2, Is.Null);
        Assert.That(t3, Is.Null);
        Assert.That(r3, Is.EqualTo("per-label-cap"));

        Assert.That(EndpointCapacityQueue.GetQueueLength("App:Chromium:UAT"), Is.EqualTo(2));

        EndpointCapacityQueue.Remove(t1!);
        EndpointCapacityQueue.Remove(t2!);
    }

    [Test]
    public void PerRunCap_IsEnforced_AcrossLabels()
    {
        EndpointCapacityQueue.Configure(perLabelCap: 10, perRunCap: 1);

        var (t1, r1) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        var (t2, r2) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        var (t3, r3) = EndpointCapacityQueue.TryEnqueue("App:Firefox:UAT", "run-1");

        Assert.That(t1, Is.Not.Null);
        Assert.That(r1, Is.Null);
        Assert.That(t2, Is.Null);
        Assert.That(r2, Is.EqualTo("per-run-cap"));
        Assert.That(t3, Is.Null);
        Assert.That(r3, Is.EqualTo("per-run-cap"));

        Assert.That(EndpointCapacityQueue.GetQueueLength("App:Chromium:UAT"), Is.EqualTo(1));
        Assert.That(EndpointCapacityQueue.GetQueueLength("App:Firefox:UAT"), Is.EqualTo(0));

        EndpointCapacityQueue.Remove(t1!);
    }

    [Test]
    public async Task Wait_Signal_Grants_Waiter()
    {
        EndpointCapacityQueue.Configure(perLabelCap: 10, perRunCap: 10);

        var (token, reason) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        Assert.That(token, Is.Not.Null);
        Assert.That(reason, Is.Null);

        var waitTask = EndpointCapacityQueue.WaitAsync(token!, TimeSpan.FromSeconds(1));

        // Signal after a short delay to simulate capacity becoming available
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            EndpointCapacityQueue.Signal("App:Chromium:UAT");
        });

        var granted = await waitTask;
        Assert.That(granted, Is.True);
    }

    [Test]
    public async Task Timeout_Removal_ReleasesPerRunSlot()
    {
        EndpointCapacityQueue.Configure(perLabelCap: 10, perRunCap: 1);

        var (t1, r1) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        Assert.That(t1, Is.Not.Null);
        Assert.That(r1, Is.Null);

        var granted = await EndpointCapacityQueue.WaitAsync(t1!, TimeSpan.FromMilliseconds(100));
        Assert.That(granted, Is.False, "Should timeout without a signal");

        // Mimic endpoint behavior on timeout: remove the token to clear per-run pending count
        EndpointCapacityQueue.Remove(t1!);

        var (t2, r2) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-1");
        Assert.That(t2, Is.Not.Null, "Per-run cap should be released after removal");
        Assert.That(r2, Is.Null);

        EndpointCapacityQueue.Remove(t2!);
    }

    [Test]
    public async Task Canceled_Waiters_Are_Skipped_On_Signal()
    {
        EndpointCapacityQueue.Configure(perLabelCap: 10, perRunCap: 10);

        var (t1, r1) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-A");
        var (t2, r2) = EndpointCapacityQueue.TryEnqueue("App:Chromium:UAT", "run-B");
        Assert.That(t1, Is.Not.Null);
        Assert.That(t2, Is.Not.Null);

        // Cancel the first waiter
        EndpointCapacityQueue.Remove(t1!);

        var wait2 = EndpointCapacityQueue.WaitAsync(t2!, TimeSpan.FromSeconds(1));
        EndpointCapacityQueue.Signal("App:Chromium:UAT");

        var granted2 = await wait2;
        Assert.That(granted2, Is.True, "Signal should skip canceled and grant next");
    }
}
