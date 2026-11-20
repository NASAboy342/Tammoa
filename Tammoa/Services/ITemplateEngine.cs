using Newtonsoft.Json.Linq;

namespace Tammoa.Services;

/// <summary>Resolves {{ path | filter }} templates against a JSON context.
/// Paths use dots and [n] indexes (input.items[0].name). Missing paths give empty text (null when the whole value is one expression).
/// Filters: json (compact JSON text), sh (POSIX single-quote the text; TODO: Windows cmd quoting).</summary>
public interface ITemplateEngine
{
    /// <summary>Text with every {{ }} replaced by its value as text.</summary>
    /// <exception cref="ArgumentException">Unknown filter.</exception>
    string Render(string template, JToken context);

    /// <summary>Resolves a whole JSON value. A string that is exactly one {{ }} (no filter) keeps the value's type.</summary>
    /// <exception cref="ArgumentException">Unknown filter.</exception>
    JToken Resolve(JToken template, JToken context);
}
