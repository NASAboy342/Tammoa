using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Runs a command line through the OS shell. Wrapped behind an interface so cards can be tested without spawning processes.</summary>
public interface IProcessRunner
{
    /// <summary>Output beyond the cap is dropped (<see cref="ProcessResult.Truncated"/>). A timeout kills the process tree.</summary>
    /// <exception cref="OperationCanceledException">Cancelled; the process tree was killed.</exception>
    Task<ProcessResult> RunAsync(string command, string workingDirectory, string? stdin, TimeSpan timeout, CancellationToken ct);
}
