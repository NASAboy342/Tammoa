using Newtonsoft.Json.Linq;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class TemplateEngineTest
{
    private readonly TemplateEngine _sut = new();
    private static readonly JObject Ctx = JObject.Parse("""
        { "input": { "text": "hi", "n": 5, "items": [ { "name": "a" }, { "name": "b" } ], "obj": { "k": 1 }, "q": "it's" },
          "run": { "input": "from run" } }
        """);

    [TestCase("{{input.text}}", "hi")]
    [TestCase("  {{ input.text }}  ", "  hi  ")]
    [TestCase("x {{input.n}} y", "x 5 y")]
    [TestCase("{{input.items[1].name}}", "b")]
    [TestCase("{{run.input}}", "from run")]
    [TestCase("{{input.missing}}", "")]
    [TestCase("{{input.items[9].name}}", "")]
    [TestCase("no braces", "no braces")]
    [TestCase("{{input.obj}}", "{\"k\":1}")]
    public void Render_ReplacesExpressions(string template, string expected) =>
        Assert.That(_sut.Render(template, Ctx), Is.EqualTo(expected));

    [Test]
    public void Render_JsonFilter_GivesCompactJson() =>
        Assert.That(_sut.Render("{{input.items | json}}", Ctx), Is.EqualTo("[{\"name\":\"a\"},{\"name\":\"b\"}]"));

    [Test]
    public void Render_ShFilter_QuotesForPosixShell() =>
        Assert.That(_sut.Render("echo {{input.q | sh}}", Ctx), Is.EqualTo("echo 'it'\\''s'"));

    [Test]
    public void Render_UnknownFilter_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => _sut.Render("{{input.text | nope}}", Ctx));

    [Test]
    public void Resolve_WholeExpression_KeepsType()
    {
        var r = _sut.Resolve(JObject.Parse("{\"a\":\"{{input.n}}\",\"b\":\"{{input.obj}}\",\"c\":\"n={{input.n}}\",\"d\":\"{{input.nope}}\",\"e\":7,\"f\":[\"{{input.text}}\"]}"), Ctx);
        Assert.That(r["a"]!.Type, Is.EqualTo(JTokenType.Integer));
        Assert.That(r["b"]!.Type, Is.EqualTo(JTokenType.Object));
        Assert.That((string?)r["c"], Is.EqualTo("n=5"));
        Assert.That(r["d"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That((int)r["e"]!, Is.EqualTo(7));
        Assert.That((string?)r["f"]![0], Is.EqualTo("hi"));
    }

    [Test]
    public void Resolve_DoesNotMutateTheTemplate()
    {
        var template = JObject.Parse("{\"a\":\"{{input.text}}\"}");
        _sut.Resolve(template, Ctx);
        Assert.That((string?)template["a"], Is.EqualTo("{{input.text}}"));
    }

    [Test]
    public void Resolve_WholeExpressionWithFilter_IsText() =>
        Assert.That(_sut.Resolve(new JValue("{{input.n | json}}"), Ctx).Type, Is.EqualTo(JTokenType.String));
}
