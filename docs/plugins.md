# Plugins

A plugin is a .NET assembly the bot loads and runs. It can add combat routines, provide the
behaviours a profile's `CustomBehavior` steps name, and do work of its own on every tick.

## Read what you load

**A plugin is code, and it is as trusted as the bot itself.** It runs in the bot's process. It
can read and write the game's memory, read and write your files, and open network connections.

There is no sandbox, and there cannot be a real one: .NET removed code access security years
ago, and anything this project did to restrict a plugin could be undone by the plugin. The
loader does one thing — it refuses to look outside the plugins folder, so a profile or a config
file cannot name an assembly somewhere else — and that is the limit of what is technically
enforceable.

So the protection is social. Read what you load, or do not load it.

This is the opposite of how profiles work, deliberately. A profile is data: a fixed vocabulary
of steps and a seven-term condition language, with no way to execute anything. Running a profile
you downloaded is a small act of trust. Running a plugin you downloaded is not.

## Writing one

Implement `IPlugin`:

```csharp
public sealed class MyPlugin : IPlugin
{
    public string Name => "My Plugin";
    public string Author => "me";
    public Version Version => new(1, 0);
    public string Description => "does a thing";

    public void Initialise(IPluginHost host)
    {
        host.Log("started");
    }

    public void Pulse(IBotState state)
    {
        // Cheap, and never blocking: this runs on the tick that also drives combat.
    }
}
```

Drop the assembly in the `Plugins` folder next to the bot. It needs a public, concrete class
with a parameterless constructor; anything else is reported rather than skipped silently,
because a plugin author whose plugin does not appear deserves to know why.

`IPluginHost` is deliberately small — the routine catalogue, a folder of the plugin's own, and a
log. Everything a plugin is meant to reach goes through it rather than through statics, so what
a plugin was handed can be read in a minute.

## A broken plugin costs its own feature, not the session

Every call into a plugin is guarded. A plugin that throws while starting is kept in a faulted
state so you can see it failed and why, rather than quietly not being there. A plugin that
throws on a tick is forgiven a few times — a moment of odd state, a loading screen, a target
that vanished — and then switched off, with a line saying so.

An unattended bot that dies at 2am because someone's experimental plugin threw a null reference
is a worse outcome than one that logs the fault, switches that plugin off, and carries on
grinding.

## Contributing routines

A plugin's assembly is offered to `RoutineCatalogue`, so a plugin that ships a combat routine
and nothing else works: no `IPlugin` implementation is needed for that. The catalogue finds
routines by looking at the assembly, so there is no registration step.

## Contributing behaviours

`IPlugin.Behaviors` returns behaviours by name, which is how a profile's `CustomBehavior` step
gets something to run — including an imported Honorbuddy profile that names a behaviour only
that bot had. Write one under the same name and the questing base finds it.

Where two plugins offer the same name, the first loaded keeps it and the clash is reported.
Taking one silently would make a profile behave differently depending on the order files
happened to be read in.

## Assembly load contexts

Each plugin gets its own `AssemblyLoadContext`, which keeps two plugins that depend on
different versions of the same library from fighting, and makes unloading during development
possible.
