namespace Tammoa.Enums;

/// <summary>How the UI renders a card parameter. The UI has no card-specific code: it draws these.
/// Select = fixed or dynamic options; Code = code editor; Connection = a saved connection of a provider;
/// Stored = text kept by the backend OUTSIDE the flow definition (edited and saved on its own, e.g. the text of a Data card).</summary>
public enum ParamKind { Text, Textarea, Number, Bool, Select, Code, Connection, Stored }
