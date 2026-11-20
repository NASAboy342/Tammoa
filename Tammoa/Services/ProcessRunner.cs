using System.Diagnostics;
using System.Text;
using Tammoa.Models;

namespace Tammoa.Services;

public class ProcessRunner : IProcessRunner
{
    public const int OutputCap = 64 * 1024;

    public async Task<ProcessResult> RunAsync(string command, string workingDirectory, string? stdin, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = ReadCappedAsync(process.StandardOutput);
        var stderr = ReadCappedAsync(process.StandardError);
        try
        {
            if (!string.IsNullOrEmpty(stdin)) await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }
        catch (IOException) { /* the command did not read its input */ }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested) throw;
            timedOut = true;
        }

        var (outText, outTruncated) = await stdout;
        var (errText, errTruncated) = await stderr;
        return new ProcessResult(timedOut ? -1 : process.ExitCode, outText, errText, timedOut, outTruncated || errTruncated);
    }

    private static void Kill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var room = OutputCap - sb.Length;
            if (room <= 0) { truncated = true; continue; } // keep draining so the process never blocks on a full pipe
            if (n > room) { sb.Append(buffer, 0, room); truncated = true; }
            else sb.Append(buffer, 0, n);
        }
        return (sb.ToString(), truncated);
    }
}
