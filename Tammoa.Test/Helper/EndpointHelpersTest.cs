using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tammoa.Helper;

namespace Tammoa.Test.Helper;

public class EndpointHelpersTest
{
    private static int Status(IResult result) => ((ContentHttpResult)result).StatusCode ?? 200;

    [Test]
    public async Task Try_ArgumentException_Returns400()
    {
        var result = await EndpointHelpers.Try(() => throw new ArgumentException("bad"));
        Assert.That(Status(result), Is.EqualTo(400));
    }

    [Test]
    public async Task Try_InvalidOperation_Returns409()
    {
        var result = await EndpointHelpers.Try(() => throw new InvalidOperationException("conflict"));
        Assert.That(Status(result), Is.EqualTo(409));
    }

    [Test]
    public async Task Try_UnexpectedException_Returns500()
    {
        var result = await EndpointHelpers.Try(() => throw new Exception("boom"));
        Assert.That(Status(result), Is.EqualTo(500));
    }

    [Test]
    public async Task Try_Success_PassesResultThrough()
    {
        var result = await EndpointHelpers.Try(() => Task.FromResult(JsonHelper.Ok(new { a = 1 })));
        Assert.That(Status(result), Is.EqualTo(200));
    }
}
