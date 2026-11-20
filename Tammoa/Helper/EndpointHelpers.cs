using Microsoft.AspNetCore.Http;

namespace Tammoa.Helper;

public static class EndpointHelpers
{
    /// <summary>
    /// Runs an endpoint body and maps exceptions to HTTP status codes:
    /// ArgumentException → 400, KeyNotFoundException → 404, InvalidOperationException → 409, anything else → 500 (logged).
    /// </summary>
    public static async Task<IResult> Try(Func<Task<IResult>> body)
    {
        try
        {
            return await body();
        }
        catch (ArgumentException ex)
        {
            return JsonHelper.Ok(new { error = ex.Message }, StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException ex)
        {
            return JsonHelper.Ok(new { error = ex.Message }, StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return JsonHelper.Ok(new { error = ex.Message }, StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return JsonHelper.Ok(new { error = "Unexpected server error." }, StatusCodes.Status500InternalServerError);
        }
    }

    public static IResult TrySync(Func<IResult> body) => Try(() => Task.FromResult(body())).GetAwaiter().GetResult();
}
