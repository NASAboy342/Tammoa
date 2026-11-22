using Tammoa.Helper;
using Tammoa.Services.Tools;

namespace Tammoa.Test.Helper;

public class ScriptPathGuardTest
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "tammoa-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [TestCase("echo hello > notes.txt")]
    [TestCase("cat ./data/a.txt | sort | uniq > out.txt")]
    [TestCase("ls -la 2>/dev/null")]
    [TestCase("const x = a / b; const s = [...items]; // note")]
    [TestCase("print(1..5 if False else 'ok')")]
    [TestCase("curl https://example.com/path -o page.html")]
    [TestCase("open('notes/a.txt', 'w').write('x')")]
    public void ScriptsThatStayInside_AreAccepted(string code) => Assert.DoesNotThrow(() => ScriptPathGuard.Check(code, _root));

    [TestCase("rm -rf /")]
    [TestCase("cat /etc/passwd")]
    [TestCase("echo x > ~/notes.txt")]
    [TestCase("cat ../secret.txt")]
    [TestCase("cd .. && ls")]
    [TestCase("cd ../..")]
    [TestCase("ls $HOME")]
    [TestCase("ls ${HOME}/x")]
    [TestCase("open('/Users/someone/a.txt').read()")]
    [TestCase("const fs = require('fs'); fs.readFileSync('/etc/hosts')")]
    [TestCase("import os; os.path.expanduser('~')")]
    [TestCase("require('os').homedir()")]
    [TestCase("type C:\\Windows\\win.ini")]
    public void ScriptsThatReachOutside_AreRejected(string code) => Assert.Throws<ToolRejectedException>(() => ScriptPathGuard.Check(code, _root));

    [Test]
    public void AnAbsolutePathInsideTheRoot_IsAccepted() => Assert.DoesNotThrow(() => ScriptPathGuard.Check($"cat {Path.Combine(_root, "a.txt")}", _root));
}
