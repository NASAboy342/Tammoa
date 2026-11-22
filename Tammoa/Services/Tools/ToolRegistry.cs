namespace Tammoa.Services.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools) => _tools = tools.ToDictionary(t => t.Definition.Name);

    public ITool? Find(string id) => _tools.GetValueOrDefault(id);
}
