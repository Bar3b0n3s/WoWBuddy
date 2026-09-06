using System.Reflection;
using System.Runtime.Loader;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Plugins;

/// <summary>What loading a folder of plugins produced.</summary>
/// <param name="Loaded">The plugins that started, running or faulted.</param>
/// <param name="Problems">Files that could not be used, and why.</param>
public sealed record PluginLoadResult(
    IReadOnlyList<LoadedPlugin> Loaded,
    IReadOnlyList<string> Problems);

/// <summary>
/// Finds plugin assemblies on disk and hands them to the manager.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read what you load.</b> A plugin is an assembly loaded into this process, and it can do
/// anything the bot can: read and write the game's memory, read and write your files, open
/// network connections. There is no sandbox here and there cannot be a real one — .NET removed
/// code access security, and anything this loader did to restrict a plugin could be undone by
/// the plugin. The one thing it does do is refuse to look outside the plugins folder, which
/// stops a profile or a config file naming an assembly somewhere else.
/// </para>
/// <para>
/// Each plugin gets its own <see cref="AssemblyLoadContext"/>, which keeps two plugins that
/// depend on different versions of the same library from fighting, and makes unloading during
/// development possible.
/// </para>
/// </remarks>
public sealed class PluginLoader(PluginManager manager)
{
    private readonly PluginManager _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    private readonly List<AssemblyLoadContext> _contexts = [];

    /// <summary>Loads every plugin in a folder.</summary>
    /// <param name="directory">The plugins folder. A missing one is not an error.</param>
    /// <param name="routines">Given each plugin's assembly, so a plugin can ship routines.</param>
    public PluginLoadResult LoadDirectory(string directory, RoutineCatalogue? routines = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        List<LoadedPlugin> loaded = [];
        List<string> problems = [];

        if (!Directory.Exists(directory))
        {
            // Not a problem worth reporting: most users have no plugins.
            Log.For<PluginLoader>().Debug("No plugin folder at {Directory}", directory);
            return new PluginLoadResult(loaded, problems);
        }

        string full = Path.GetFullPath(directory);

        foreach (string file in Directory.EnumerateFiles(full, "*.dll", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            // Belt and braces against a symlink or a "..\..\" that escapes the folder. A
            // plugin is trusted once loaded, so the only thing worth guarding is which files
            // get to become one.
            if (!Path.GetFullPath(file).StartsWith(full, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{file} is outside the plugin folder and was not loaded.");
                continue;
            }

            LoadFile(file, loaded, problems, routines);
        }

        return new PluginLoadResult(loaded, problems);
    }

    private void LoadFile(
        string file,
        List<LoadedPlugin> loaded,
        List<string> problems,
        RoutineCatalogue? routines)
    {
        Assembly assembly;

        try
        {
            AssemblyLoadContext context = new(Path.GetFileNameWithoutExtension(file), isCollectible: true);
            _contexts.Add(context);

            assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
        }
        catch (BadImageFormatException)
        {
            // A native DLL a plugin depends on, most likely. Not something to complain about.
            Log.For<PluginLoader>().Debug("{File} is not a .NET assembly; skipped", file);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Path.GetFileName(file)} could not be read: {exception.Message}");
            return;
        }

        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Usually a plugin built against a different version of the bot. Say which, because
            // "it did not load" is not something a user can act on.
            problems.Add(
                $"{Path.GetFileName(file)} could not be read. It may have been built against a "
                + $"different version of WoWBuddy: {exception.LoaderExceptions.FirstOrDefault()?.Message}");
            return;
        }

        bool found = false;

        foreach (Type type in types)
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            found = true;

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add(
                    $"{type.FullName} in {Path.GetFileName(file)} is a plugin but has no "
                    + "parameterless constructor, so it could not be created.");
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is IPlugin plugin)
                {
                    loaded.Add(_manager.Add(plugin, Path.GetFileName(file)));
                }
            }
            catch (Exception exception)
            {
                problems.Add($"{type.FullName} threw while being created: {exception.Message}");
            }
        }

        // An assembly with no plugin in it may still be worth having: a plugin can ship
        // combat routines and nothing else.
        int added = routines?.Add(assembly) ?? 0;

        if (!found && added == 0)
        {
            Log.For<PluginLoader>().Debug(
                "{File} contains no plugins or routines; it is probably a dependency", file);
        }
    }
}
