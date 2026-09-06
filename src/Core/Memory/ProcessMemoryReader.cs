using System.Diagnostics;
using System.Runtime.InteropServices;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Core.Memory;

/// <summary>
/// Reads the game client's memory through <c>ReadProcessMemory</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reading is the whole of phase 1: nothing is written and nothing is injected, so attaching
/// alone cannot destabilise the client. The writing half of <see cref="IProcessMemory"/> is
/// used only by the phase 2 execution layer, and only after the offset table has been
/// verified against this specific client.
/// </para>
/// <para>
/// Failed reads are counted rather than logged individually. A bad pointer during a list
/// walk is expected several times a second on a busy screen, and logging each one would bury
/// everything else. A sustained failure rate is what actually matters, and
/// <see cref="FailedReadCount"/> makes that visible.
/// </para>
/// </remarks>
public sealed class ProcessMemoryReader : IProcessMemory, IDisposable
{
    private readonly Process _process;
    private readonly bool _ownsProcess;
    private nint _handle;
    private long _failedReads;
    private bool _disposed;

    private ProcessMemoryReader(Process process, nint handle, nint moduleBase, int moduleSize, bool ownsProcess)
    {
        _process = process;
        _handle = handle;
        _ownsProcess = ownsProcess;
        ModuleBase = moduleBase;
        ModuleSize = moduleSize;
    }

    /// <inheritdoc />
    public nint ModuleBase { get; }

    /// <inheritdoc />
    public int ModuleSize { get; }

    /// <summary>Process id of the attached client.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Number of reads that failed since attach.</summary>
    public long FailedReadCount => Interlocked.Read(ref _failedReads);

    /// <inheritdoc />
    public bool IsValid
    {
        get
        {
            if (_disposed || _handle == 0)
            {
                return false;
            }

            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="process"/> for reading.
    /// </summary>
    /// <param name="process">The client process. Not disposed by this reader unless <paramref name="ownsProcess"/>.</param>
    /// <param name="ownsProcess">Whether disposing the reader should also dispose the process object.</param>
    /// <exception cref="MemoryAccessException">
    /// The process could not be opened, which in practice means the bot is not running elevated
    /// enough for the client, or the client exited between being found and being opened.
    /// </exception>
    public static ProcessMemoryReader Open(Process process, bool ownsProcess = false)
    {
        ArgumentNullException.ThrowIfNull(process);

        const int access = NativeMethods.ProcessVmRead
            | NativeMethods.ProcessVmWrite
            | NativeMethods.ProcessVmOperation
            | NativeMethods.ProcessQueryInformation;

        nint handle = NativeMethods.OpenProcess(access, false, process.Id);
        if (handle == 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new MemoryAccessException(
                $"Could not open the game process (pid {process.Id}) for reading. Win32 error {error}. " +
                "Run WoWBuddy with the same or higher privileges than the game client.");
        }

        nint moduleBase;
        int moduleSize;
        try
        {
            ProcessModule main = process.MainModule
                ?? throw new MemoryAccessException("The game process reported no main module.");
            moduleBase = main.BaseAddress;
            moduleSize = main.ModuleMemorySize;
        }
        catch (Exception ex) when (ex is not MemoryAccessException)
        {
            NativeMethods.CloseHandle(handle);
            throw new MemoryAccessException("Could not read the game process's main module.", ex);
        }

        Log.For<ProcessMemoryReader>().Information(
            "Opened game process {Pid} (module base 0x{Base:X8}, size 0x{Size:X})",
            process.Id, moduleBase, moduleSize);

        return new ProcessMemoryReader(process, handle, moduleBase, moduleSize, ownsProcess);
    }

    /// <inheritdoc />
    public bool TryReadBytes(nint address, Span<byte> buffer)
    {
        if (_disposed || _handle == 0 || buffer.IsEmpty)
        {
            return false;
        }

        // A null or near-null address is the usual shape of a stale pointer. Rejecting it
        // here avoids a syscall on the hottest path in the bot.
        if (address <= 0x1000)
        {
            Interlocked.Increment(ref _failedReads);
            return false;
        }

        bool ok = NativeMethods.ReadProcessMemory(
            _handle, address, ref MemoryMarshal.GetReference(buffer), buffer.Length, out nint read);

        if (!ok || read != buffer.Length)
        {
            Interlocked.Increment(ref _failedReads);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryWriteBytes(nint address, ReadOnlySpan<byte> buffer)
    {
        if (_disposed || _handle == 0 || buffer.IsEmpty || address <= 0x1000)
        {
            return false;
        }

        bool ok = NativeMethods.WriteProcessMemory(
            _handle,
            address,
            ref MemoryMarshal.GetReference(buffer),
            buffer.Length,
            out nint written);

        if (!ok || written != buffer.Length)
        {
            Log.For<ProcessMemoryReader>().Error(
                "Failed to write {Count} byte(s) at 0x{Address:X8} (Win32 error {Error})",
                buffer.Length, address, Marshal.GetLastWin32Error());
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public nint Allocate(int size, bool executable)
    {
        if (_disposed || _handle == 0 || size <= 0)
        {
            return 0;
        }

        int protect = executable ? NativeMethods.PageExecuteReadWrite : NativeMethods.PageReadWrite;

        nint address = NativeMethods.VirtualAllocEx(
            _handle,
            0,
            size,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            protect);

        if (address == 0)
        {
            Log.For<ProcessMemoryReader>().Error(
                "Failed to allocate {Size} bytes in the client (Win32 error {Error})",
                size, Marshal.GetLastWin32Error());
            return 0;
        }

        Log.For<ProcessMemoryReader>().Debug(
            "Allocated {Size} bytes at 0x{Address:X8} in the client ({Protection})",
            size, address, executable ? "RWX" : "RW");

        return address;
    }

    /// <inheritdoc />
    public bool Free(nint address)
    {
        if (_disposed || _handle == 0 || address == 0)
        {
            return false;
        }

        // MEM_RELEASE requires a size of zero and frees the whole reservation.
        return NativeMethods.VirtualFreeEx(_handle, address, 0, NativeMethods.MemRelease);
    }

    /// <inheritdoc />
    public bool WithWritableMemory(nint address, int size, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_disposed || _handle == 0 || size <= 0)
        {
            return false;
        }

        if (!NativeMethods.VirtualProtectEx(
                _handle, address, size, NativeMethods.PageReadWrite, out int original))
        {
            Log.For<ProcessMemoryReader>().Error(
                "Could not make 0x{Address:X8} writable (Win32 error {Error})",
                address, Marshal.GetLastWin32Error());
            return false;
        }

        try
        {
            action();
        }
        finally
        {
            // Always restore, even if the action threw. Leaving a page in the client
            // writable because of an exception on our side would be inexcusable.
            NativeMethods.VirtualProtectEx(_handle, address, size, original, out _);
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != 0)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = 0;
        }

        if (_ownsProcess)
        {
            _process.Dispose();
        }
    }
}
