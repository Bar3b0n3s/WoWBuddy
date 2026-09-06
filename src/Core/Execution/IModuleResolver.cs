namespace WoWBuddy.Core.Execution;

/// <summary>
/// Answers which loaded module an address belongs to in the attached client.
/// </summary>
/// <remarks>
/// Used for one thing: confirming that the function about to be hooked really is the
/// Direct3D one. The pointer chain that finds EndScene is three dereferences deep, and if any
/// hop is wrong the result is a plausible-looking address that is not EndScene at all.
/// Redirecting that would corrupt the client in a way that is very hard to diagnose, so the
/// hook refuses to install unless the target lands where Direct3D actually lives.
/// </remarks>
public interface IModuleResolver
{
    /// <summary>
    /// The file name of the module containing <paramref name="address"/>, or null when the
    /// address is not inside any loaded module.
    /// </summary>
    string? ModuleContaining(nint address);
}

/// <summary>Resolves modules of a real process.</summary>
public sealed class ProcessModuleResolver : IModuleResolver
{
    private readonly List<(nint Start, nint End, string Name)> _modules = [];

    /// <summary>Snapshots the module list of <paramref name="process"/>.</summary>
    /// <remarks>
    /// A snapshot rather than a live query. The check happens once, at hook install, and
    /// walking the module list is slow enough that doing it repeatedly would be wasteful.
    /// </remarks>
    public ProcessModuleResolver(System.Diagnostics.Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            foreach (System.Diagnostics.ProcessModule module in process.Modules)
            {
                _modules.Add((
                    module.BaseAddress,
                    module.BaseAddress + module.ModuleMemorySize,
                    module.ModuleName));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited, or its module list is not readable. An empty table makes
            // every check fail, which is the safe direction: the hook will refuse to install.
        }
    }

    /// <inheritdoc />
    public string? ModuleContaining(nint address)
    {
        foreach ((nint start, nint end, string name) in _modules)
        {
            if (address >= start && address < end)
            {
                return name;
            }
        }

        return null;
    }
}
