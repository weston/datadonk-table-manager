using DataDonkTM.Betting;
using DataDonkTM.Core;
using Xunit;

namespace DataDonkTM.Tests;

public class TypingMethodTests
{
    [Fact]
    public void NewSite_TriesPreferredMethodsFirst() =>
        Assert.Equal(new[] { TypeMethod.Accessibility, TypeMethod.TextBox, TypeMethod.WindowMessages, TypeMethod.VirtualKeys, TypeMethod.Unicode },
            BetExecutor.MethodsToTry(new SiteProfile(), boxExposed: true, textBoxFound: true));

    [Fact]
    public void MissingBoxes_AreSkipped()
    {
        var methods = BetExecutor.MethodsToTry(new SiteProfile(), boxExposed: false, textBoxFound: false);
        Assert.DoesNotContain(TypeMethod.Accessibility, methods);
        Assert.DoesNotContain(TypeMethod.TextBox, methods);
    }

    [Fact]
    public void LearnedMethod_GoesFirst()
    {
        var site = new SiteProfile { Typing = TypeMethod.VirtualKeys, TypingLearned = true };
        Assert.Equal(new[] { TypeMethod.VirtualKeys, TypeMethod.Accessibility, TypeMethod.WindowMessages, TypeMethod.Unicode },
            BetExecutor.MethodsToTry(site, boxExposed: true, textBoxFound: false));
    }
}
