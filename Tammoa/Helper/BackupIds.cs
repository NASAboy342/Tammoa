namespace Tammoa.Helper;

/// <summary>Ids that come from a backup file are checked before they are used (they end up in file paths and in the database).</summary>
public static class BackupIds
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
