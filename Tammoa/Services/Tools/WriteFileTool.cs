using System.Text;
using Newtonsoft.Json.Linq;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

public class WriteFileTool : ITool
{
    public ToolDefinition Definition { get; } = new(ToolIds.WriteFile,
        "Writes text to a file inside the allowed folder (folders are created). mode 'overwrite' replaces the file, 'append' adds to its end.",
        ToolArgs.Schema(new JObject
        {
            ["path"] = ToolArgs.Prop("File path, relative to the allowed folder."),
            ["content"] = ToolArgs.Prop("The text to write."),
            ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("overwrite", "append"), ["description"] = "overwrite (default) or append." },
        }, "path", "content"));

    public bool NeedsRoot => true;

    public void Check(JObject arguments, ToolContext context)
    {
        RootGuard.Resolve(ToolArgs.RequireRoot(context), ToolArgs.Required(arguments, "path"));
        Mode(arguments);
    }

    public async Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct)
    {
        var path = RootGuard.Resolve(ToolArgs.RequireRoot(context), ToolArgs.Required(arguments, "path"));
        var mode = Mode(arguments);
        if (arguments["content"] is null || arguments["content"]!.Type == JTokenType.Null) throw new ArgumentException("Missing argument 'content'.");
        var content = arguments["content"]!.Type == JTokenType.String ? (string)arguments["content"]! : arguments["content"]!.ToString(Newtonsoft.Json.Formatting.None);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (mode == "append") await File.AppendAllTextAsync(path, content, ct);
        else await File.WriteAllTextAsync(path, content, ct);
        return $"Wrote {Encoding.UTF8.GetByteCount(content)} bytes to {ToolArgs.Required(arguments, "path")} ({mode}).";
    }

    private static string Mode(JObject a)
    {
        var mode = ToolArgs.Optional(a, "mode", "overwrite");
        return mode is "overwrite" or "append" ? mode : throw new ArgumentException($"Unknown mode '{mode}' (use overwrite or append).");
    }
}
