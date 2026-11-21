using System.Text;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class WriteFileCard : ICardType
{
    public const string TypeId = "file.write";
    private readonly ITemplateEngine _templates;

    public WriteFileCard(ITemplateEngine templates) => _templates = templates;

    public CardTypeInfo Info { get; } = new(
        TypeId, "Write file", "Writes (or appends) text to a file (UTF-8). Output: { path, bytes, mode }. Use an absolute path or ~/…",
        CardCategory.Action, "file-plus",
        [
            new CardParamInfo("path", "Path", ParamKind.Text, Required: true, Placeholder: "~/notes/out.txt"),
            new CardParamInfo("content", "Content", ParamKind.Code, CodeLanguage: "text", Required: true, Placeholder: "{{input.text}}"),
            new CardParamInfo("mode", "If the file exists", ParamKind.Select, DefaultValue: "overwrite", Options: [new("overwrite", "Overwrite"), new("append", "Append")]),
            new CardParamInfo("createDirectories", "Create missing folders", ParamKind.Bool, DefaultValue: true),
        ]);

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var path = FilePaths.RequireAbsolute(_templates.Render(ParamReader.GetString(context.Params, "path"), ctx));
        var content = _templates.Render(ParamReader.GetString(context.Params, "content"), ctx);
        var mode = ParamReader.GetString(context.Params, "mode");
        if (mode.Length == 0) mode = "overwrite";
        if (mode is not ("overwrite" or "append")) throw new ArgumentException($"Unknown mode '{mode}'.");

        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
        {
            if (!ParamReader.GetBool(context.Params, "createDirectories", true)) throw new InvalidOperationException($"Folder does not exist: {dir}");
            Directory.CreateDirectory(dir);
        }
        if (mode == "append") await File.AppendAllTextAsync(path, content, ct);
        else await File.WriteAllTextAsync(path, content, ct);
        return new JObject { ["path"] = path, ["bytes"] = Encoding.UTF8.GetByteCount(content), ["mode"] = mode };
    }
}
