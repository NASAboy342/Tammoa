using System.Diagnostics;

namespace Tammoa.Services;

public class FolderOpener : IFolderOpener
{
    public void Open(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false };
            info.ArgumentList.Add(folder); // an argument, never a command line: the path cannot add anything to it
            using var _ = Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not open the folder. It is here: {folder}");
        }
    }
}
