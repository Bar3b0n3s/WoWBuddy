using System;
using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Presentation;
using WoWBuddy.Profiles;

namespace WoWBuddy.UI;

/// <summary>
/// Attaching to a client, and what the window can do with one.
/// </summary>
/// <remarks>
/// <para>
/// Attaching, verifying the offset table and reading the character are real and work today.
/// Starting is not, and this class says so rather than appearing to run.
/// </para>
/// <para>
/// <b>What is missing is the live <see cref="IBotState"/>.</b> Everything above this line —
/// six bot bases, thirty combat routines, the behaviour tree, profiles, group play — is written
/// against that interface and tested against a fake implementation of it. Nothing yet implements
/// it against a running client: doing so means reading the quest log, the party, the bags and
/// the battleground queue out of a real 3.3.5a client, and most of that is Lua this project has
/// not verified. It is the next piece of work, and it is deliberately not faked here. A start
/// button that ticked a tree fed on invented values would look like progress and be worth less
/// than nothing.
/// </para>
/// </remarks>
public sealed class BotController : IBotController
{
    private GameClient? _client;
    private World? _world;
    private Node<IBotState>? _tree;

    /// <inheritdoc />
    public bool IsAttached => _client is not null;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public bool CanExecute => _client?.Execution is { IsUsable: true };

    /// <inheritdoc />
    public string ClientCapabilities { get; private set; } = string.Empty;

    /// <inheritdoc />
    public string CharacterSummary
    {
        get
        {
            if (_world?.Me is not { } me)
            {
                return string.Empty;
            }

            return $"{me.Name} — level {me.Level} {me.Class}, {me.HealthPercent:F0}% health";
        }
    }

    /// <inheritdoc />
    public AttachOutcome Attach(ClientOption client)
    {
        Detach();

        // Attaching by id rather than by handing over a Process: the core layer already deals
        // with a process that has since exited, and there is no reason to do it twice.
        AttachResult result = GameClient.Attach(client.ProcessId);

        if (!result.Success)
        {
            return new AttachOutcome(
                false,
                result.Report.ToString(),
                "Attach failed. See the verification report.");
        }

        _client = result.Client;
        _world = new World(_client!);

        return new AttachOutcome(true, result.Report.ToString(), "Attached.");
    }

    /// <summary>
    /// Installs the execution hook, proves the Lua round trip, and asks what the client can do.
    /// </summary>
    /// <remarks>
    /// Three steps in one because the second and third are only meaningful after the first, and
    /// each refuses rather than continuing: a hook that did not install means no Lua, a Lua
    /// round trip that did not prove itself means every capability answer would be a guess, and
    /// a capability that is missing means a feature is switched off rather than left to fail
    /// four hours in.
    /// </remarks>
    public string EnableExecution()
    {
        if (_client is null)
        {
            return "Attach to a client first.";
        }

        ExecutionInstallResult install = _client.EnableExecution();

        if (!install.Success)
        {
            ClientCapabilities = install.Report.ToString();
            return $"Could not install the execution hook. {install.FailureReason}";
        }

        LuaBridge lua = install.Session!.Lua;

        if (!lua.SelfTest(out string detail))
        {
            ClientCapabilities = $"The Lua round trip could not be proved against this client: {detail}";
            return "Execution installed, but Lua could not be proved. The bot will not act on the game.";
        }

        CapabilityReport report = CapabilityProbes.Probe(lua);
        ClientCapabilities = report.Describe();

        return report.NothingUnexpected
            ? "Execution enabled."
            : "Execution enabled, but this client is missing calls the bot expected. Read the "
                + "capabilities report before running anything.";
    }

    /// <inheritdoc />
    public void Detach()
    {
        Stop();

        ClientCapabilities = string.Empty;
        _world = null;
        _client?.Dispose();
        _client = null;
    }

    /// <inheritdoc />
    public string Start(string botBase, string routine, string? profilePath)
    {
        if (_client is null)
        {
            return "Attach to a client first.";
        }

        if (!CanExecute)
        {
            return "Enable execution first.";
        }

        Profile? profile = null;

        if (!string.IsNullOrEmpty(profilePath))
        {
            ProfileLoadResult loaded = ProfileLoader.LoadFile(profilePath);

            if (!loaded.Success)
            {
                return "That profile cannot be used. See the profile report.";
            }

            profile = loaded.Profile;
        }

        // Building the tree is real, and worth doing here: it is where a profile that does not
        // give a bot base what it needs is caught, and the message says which.
        BotBaseBuild built = BotBaseFactory.Create(botBase, profile);

        if (!built.Success)
        {
            return built.Message;
        }

        _tree = built.Tree;

        return built.Message
            + "  The tree is built, but the bot cannot play yet: nothing implements IBotState "
            + "against a live client. See docs/architecture.md.";
    }

    /// <inheritdoc />
    public void Stop()
    {
        IsRunning = false;
        _tree = null;
    }
}
