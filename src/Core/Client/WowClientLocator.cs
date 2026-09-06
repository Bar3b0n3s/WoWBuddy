using System.Diagnostics;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Core.Client;

/// <summary>
/// A running game client the user could attach to.
/// </summary>
/// <param name="Process">The process itself.</param>
/// <param name="Build">What could be determined about its version.</param>
/// <param name="WindowTitle">Its main window title, which usually carries the character name.</param>
public sealed record WowClientCandidate(
    Process Process,
    ClientBuild Build,
    string WindowTitle)
{
    /// <summary>Process id, shown in the picker.</summary>
    public int ProcessId => Process.Id;

    /// <summary>A line describing this candidate for the UI's process picker.</summary>
    public string Describe() =>
        $"pid {ProcessId}  {Build}" +
        (string.IsNullOrWhiteSpace(WindowTitle) ? string.Empty : $"  \"{WindowTitle}\"");
}

/// <summary>
/// Finds running 3.3.5a clients.
/// </summary>
/// <remarks>
/// Several clients are commonly open at once (multiboxing, or one client per account), so
/// this returns every candidate and lets the user or the caller choose rather than guessing.
/// </remarks>
public static class WowClientLocator
{
    /// <summary>Process names 3.3.5a clients are distributed under.</summary>
    private static readonly string[] KnownProcessNames = ["Wow", "WoW", "Wow-64", "WowClassic", "run"];

    /// <summary>
    /// Returns every running process that looks like a WoW client, supported or not.
    /// Unsupported ones are included so the UI can explain why they were rejected instead
    /// of showing an empty list.
    /// </summary>
    public static IReadOnlyList<WowClientCandidate> FindAll()
    {
        var results = new List<WowClientCandidate>();

        foreach (Process process in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                if (!LooksLikeWow(process.ProcessName))
                {
                    continue;
                }

                ClientBuild build = ClientBuildDetector.Detect(process);
                string title = SafeWindowTitle(process);
                results.Add(new WowClientCandidate(process, build, title));
                keep = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process exited mid-scan, or is not ours to inspect. Skip it.
            }
            finally
            {
                if (!keep)
                {
                    process.Dispose();
                }
            }
        }

        Log.For(nameof(WowClientLocator)).Debug("Found {Count} candidate game client(s)", results.Count);
        return results;
    }

    /// <summary>Returns only the candidates whose build matches the supported one.</summary>
    public static IReadOnlyList<WowClientCandidate> FindSupported() =>
        FindAll().Where(c => c.Build.IsSupported).ToList();

    private static bool LooksLikeWow(string processName) =>
        KnownProcessNames.Any(known => string.Equals(known, processName, StringComparison.OrdinalIgnoreCase));

    private static string SafeWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }
}
