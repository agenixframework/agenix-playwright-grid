using Agenix.PlaywrightGrid.Domain;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class LabelKeyTests
{
    [Test]
    public void TryParse_Valid_MinSegments_Two_AppBrowser_DefaultOptions()
    {
        var ok = LabelKey.TryParse("MyApp:Chromium", out var lk);
        Assert.That(ok, Is.True);
        Assert.That(lk, Is.Not.Null);
        Assert.That(lk!.Original, Is.EqualTo("MyApp:Chromium"));
        Assert.That(lk.Normalized, Is.EqualTo("MyApp:Chromium"));
        Assert.That(lk.Segments, Has.Count.EqualTo(2));
        Assert.That(lk.App, Is.EqualTo("MyApp"));
        Assert.That(lk.Browser, Is.EqualTo("Chromium"));
        Assert.That(lk.Env, Is.EqualTo(""));
    }

    [Test]
    public void TryParse_Trims_And_Rejects_LeadingTrailingColons_And_DoubleColons()
    {
        Assert.That(LabelKey.TryParse(":A:Chromium", out _), Is.False);
        Assert.That(LabelKey.TryParse("A:Chromium:", out _), Is.False);
        Assert.That(LabelKey.TryParse("A::Chromium", out _), Is.False);
        Assert.That(LabelKey.TryParse("  A:Chromium  ", out var lk), Is.True);
        Assert.That(lk!.Original, Is.EqualTo("A:Chromium"));
        Assert.That(lk.Normalized, Is.EqualTo("A:Chromium"));
    }

    [Test]
    public void TryParse_EnforceBrowserSecond_DefaultTrue_Rejects_UnknownBrowser()
    {
        Assert.That(LabelKey.TryParse("AppX:NotABrowser:UAT", out _), Is.False);
    }

    [Test]
    public void TryParse_EnforceBrowserSecond_False_Allows_Any_Second_Segment()
    {
        var opts = new LabelKeyParsingOptions { EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("AppX:NotABrowser:UAT", out var lk, opts), Is.True);
        Assert.That(lk!.Browser, Is.EqualTo("NotABrowser"));
    }

    [Test]
    public void TryParse_CasePolicy_Lower_And_Upper_Are_Applied_To_Normalized()
    {
        var lower = new LabelKeyParsingOptions { CasePolicy = LabelKeyCasePolicy.Lower, EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("MyApp:Chromium:Staging:US", out var lkLower, lower), Is.True);
        Assert.That(lkLower!.Normalized, Is.EqualTo("myapp:chromium:staging:us"));
        Assert.That(lkLower.Original, Is.EqualTo("MyApp:Chromium:Staging:US"));

        var upper = new LabelKeyParsingOptions { CasePolicy = LabelKeyCasePolicy.Upper, EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("MyApp:Firefox:Uat:eu", out var lkUpper, upper), Is.True);
        Assert.That(lkUpper!.Normalized, Is.EqualTo("MYAPP:FIREFOX:UAT:EU"));
        Assert.That(lkUpper.Original, Is.EqualTo("MyApp:Firefox:Uat:eu"));
    }

    [Test]
    public void TryParse_ForbiddenChars_Default_Rejects_Whitespace_In_Segments()
    {
        // Default ForbiddenChars include whitespace; spaces within segments should be rejected
        Assert.That(LabelKey.TryParse("My App:Chromium:UAT", out _), Is.False);
        Assert.That(LabelKey.TryParse("MyApp:Chrom ium:UAT", out _), Is.False);
    }

    [Test]
    public void TryParse_SegmentCount_OutOfBounds_Fails()
    {
        var tooFew = new LabelKeyParsingOptions { MinSegments = 3 };
        Assert.That(LabelKey.TryParse("App:Chromium", out _, tooFew), Is.False);

        var tooMany = new LabelKeyParsingOptions { MaxSegments = 3 };
        Assert.That(LabelKey.TryParse("A:B:C:D", out _, tooMany), Is.False);
    }

    [Test]
    public void Equality_And_HashCode_Use_Normalized()
    {
        var lower = new LabelKeyParsingOptions { CasePolicy = LabelKeyCasePolicy.Lower, EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("MyApp:Chromium:UAT", out var a, lower), Is.True);
        Assert.That(LabelKey.TryParse("myapp:chromium:uat", out var b, lower), Is.True);
        Assert.That(a!.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b!.GetHashCode()));

        // With Keep policy, differing case leads to different Normalized values
        var keep = new LabelKeyParsingOptions { CasePolicy = LabelKeyCasePolicy.Keep, EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("MyApp:Chromium:UAT", out var c, keep), Is.True);
        Assert.That(LabelKey.TryParse("myapp:chromium:uat", out var d, keep), Is.True);
        Assert.That(c!.Equals(d), Is.False);
    }

    [Test]
    public void Accessors_App_Browser_Env_Work_With_Missing_Segments()
    {
        var opts = new LabelKeyParsingOptions { EnforceBrowserSecond = false };
        Assert.That(LabelKey.TryParse("OnlyApp:OnlyBrowser", out var two, opts), Is.True);
        Assert.That(two!.App, Is.EqualTo("OnlyApp"));
        Assert.That(two.Browser, Is.EqualTo("OnlyBrowser"));
        Assert.That(two.Env, Is.EqualTo(""));

        Assert.That(LabelKey.TryParse("A:B:C", out var three, opts), Is.True);
        Assert.That(three!.Env, Is.EqualTo("C"));
    }

    [Test]
    public void TryParse_Null_Or_Empty_Returns_False()
    {
        Assert.That(LabelKey.TryParse(null, out _), Is.False);
        Assert.That(LabelKey.TryParse("   ", out _), Is.False);
    }
}
