using System.Text;
using Newtonsoft.Json.Linq;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

public class ReadFileTool : ITool
{
    public const int MaxBytes = 256 * 1024;

    public ToolDefinition Definition { get; } = new(ToolIds.ReadFile,
        "Reads a UTF-8 text file inside the allowed folder and returns its text. Use it to look at a file that already exists (to check or continue your work). Use a path relative to the allowed folder.",
        ToolArgs.Schema(new JObject { ["path"] = ToolArgs.Prop("File path, relative to the allowed folder.") }, "path"));

    public bool NeedsRoot => true;

    public void Check(JObject arguments, ToolContext context) => RootGuard.Resolve(ToolArgs.RequireRoot(context), ToolArgs.Required(arguments, "path"));

    public async Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct)
    {
        var path = RootGuard.Resolve(ToolArgs.RequireRoot(context), ToolArgs.Required(arguments, "path"));
        if (!File.Exists(path)) throw new InvalidOperationException($"File not found: {ToolArgs.Required(arguments, "path")}");
        await using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(stream.Length, MaxBytes)];
        var read = 0;
        while (read < buffer.Length && await stream.ReadAsync(buffer.AsMemory(read), ct) is > 0 and var n) read += n;
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return stream.Length > MaxBytes ? text + $"\n[cut: the file has {stream.Length} bytes, the first {MaxBytes} are shown]" : text;
    }
}
