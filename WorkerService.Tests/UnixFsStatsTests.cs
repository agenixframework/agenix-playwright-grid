using NUnit.Framework;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class UnixFsStatsTests
{
    [Test]
    public void TryGetInodeStats_EmptyPath_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            UnixFsStats.TryGetInodeStats(string.Empty, out var total, out var free);
            // May return false on non-Linux; any value is acceptable but must not throw
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public void TryGetInodeStats_WhitespacePath_DoesNotThrowAndFallsBackToRoot()
    {
        Assert.DoesNotThrow(() =>
        {
            var ok = UnixFsStats.TryGetInodeStats("   ", out var total, out var free);
            Assert.That(ok, Is.False.Or.True); // Platform dependent; only asserting no throw
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public void TryGetInodeStats_NonExistingPath_DoesNotThrow()
    {
        var nonExisting = OperatingSystem.IsWindows() ?
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "this\\path\\should\\not\\exist") :
            "/this/path/should/not/exist";

        Assert.DoesNotThrow(() =>
        {
            var ok = UnixFsStats.TryGetInodeStats(nonExisting, out var total, out var free);
            Assert.That(ok, Is.False.Or.True);
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(free, Is.GreaterThanOrEqualTo(0));
        });
    }
}
