using NUnit.Framework;

namespace Dashboard.Tests;

public class SanityTest
{
    [Test]
    public void AddsTwoAndTwo()
    {
        Assert.That(2 + 2, Is.EqualTo(4));
    }
}
