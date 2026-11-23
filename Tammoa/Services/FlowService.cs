using Newtonsoft.Json.Linq;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Services;

public class FlowService : IFlowService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxDefinitionLength = 1_000_000;
    public const string DocumentFormat = "tammoa.flow";
    public const int DocumentVersion = 1;
    /// <summary>Version of the definition JSON itself, so future card changes can be migrated on import/load.</summary>
    public const int DefinitionSchemaVersion = 1;

    private readonly IFlowRepository _repository;
    private readonly TimeProvider _time;
    private readonly IFlowDefinitionValidator _validator;
    private readonly IDataService _data;

    public FlowService(IFlowRepository repository, TimeProvider time, IFlowDefinitionValidator validator, IDataService data)
    {
        _repository = repository;
        _time = time;
        _validator = validator;
        _data = data;
    }

    public IReadOnlyList<FlowSummary> List() =>
        _repository.List()
            .Select(f => new FlowSummary(f.Id, f.Name, f.Description, f.Enabled, CardCount(f.Definition), f.CreatedAt, f.UpdatedAt))
            .ToList();

    public Flow Get(string id) => _repository.Get(id) ?? throw NotFound(id);

    public Flow Create(CreateFlowRequest request)
    {
        var now = _time.GetUtcNow();
        var flow = new Flow(
            Guid.NewGuid().ToString("N"),
            ValidName(request.Name),
            ValidDescription(request.Description),
            false,
            EmptyDefinition(),
            now, now);
        _repository.Insert(flow);
        return flow;
    }

    public Flow Update(string id, UpdateFlowRequest request)
    {
        var existing = Get(id);
        var updated = existing with
        {
            Name = ValidName(request.Name),
            Description = ValidDescription(request.Description),
            Definition = ValidDefinition(request.Definition ?? existing.Definition),
            UpdatedAt = _time.GetUtcNow(),
        };
        Save(updated);
        return updated;
    }

    public void Delete(string id, string? confirmName)
    {
        var existing = Get(id);
        if (string.IsNullOrEmpty(confirmName) || !string.Equals(confirmName, existing.Name, StringComparison.Ordinal))
            throw new ArgumentException("Type the exact flow name to confirm deletion.");
        _repository.Delete(id);
        _data.DeleteFlowData(id); // the flow's Data cards go with it
    }

    public Flow SetEnabled(string id, bool enabled)
    {
        var existing = Get(id);
        if (existing.Enabled == enabled) return existing;
        var updated = existing with { Enabled = enabled, UpdatedAt = _time.GetUtcNow() };
        Save(updated);
        return updated;
    }

    public Flow Duplicate(string id)
    {
        var source = Get(id);
        var suffix = " (copy)";
        var name = source.Name.Length + suffix.Length > MaxNameLength
            ? source.Name[..(MaxNameLength - suffix.Length)] + suffix
            : source.Name + suffix;
        var now = _time.GetUtcNow();
        var copy = source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Enabled = false,
            Definition = (JObject)source.Definition.DeepClone(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _repository.Insert(copy);
        _data.CopyFlowData(source.Id, copy.Id); // a copy starts with the same stored text
        return copy;
    }

    public FlowDocument Export(string id)
    {
        var f = Get(id);
        var data = _data.ExportFlowData(f.Id, f.Definition);
        return new FlowDocument(DocumentFormat, DocumentVersion, f.Name, f.Description, (JObject)f.Definition.DeepClone(), data.HasValues ? data : null);
    }

    public Flow Import(FlowDocument document)
    {
        if (document.Format != DocumentFormat)
            throw new ArgumentException($"Not a Tammoa flow file (expected format '{DocumentFormat}').");
        if (document.Version < 1 || document.Version > DocumentVersion)
            throw new ArgumentException($"Unsupported flow file version {document.Version}; this app reads up to {DocumentVersion}.");
        var now = _time.GetUtcNow();
        var flow = new Flow(
            Guid.NewGuid().ToString("N"),
            ValidName(document.Name),
            ValidDescription(document.Description),
            false,
            ValidDefinition(document.Definition ?? EmptyDefinition()),
            now, now);
        _repository.Insert(flow);
        _data.ImportFlowData(flow.Id, flow.Definition, document.Data);
        return flow;
    }

    private void Save(Flow flow)
    {
        if (!_repository.Update(flow)) throw NotFound(flow.Id); // deleted between read and write
    }

    private static KeyNotFoundException NotFound(string id) => new($"Flow '{id}' was not found.");

    private static JObject EmptyDefinition() => new() { ["schemaVersion"] = DefinitionSchemaVersion, ["nodes"] = new JArray(), ["edges"] = new JArray() };

    private static int CardCount(JObject definition) => definition["nodes"] is JArray nodes ? nodes.Count : 0;

    private static string ValidName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("A flow needs a name.");
        if (trimmed.Length > MaxNameLength) throw new ArgumentException($"The name is limited to {MaxNameLength} characters.");
        return trimmed;
    }

    private static string ValidDescription(string? description)
    {
        var trimmed = description?.Trim() ?? "";
        if (trimmed.Length > MaxDescriptionLength) throw new ArgumentException($"The description is limited to {MaxDescriptionLength} characters.");
        return trimmed;
    }

    public FlowValidation Validate(JObject? definition)
    {
        try
        {
            ValidDefinition(definition ?? throw new ArgumentException("A definition is required: { \"nodes\": [], \"edges\": [] }."));
            return new FlowValidation(true, null);
        }
        catch (ArgumentException ex)
        {
            return new FlowValidation(false, ex.Message);
        }
    }

    private JObject ValidDefinition(JObject definition)
    {
        if (definition.ToString(Newtonsoft.Json.Formatting.None).Length > MaxDefinitionLength)
            throw new ArgumentException("The flow definition is too large.");
        _validator.Validate(definition);
        return definition;
    }
}
