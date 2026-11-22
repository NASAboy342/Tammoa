using System.Net;
using System.Net.Sockets;

namespace Tammoa.Helper;

/// <summary>Picks the port the dev API listens on: the preferred one (8000, or TAMMOA_PORT) when it is free, otherwise up to
/// <see cref="RandomTries"/> random ports, otherwise it gives up with a clear message (never loops forever).
/// The chosen port is written to a small file in the temp folder so the Vite dev proxy can find it (see UserInterface/vite.config.ts).</summary>
public static class PortFinder
{
    public const int DefaultPort = 8000;
    public const int RandomTries = 5;
    public const string PortEnvVar = "TAMMOA_PORT";
    public const string PortFileName = "tammoa-api-port";
    private const int RandomFrom = 49152, RandomTo = 65535; // the dynamic / private port range

    /// <summary>The preferred port from an environment value; anything missing or not a usable port number means the default.</summary>
    public static int PreferredFrom(string? value) => int.TryParse(value?.Trim(), out var p) && p is >= 1024 and <= 65535 ? p : DefaultPort;

    /// <exception cref="InvalidOperationException">The preferred port and every random port tried were in use.</exception>
    public static int Choose(int preferred, Func<int, bool>? isFree = null, Func<int>? randomPort = null)
    {
        isFree ??= IsFree;
        randomPort ??= () => Random.Shared.Next(RandomFrom, RandomTo + 1);
        if (isFree(preferred)) return preferred;
        var tried = new HashSet<int> { preferred };
        for (var i = 0; i < RandomTries; i++)
        {
            int port;
            do port = randomPort(); while (!tried.Add(port) && tried.Count < 1000); // never retry the same number
            if (isFree(port)) return port;
        }
        throw new InvalidOperationException($"No free port: {preferred} and {RandomTries} random ports were all in use. Close the program that holds them, or set {PortEnvVar}.");
    }

    /// <summary>True when something can listen on the port on this machine's loopback right now.</summary>
    public static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static string PortFile() => Path.Combine(Path.GetTempPath(), PortFileName);

    public static void WritePortFile(string file, int port) => File.WriteAllText(file, port.ToString());
}
