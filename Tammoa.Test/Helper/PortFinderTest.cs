using System.Net;
using System.Net.Sockets;
using Tammoa.Helper;

namespace Tammoa.Test.Helper;

public class PortFinderTest
{
    [Test]
    public void PreferredPortFree_IsUsed_AndNoRandomPortIsTried()
    {
        var randomCalls = 0;
        Assert.That(PortFinder.Choose(8000, _ => true, () => { randomCalls++; return 50000; }), Is.EqualTo(8000));
        Assert.That(randomCalls, Is.EqualTo(0));
    }

    [Test]
    public void PreferredPortBusy_TriesRandomPortsUntilOneIsFree()
    {
        var next = new Queue<int>([50001, 50002, 50003, 50004, 50005]);
        var checkedPorts = new List<int>();
        var port = PortFinder.Choose(8000, p => { checkedPorts.Add(p); return p == 50003; }, () => next.Dequeue());
        Assert.That(port, Is.EqualTo(50003));
        Assert.That(checkedPorts, Is.EqualTo(new[] { 8000, 50001, 50002, 50003 }), "it stops at the first free one");
    }

    [Test]
    public void NothingFree_GivesUpAfterFiveRandomPorts_WithAClearMessage()
    {
        var checkedPorts = new List<int>();
        var n = 50000;
        var ex = Assert.Throws<InvalidOperationException>(() => PortFinder.Choose(8000, p => { checkedPorts.Add(p); return false; }, () => ++n));
        Assert.That(checkedPorts, Has.Count.EqualTo(6), "the preferred port + exactly 5 random ones");
        Assert.That(ex!.Message, Does.Contain("8000").And.Contain("5 random ports").And.Contain("TAMMOA_PORT"));
    }

    [Test]
    public void ARandomPortThatRepeatsOrEqualsThePreferredOne_IsNotCountedTwice()
    {
        var sequence = new Queue<int>([8000, 50001, 50001, 50002, 50003, 50004, 50005, 50006]);
        var checkedPorts = new List<int>();
        Assert.Throws<InvalidOperationException>(() => PortFinder.Choose(8000, p => { checkedPorts.Add(p); return false; }, () => sequence.Dequeue()));
        Assert.That(checkedPorts.Distinct().Count(), Is.EqualTo(checkedPorts.Count), "every port is checked once");
        Assert.That(checkedPorts, Has.Count.EqualTo(6));
    }

    [Test]
    public void ARealBusyPort_IsSkipped_AndTheReturnedPortReallyIsFree()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        var busy = ((IPEndPoint)holder.LocalEndpoint).Port;
        Assert.That(PortFinder.IsFree(busy), Is.False);
        var chosen = PortFinder.Choose(busy);
        Assert.That(chosen, Is.Not.EqualTo(busy));
        Assert.That(PortFinder.IsFree(chosen), Is.True);
    }

    [TestCase(null, 8000)]
    [TestCase("", 8000)]
    [TestCase("abc", 8000)]
    [TestCase("80", 8000)]
    [TestCase("70000", 8000)]
    [TestCase("8123", 8123)]
    [TestCase(" 9000 ", 9000)]
    public void PreferredFrom_UsesTheEnvironmentValueOnlyWhenItIsAUsablePort(string? value, int expected) => Assert.That(PortFinder.PreferredFrom(value), Is.EqualTo(expected));

    [Test]
    public void PortFile_HoldsThePlainPortNumber_InTheTempFolder()
    {
        Assert.That(PortFinder.PortFile(), Does.StartWith(Path.GetTempPath()).And.EndWith("tammoa-api-port"));
        var file = Path.Combine(Path.GetTempPath(), "tammoa-test-port-" + Guid.NewGuid().ToString("N"));
        try
        {
            PortFinder.WritePortFile(file, 51234);
            Assert.That(File.ReadAllText(file), Is.EqualTo("51234"));
        }
        finally { File.Delete(file); }
    }
}
