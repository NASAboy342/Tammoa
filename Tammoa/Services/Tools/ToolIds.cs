using Tammoa.Models;

namespace Tammoa.Services.Tools;

/// <summary>The built-in tools an AI card can be allowed to use. The order is the order shown in the picker.</summary>
public static class ToolIds
{
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";
    public const string HttpRequest = "http_request";
    public const string RunScript = "run_script";
    public const string CheckPage = "check_page";

    public static readonly IReadOnlyList<ParamOption> Options =
    [
        new(ReadFile, "Read file (inside the root folder)"),
        new(WriteFile, "Write file (inside the root folder)"),
        new(HttpRequest, "HTTP request (allowed hosts only)"),
        new(RunScript, "Run script (inside the root folder, best-effort guard)"),
        new(CheckPage, "Check web page (opens an .html file headless, lists its errors)"),
    ];
}
