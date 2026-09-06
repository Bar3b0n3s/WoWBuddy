using WoWBuddy.Core.Memory;

namespace WoWBuddy.Core.Client;

/// <summary>
/// Narrow bridge exposing the few Win32 calls the process layer needs without widening
/// the visibility of <see cref="NativeMethods"/> itself.
/// </summary>
internal static class NativeMethodsBridge
{
    internal static nint OpenForQuery(int processId) =>
        NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation, false, processId);

    internal static bool IsWow64(nint handle, out bool isWow64) =>
        NativeMethods.IsWow64Process(handle, out isWow64);

    internal static void Close(nint handle) => NativeMethods.CloseHandle(handle);
}
