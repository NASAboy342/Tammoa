using Tammoa.Models;

namespace Tammoa.Services;

public interface IFlowService
{
    /// <summary>Flows without definitions, most recently updated first.</summary>
    IReadOnlyList<FlowSummary> List();

    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    Flow Get(string id);

    /// <summary>Creates a disabled flow with an empty definition.</summary>
    /// <exception cref="ArgumentException">Blank or too long name, too long description (400).</exception>
    Flow Create(CreateFlowRequest request);

    /// <summary>Replaces name, description and definition; keeps id, enabled state and created time.</summary>
    /// <exception cref="ArgumentException">Invalid name/description/definition (400).</exception>
    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    Flow Update(string id, UpdateFlowRequest request);

    /// <summary>Deletes a flow only when <paramref name="confirmName"/> equals the flow's name exactly.</summary>
    /// <exception cref="ArgumentException">Confirmation missing or not an exact match (400).</exception>
    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    void Delete(string id, string? confirmName);

    /// <summary>Sets the enabled flag.
    /// TODO(run engine): enabling does not activate triggers yet; decide what "active" means once flows can run.</summary>
    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    Flow SetEnabled(string id, bool enabled);

    /// <summary>Copies a flow as "NAME (copy)", disabled.</summary>
    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    Flow Duplicate(string id);

    /// <summary>Builds the portable document for a flow (see <see cref="FlowDocument"/>).</summary>
    /// <exception cref="KeyNotFoundException">No flow with that id (404).</exception>
    FlowDocument Export(string id);

    /// <summary>Creates a new, disabled flow from a document (new id and timestamps).</summary>
    /// <exception cref="ArgumentException">Wrong format marker, unsupported (newer) version, or invalid content (400).</exception>
    Flow Import(FlowDocument document);
}
