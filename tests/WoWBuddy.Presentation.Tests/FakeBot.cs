using WoWBuddy.Presentation;

namespace WoWBuddy.Presentation.Tests;

/// <summary>Client processes that are not running.</summary>
internal sealed class FakeDiscovery : IClientDiscovery
{
    public List<ClientOption> Found { get; } = [];

    public int Calls { get; private set; }

    public IReadOnlyList<ClientOption> Find()
    {
        Calls++;
        return Found;
    }

    /// <summary>Adds a client to be found.</summary>
    public FakeDiscovery With(int processId, bool supported = true)
    {
        Found.Add(new ClientOption(processId, $"WoW.exe ({processId})", supported));
        return this;
    }
}

/// <summary>A bot that does not exist, driven by hand.</summary>
internal sealed class FakeController : IBotController
{
    public bool IsAttached { get; private set; }

    public bool IsRunning { get; private set; }

    public string CharacterSummary { get; set; } = string.Empty;

    public bool CanExecute { get; private set; }

    public string ClientCapabilities { get; set; } = string.Empty;

    /// <summary>Whether enabling execution succeeds.</summary>
    public bool ExecutionSucceeds { get; set; } = true;

    public string EnableExecution()
    {
        Actions.Add("EnableExecution");

        CanExecute = ExecutionSucceeds;

        return ExecutionSucceeds
            ? "Execution enabled."
            : "Could not install the execution hook.";
    }

    /// <summary>What the bot was asked to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether attaching succeeds.</summary>
    public bool AttachSucceeds { get; set; } = true;

    /// <summary>What the offset check reports.</summary>
    public string Report { get; set; } = "All offsets verified.";

    public AttachOutcome Attach(ClientOption client)
    {
        Actions.Add($"Attach({client.ProcessId})");

        IsAttached = AttachSucceeds;

        return new AttachOutcome(
            AttachSucceeds,
            Report,
            AttachSucceeds ? "Attached." : "Attach failed. See the verification report.");
    }

    public void Detach()
    {
        Actions.Add("Detach");
        IsRunning = false;
        IsAttached = false;
        CanExecute = false;
    }

    public string Start(string botBase, string routine, string? profilePath)
    {
        Actions.Add($"Start({botBase}, {routine}, {profilePath ?? "no profile"})");
        IsRunning = true;
        return $"Running {botBase}.";
    }

    public void Stop()
    {
        Actions.Add("Stop");
        IsRunning = false;
    }
}
