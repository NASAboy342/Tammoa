using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class ReadFileCard : ICardType
{
    public const string TypeId = "file.read";
    private readonly ITemplateEngine _templates;

    public ReadFileCard(ITemplateEngine templates) => _templates = templates;

    public CardTypeInfo Info { get; } = new(
        TypeId, "Read file", "Reads a text file (UTF-8). Output: { path, text, size, exists }. Use an absolute path or ~/… A missing file gives empty text, or is created if you switch that on.",
        CardCategory.Action, "file-text",
        [
            new CardParamInfo("path", "Path", ParamKind.Text, Required: true, Placeholder: "~/notes/today.txt"),
            new CardParamInfo("maxKilobytes", "Size limit (KB)", ParamKind.Number, DefaultValue: 1024, Help: "Larger files fail instead of filling memory."),
            new CardParamInfo("createIfMissing", "Create the file (and folders) if missing", ParamKind.Bool, DefaultValue: false,
                Help: "Off: a missing file is not an error, the text is empty. On: the empty file and its folders are created first."),
        ])
    {
        Guide = new CardGuide(
            "Reads a text file from this computer so the next card can use its content.",
            "Receives the previous card's output as input; use it in the Path setting if the path is not fixed.",
            new JObject { ["path"] = "/home/me/notes/today.txt", ["text"] = "Buy milk\nCall Dara\n", ["size"] = 20, ["exists"] = true },
            [
                new GuideExample("Read a fixed file", "Use an absolute path, or start with ~/ for your home folder.", new JObject { ["path"] = "~/notes/today.txt" }, "~/notes/today.txt", "text",
                    Result: "Output { path, text, size, exists }. The next card reads the content as {{input.text}}."),
                new GuideExample("Read the file named by the previous card", "A path coming from the input.", new JObject { ["path"] = "{{input.json.path}}" }, "{{input.json.path}}", "text",
                    new JObject { ["json"] = new JObject { ["path"] = "~/notes/today.txt" } }, "Reads whichever file the input names."),
            ],
            ["A missing file is not an error: the text is empty and exists is false (check it with an IF card). Switch on \"Create the file\" to create the empty file and its folders instead.", "Only UTF-8 text files. Files above the size limit fail with a clear message.", "Relative paths are refused so a flow never depends on where the app was started."]),
    };

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var path = FilePaths.RequireAbsolute(_templates.Render(ParamReader.GetString(context.Params, "path"), context.TemplateContext));
        var limit = ParamReader.GetInt(context.Params, "maxKilobytes", 1024);
        if (limit < 1 || limit > 102400) throw new ArgumentException("Size limit must be between 1 and 102400 KB.");
        if (Directory.Exists(path)) throw new InvalidOperationException($"{path} is a folder, not a file.");
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            if (ParamReader.GetBool(context.Params, "createIfMissing", false))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, "", ct);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException($"Could not create {path}: {e.Message}");
                }
            }
            return new JObject { ["path"] = path, ["text"] = "", ["size"] = 0, ["exists"] = false };
        }
        if (info.Length > limit * 1024L) throw new InvalidOperationException($"The file is {info.Length / 1024} KB, over the {limit} KB limit.");
        var text = await File.ReadAllTextAsync(path, ct);
        return new JObject { ["path"] = path, ["text"] = text, ["size"] = info.Length, ["exists"] = true };
    }
}
