namespace WoWBuddy.Presentation;

/// <summary>A client process the user could attach to.</summary>
/// <param name="ProcessId">Its process id.</param>
/// <param name="Description">What to show in the list.</param>
/// <param name="IsSupported">Whether the bot knows this build.</param>
public readonly record struct ClientOption(int ProcessId, string Description, bool IsSupported)
{
    public override string ToString() =>
        IsSupported ? Description : $"{Description}  [unsupported]";
}

/// <summary>What attaching produced.</summary>
/// <param name="Success">Whether the bot is now attached.</param>
/// <param name="Report">The offset verification report, to show whatever happened.</param>
/// <param name="Message">A sentence for the status bar.</param>
public readonly record struct AttachOutcome(bool Success, string Report, string Message);

/// <summary>Finds clients to attach to.</summary>
/// <remarks>
/// An interface so the window's logic can be exercised without a game running. Everything the
/// view model does with a client goes through this and <see cref="IBotController"/>.
/// </remarks>
public interface IClientDiscovery
{
    /// <summary>Every WoW process currently running.</summary>
    IReadOnlyList<ClientOption> Find();
}

/// <summary>Attaching to a client and running the bot in it.</summary>
public interface IBotController
{
    /// <summary>True once attached to a client.</summary>
    bool IsAttached { get; }

    /// <summary>True while the bot is playing.</summary>
    bool IsRunning { get; }

    /// <summary>What the character is, once attached. Empty before that.</summary>
    string CharacterSummary { get; }

    /// <summary>
    /// True once the bot can run code inside the client.
    /// </summary>
    /// <remarks>
    /// A separate step from attaching, and deliberately so: attaching only reads, and a user
    /// who wants to look at what the bot can see should not have to let it write. Everything
    /// that acts on the game — casting, moving, talking to quest givers — waits for this.
    /// </remarks>
    bool CanExecute { get; }

    /// <summary>What the client can and cannot tell the bot, once execution is enabled.</summary>
    string ClientCapabilities { get; }

    /// <summary>Attaches to a client.</summary>
    AttachOutcome Attach(ClientOption client);

    /// <summary>
    /// Installs the execution hook and asks the client what it supports.
    /// </summary>
    /// <returns>A sentence for the status bar.</returns>
    string EnableExecution();

    /// <summary>Detaches, stopping first if it is running.</summary>
    void Detach();

    /// <summary>Starts playing.</summary>
    /// <returns>A sentence saying what happened, for the status bar.</returns>
    string Start(string botBase, string routine, string? profilePath);

    /// <summary>Stops playing, staying attached.</summary>
    void Stop();
}
