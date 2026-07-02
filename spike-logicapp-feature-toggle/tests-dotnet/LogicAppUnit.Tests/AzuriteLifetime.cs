using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Starts Azurite once before any test in the assembly runs, then stops it on teardown.
/// Skips startup when Azurite is already listening on port 10000 (e.g. CI pre-starts it).
/// On Windows, npm installs azurite as a .cmd shim; cmd /c resolves it from PATH.
/// </summary>
[TestClass]
public static class AzuriteLifetime
{
    private const int BlobPort = 10000;
    private const int StartupTimeoutSeconds = 15;

    private static Process? _process;
    private static bool _weStartedIt;

    [AssemblyInitialize]
    public static void StartAzurite(TestContext _)
    {
        EnsureToolsOnPath();

        if (IsListening(BlobPort))
            return; // already running – reuse it

        var location = Path.Combine(Path.GetTempPath(), "azurite-logicappunit");

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c azurite --silent --location \"{location}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException(
                "Could not launch Azurite. Install it with:  npm install -g azurite");

        _weStartedIt = true;
        WaitUntilReady();
    }

    [AssemblyCleanup]
    public static void StopAzurite()
    {
        if (!_weStartedIt || _process is null)
            return;

        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* best-effort */ }
        finally { _process.Dispose(); _process = null; }
    }

    private static void WaitUntilReady()
    {
        var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);
        while (!IsListening(BlobPort))
        {
            if (_process!.HasExited)
                throw new InvalidOperationException(
                    $"Azurite exited (code {_process.ExitCode}). " +
                    "Run 'azurite' in a terminal to see the startup error.");

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Azurite port {BlobPort} not ready within {StartupTimeoutSeconds} s.");

            Thread.Sleep(250);
        }
    }

    private static bool IsListening(int port)
    {
        try { using var t = new TcpClient(); t.Connect("127.0.0.1", port); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Prepends the npm global bin directory (azurite) and Azure Functions Core Tools
    /// (func.exe) to the current process PATH so both this Azurite launcher and the
    /// LogicAppUnit WorkflowTestHost can find the required tools without requiring the
    /// user to have them in their system PATH.
    /// </summary>
    private static void EnsureToolsOnPath()
    {
        var candidates = new[]
        {
            // npm global bin – where `npm install -g azurite` puts azurite.cmd
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm"),
            // Azure Functions Core Tools default install location
            @"C:\Program Files\Microsoft\Azure Functions Core Tools",
        };

        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var toAdd = candidates
            .Where(p => Directory.Exists(p) && !current.Contains(p, StringComparison.OrdinalIgnoreCase));

        var prepend = string.Join(Path.PathSeparator.ToString(), toAdd);
        if (!string.IsNullOrEmpty(prepend))
            Environment.SetEnvironmentVariable("PATH", prepend + Path.PathSeparator + current);
    }
}