namespace Tammoa.Enums;

/// <summary>How the UI renders a card parameter. The UI has no card-specific code: it draws these.
/// Select = fixed or dynamic options; Code = code editor; Connection = a saved connection of a provider.</summary>
public enum ParamKind { Text, Textarea, Number, Bool, Select, Code, Connection }
