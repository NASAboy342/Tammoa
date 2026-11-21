namespace Tammoa.Helper;

/// <summary>Rules for the name of a Data card, which is how other cards refer to it: {{data.Name}}.</summary>
public static class DataNames
{
    public const int MaxLength = 60;
    private static readonly char[] Forbidden = ['.', '[', ']', '|', '{', '}'];

    /// <returns>null when the name is fine, otherwise why not.</returns>
    public static string? Problem(string? name)
    {
        var t = name?.Trim();
        if (string.IsNullOrEmpty(t)) return "A Data card needs a name: it is how other cards refer to it.";
        if (t.Length > MaxLength) return $"A Data card name is limited to {MaxLength} characters.";
        var bad = t.FirstOrDefault(c => Forbidden.Contains(c));
        return bad == default ? null : $"The name '{t}' contains '{bad}'. A reference like {{{{data.Name}}}} cannot contain . [ ] | {{ or }}.";
    }

    /// <summary>A file name for a shared Data card: lower case (so Notes and notes are one file on every OS) with unsafe characters replaced.</summary>
    public static string FileName(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray();
        var s = new string(chars).Trim();
        return (s.Length == 0 ? "_" : s) + ".txt";
    }
}
