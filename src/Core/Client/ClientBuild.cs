using System.Diagnostics;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Client;

/// <summary>
/// What the bot could determine about a candidate client's version.
/// </summary>
/// <param name="Version">Version string reported by the executable, if any.</param>
/// <param name="Build">Build number, or null when it could not be determined.</param>
/// <param name="Is32Bit">Whether the process is 32-bit, as 3.3.5a must be.</param>
/// <param name="Source">How the build was determined, for the log and the UI.</param>
public readonly record struct ClientBuild(string Version, int? Build, bool Is32Bit, string Source)
{
    /// <summary>True when this is the 3.3.5a build the offset table describes.</summary>
    public bool IsSupported => Build == Offsets335a.SupportedBuild && Is32Bit;

    /// <summary>
    /// True when the build could not be determined at all. Private-server launchers
    /// sometimes strip version resources, so this is not automatically disqualifying,
    /// but it does mean the user has to confirm the client themselves.
    /// </summary>
    public bool IsUnknown => Build is null;

    public override string ToString() =>
        Build is null
            ? $"unknown build ({Source})"
            : $"{Version} build {Build} ({Source})";
}

/// <summary>
/// Determines a client executable's build number.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does not read the build out of process memory. The address of the version
/// string is itself an offset that would have to be verified, and using an unverified offset
/// to decide whether the offsets are right is circular. The executable's own version
/// resource is authoritative, needs no offset, and is what Windows itself reports.
/// </para>
/// </remarks>
public static class ClientBuildDetector
{
    /// <summary>
    /// Reads the build from <paramref name="process"/>'s main module version resource.
    /// </summary>
    public static ClientBuild Detect(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        bool is32Bit = IsProcess32Bit(process);

        try
        {
            ProcessModule? main = process.MainModule;
            if (main is null)
            {
                return new ClientBuild(string.Empty, null, is32Bit, "no main module");
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(main.FileName);

            // 3.3.5a reports a file version of 3.3.5.12340: the private build part is the
            // build number the community identifies the client by.
            if (info.FileMajorPart != 0 || info.FilePrivatePart != 0)
            {
                string version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
                return new ClientBuild(version, info.FilePrivatePart, is32Bit, "file version resource");
            }

            return new ClientBuild(info.FileVersion ?? string.Empty, null, is32Bit, "version resource had no build");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Reading the main module of a process at a different bitness, or one that just
            // exited, throws. Neither is worth failing the whole scan over.
            return new ClientBuild(string.Empty, null, is32Bit, "module not readable");
        }
    }

    private static bool IsProcess32Bit(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return true;
        }

        try
        {
            nint handle = NativeMethodsBridge.OpenForQuery(process.Id);
            if (handle == 0)
            {
                // Cannot tell; assume it is fine and let the deeper checks decide.
                return true;
            }

            try
            {
                return NativeMethodsBridge.IsWow64(handle, out bool wow64) && wow64;
            }
            finally
            {
                NativeMethodsBridge.Close(handle);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return true;
        }
    }
}
