namespace Tammoa.Enums;

/// <summary>How the UI renders a card parameter. The UI has no card-specific code: it draws these.
/// Select = fixed or dynamic options; Code = code editor; Connection = a saved connection of a provider;
/// MultiSelect = several of the fixed options (value: an array of option values); Stored = text kept by the backend OUTSIDE the flow definition (edited and saved on its own, e.g. the text of a Data card).</summary>
public enum ParamKind { Text, Textarea, Number, Bool, Select, Code, Connection, Stored, MultiSelect }
