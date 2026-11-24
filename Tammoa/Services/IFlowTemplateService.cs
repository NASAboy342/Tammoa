using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Ready-made flows a new user can start from.</summary>
public interface IFlowTemplateService
{
    IReadOnlyList<FlowTemplateInfo> List();

    /// <summary>Creates a new, disabled flow from a template. For a template with AI cards the given connection (and model) are filled in.</summary>
    /// <exception cref="KeyNotFoundException">No such template (404).</exception>
    /// <exception cref="ArgumentException">The connection does not exist or is not an Ollama connection (400).</exception>
    Flow Create(string id, CreateFromTemplateRequest request);
}
