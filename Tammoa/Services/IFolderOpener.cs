namespace Tammoa.Services;

/// <summary>Shows a folder in the computer's file manager (Finder / Explorer).</summary>
public interface IFolderOpener
{
    /// <exception cref="InvalidOperationException">The folder could not be opened (with a readable reason).</exception>
    void Open(string folder);
}
