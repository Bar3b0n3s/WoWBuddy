using System.Runtime.InteropServices;

namespace WoWBuddy.Core.Memory;

/// <summary>
/// The Win32 surface the bot uses. Deliberately tiny.
/// </summary>
/// <remarks>
/// The bot only ever opens the one game process the user chose, and only for reading (phase
/// 1) and later for writing its click-to-move block and injected payload. No other process
/// on the machine is touched.
/// </remarks>
internal static partial class NativeMethods
{
    /// <summary>Read access to another process's address space.</summary>
    internal const int ProcessVmRead = 0x0010;

    /// <summary>Write access, needed from phase 2 for click-to-move and injection.</summary>
    internal const int ProcessVmWrite = 0x0020;

    /// <summary>Required alongside write access to change page protection.</summary>
    internal const int ProcessVmOperation = 0x0008;

    /// <summary>Lets the bot poll whether the client is still running.</summary>
    internal const int ProcessQueryInformation = 0x0400;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadProcessMemory(
        nint process,
        nint baseAddress,
        ref byte buffer,
        nint size,
        out nint bytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process(nint process, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
}
