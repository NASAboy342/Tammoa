using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Reusable data kept by Data cards: plain text, edited by hand or written by a flow, readable from any card of the flow as {{data.Name}}.
/// A value that is a JSON object or list is available as data (so {{data.Settings.city}} works); anything else stays text.</summary>
public interface IDataService
{
    public const int MaxLength = 1024 * 1024;

    /// <summary>Data cards of a flow with a short preview.</summary>
    /// <exception cref="KeyNotFoundException">No such flow (404).</exception>
    IReadOnlyList<DataCardSummary> List(string flowId);

    /// <exception cref="KeyNotFoundException">No such flow, or the card is not a Data card of it (404).</exception>
    DataEntry Read(string flowId, string cardId);

    /// <summary>Saves the text of a Data card (an empty text clears it) and tells every open window.</summary>
    /// <exception cref="ArgumentException">Text longer than 1 MB (400).</exception>
    /// <exception cref="KeyNotFoundException">No such flow, or the card is not a Data card of it (404).</exception>
    DataEntry Write(string flowId, string cardId, string? text);

    // ---- for cards and the engine (the run already holds the definition, so no lookup is needed)
    DataEntry ReadAt(DataLocation location);
    DataEntry WriteAt(DataLocation location, string text);

    /// <summary>Name -> value of every Data card of the definition (right now: re-read for each card that starts, so a card that was
    /// updated earlier in the run is seen by the cards after it).</summary>
    JObject LoadContext(string flowId, JObject definition);

    /// <summary>Text -> value: a JSON object or list becomes data, anything else stays text.</summary>
    JToken ToValue(string text);

    // ---- flow lifecycle
    void DeleteFlowData(string flowId);
    void CopyFlowData(string fromFlowId, string toFlowId);

    /// <summary>cardId -> text of the flow's own (flow-scoped) Data cards, for export.</summary>
    JObject ExportFlowData(string flowId, JObject definition);

    /// <summary>name -> text of every shared Data text (for a backup).</summary>
    JObject ExportShared();

    /// <summary>Writes shared texts that do not exist yet (never overwrites); returns how many were added. Oversized or non-text values are ignored.</summary>
    int ImportShared(JObject? texts);

    /// <summary>Writes exported texts for the Data cards of the (new) flow; unknown cards and oversized texts are ignored.</summary>
    void ImportFlowData(string flowId, JObject definition, JObject? data);
}
