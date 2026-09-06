# Setup

## Requirements

- Windows 10 or 11.
- .NET 8 SDK (to build) or the .NET 8 Desktop Runtime (to run a release build).
- World of Warcraft 3.3.5a, **build 12340**. Verify with right-click `WoW.exe` → Properties
  → Details: the file version must read `3.3.5.12340`.

## Build

```
git clone https://github.com/Bar3b0n3s/WoWBuddy.git
cd WoWBuddy
dotnet build WoWBuddy.sln -c Release
```

On a non-Windows machine, build the portable subset instead. The WPF shell needs the Windows
Desktop SDK and will not build elsewhere:

```
dotnet build WoWBuddy.NoUI.slnf -c Release
dotnet test WoWBuddy.NoUI.slnf -c Release
```

## First run

1. Start WoW and **log a character all the way into the world.** The object manager does not
   exist at the character-select screen.
2. Run the inspector to confirm the offsets match your client:
   ```
   dotnet run --project tools/WoWBuddy.Inspector -c Release -- inspect
   ```
   Work through [phase-1-manual-test.md](phase-1-manual-test.md) if anything fails.
3. Or start the desktop shell, pick the client, and press Attach:
   ```
   dotnet run --project src/UI -c Release
   ```

## Privileges

WoWBuddy runs as an ordinary user and deliberately does **not** request administrator rights.
Opening a process you started yourself for reading needs no elevation. If attach fails with a
Win32 access error, the usual cause is that the game was started elevated and the bot was
not; start both the same way rather than elevating the bot.

## What the bot touches

- The one game process you selected. Reads only, in phase 1.
- A `logs` folder beside the executable.
- `%APPDATA%\WoWBuddy` for settings.

It makes no network connections, sends no telemetry, and reads no other process.

## Navigation data (phase 3, not yet needed)

Pathfinding will need `mmaps` generated from **your own** game client using the TrinityCore or
AzerothCore extractors. No such data ships with this repository, and none may ever be
committed to it. Instructions will arrive with phase 3.
