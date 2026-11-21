using Tammoa.Services;

namespace Tammoa.Test.Services;

public class LoopAnalysisTest
{
    private static LoopInfo Analyze(string[] nodes, params (string, string)[] edges) => LoopAnalysis.Analyze(nodes, edges);

    [Test]
    public void APlainFlow_HasNoLoops()
    {
        var info = Analyze(["t", "a", "b"], ("t", "a"), ("a", "b"));
        Assert.That(info.HasLoops, Is.False);
        Assert.That(info.BackEdges, Is.Empty);
        Assert.That(info.LoopOf, Is.Empty);
    }

    [Test]
    public void ADiamond_IsNotALoop()
    {
        var info = Analyze(["t", "a", "b", "j"], ("t", "a"), ("t", "b"), ("a", "j"), ("b", "j"));
        Assert.That(info.HasLoops, Is.False);
    }

    [Test]
    public void AConnectionBackToAnEarlierCard_IsTheLoopBack_AndTheCardsBetweenFormTheLoop()
    {
        var info = Analyze(["t", "a", "b", "c", "x"], ("t", "a"), ("a", "b"), ("b", "c"), ("c", "a"), ("c", "x"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("c", "a") }));
        Assert.That(info.LoopMembers.Single().Value, Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(info.LoopOf.Keys, Is.EquivalentTo(new[] { "a", "b", "c" }));
        Assert.That(info.LoopOf.ContainsKey("t"), Is.False, "the trigger before the loop is not in it");
        Assert.That(info.LoopOf.ContainsKey("x"), Is.False, "the card after the loop is not in it");
    }

    [Test]
    public void ALoopOfTwoCards()
    {
        var info = Analyze(["t", "a", "b"], ("t", "a"), ("a", "b"), ("b", "a"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("b", "a") }));
    }

    [Test]
    public void ABackEdgeCanSkipOverCards_ToAnyEarlierCard()
    {
        // "connect back to the card before it or other before it": c -> a jumps over b
        var info = Analyze(["t", "a", "b", "c"], ("t", "a"), ("a", "b"), ("b", "c"), ("c", "a"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("c", "a") }));
        info = Analyze(["t", "a", "b", "c"], ("t", "a"), ("a", "b"), ("b", "c"), ("c", "b"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("c", "b") }));
        Assert.That(info.LoopMembers.Single().Value, Is.EqualTo(new[] { "b", "c" }));
    }

    [Test]
    public void TwoSeparateLoops_AreSeparate()
    {
        var info = Analyze(["t", "a", "b", "c", "d"], ("t", "a"), ("a", "b"), ("b", "a"), ("b", "c"), ("c", "d"), ("d", "c"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("b", "a"), ("d", "c") }));
        Assert.That(info.LoopMembers, Has.Count.EqualTo(2));
        Assert.That(info.LoopOf["a"], Is.EqualTo(info.LoopOf["b"]));
        Assert.That(info.LoopOf["c"], Is.EqualTo(info.LoopOf["d"]));
        Assert.That(info.LoopOf["a"], Is.Not.EqualTo(info.LoopOf["c"]));
    }

    [Test]
    public void ANestedLoop_IsOneGroupOfCards_WithTwoLoopBacks()
    {
        var info = Analyze(["t", "a", "b", "c"], ("t", "a"), ("a", "b"), ("b", "c"), ("c", "b"), ("c", "a"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("c", "b"), ("c", "a") }));
        Assert.That(info.LoopMembers.Single().Value, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void TwoHandlesToTheSameTarget_AreOneConnectionPair()
    {
        var info = Analyze(["t", "a", "i"], ("t", "a"), ("a", "i"), ("i", "a"), ("i", "a"));
        Assert.That(info.BackEdges, Is.EquivalentTo(new[] { ("i", "a") }));
    }

    [Test]
    public void ALoopNothingStartsFrom_IsStillFound_SoItCanBeIgnoredAsUnreachable()
    {
        var info = Analyze(["t", "a", "b"], ("a", "b"), ("b", "a"));
        Assert.That(info.HasLoops, Is.True);
        Assert.That(info.LoopMembers.Single().Value, Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void ConnectionsToUnknownCards_AreIgnored() => Assert.That(Analyze(["a"], ("a", "ghost"), ("ghost", "a")).HasLoops, Is.False);

    [Test]
    public void ALoopMembersKeepTheFlowsOwnOrder()
    {
        var info = Analyze(["t", "c", "a", "b"], ("t", "a"), ("a", "b"), ("b", "c"), ("c", "a"));
        Assert.That(info.LoopMembers.Single().Value, Is.EqualTo(new[] { "c", "a", "b" }));
    }
}
