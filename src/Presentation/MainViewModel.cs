using System.Collections.ObjectModel;
using WoWBuddy.BotBases.Group;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Configuration;
using WoWBuddy.Plugins;
using WoWBuddy.Profiles;

namespace WoWBuddy.Presentation;

/// <summary>
/// Everything the main window shows and every decision it makes.
/// </summary>
/// <remarks>
/// <para>
/// All of it lives here rather than in the window's code-behind, for one reason worth stating:
/// the rules about what the user is allowed to do next are exactly the part of a UI that goes
/// wrong, and they are testable only if they are somewhere a test can reach. Attaching while
/// running, starting a questing profile that will not load, detaching mid-session — those are
/// decisions, and they are checked here rather than hoped for.
/// </para>
/// <para>
/// Nothing in this file mentions WPF. The window binds to it; this project builds and its tests
/// run on any platform, which matters because the window itself can only be built on Windows.
/// </para>
/// </remarks>
public sealed class MainViewModel : ObservableObject
{
    private readonly IClientDiscovery _discovery;
    private readonly IBotController _controller;
    private readonly RoutineCatalogue _routines;
    private readonly PluginManager? _plugins;
    private readonly ConfigStore? _config;

    private BotSettings _settings = new();
    private bool _loading;

    private ClientOption? _selectedClient;
    private BotBaseOption _selectedBotBase = BotBaseOption.All[0];
    private ICombatRoutine? _selectedRoutine;
    private string _profilePath = string.Empty;
    private string _profileReport = string.Empty;
    private Profile? _profile;
    private string _status = "Not attached.";
    private string _verificationReport = "Attach to a client to verify the offset table against it.";

    public MainViewModel(
        IClientDiscovery discovery,
        IBotController controller,
        RoutineCatalogue? routines = null,
        PluginManager? plugins = null,
        ConfigStore? config = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _routines = routines ?? new RoutineCatalogue();
        _plugins = plugins;
        _config = config;

        RefreshCommand = new RelayCommand(RefreshClients);
        AttachCommand = new RelayCommand(Attach, () => CanAttach);
        DetachCommand = new RelayCommand(Detach, () => IsAttached);
        EnableExecutionCommand = new RelayCommand(EnableExecution, () => CanEnableExecution);
        StartCommand = new RelayCommand(Start, () => CanStart);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        foreach (ICombatRoutine routine in _routines.All)
        {
            Routines.Add(routine);
        }

        _selectedRoutine = Routines.FirstOrDefault();

        RefreshClients();
        RefreshPlugins();
        LoadSettings();
    }

    // ---- what the window shows -----------------------------------------------------------

    /// <summary>Client processes to choose between.</summary>
    public ObservableCollection<ClientOption> Clients { get; } = [];

    /// <summary>Ways of playing.</summary>
    public IReadOnlyList<BotBaseOption> BotBases => BotBaseOption.All;

    /// <summary>Combat routines to choose between.</summary>
    public ObservableCollection<ICombatRoutine> Routines { get; } = [];

    /// <summary>Plugins that were loaded, and what state they are in.</summary>
    public ObservableCollection<LoadedPlugin> Plugins { get; } = [];

    /// <summary>The last few log lines.</summary>
    public LogBuffer Log { get; } = new();

    /// <summary>The line at the bottom of the window.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>What the offset check said when the bot attached.</summary>
    public string VerificationReport
    {
        get => _verificationReport;
        private set => Set(ref _verificationReport, value);
    }

    /// <summary>What the character is, once attached.</summary>
    public string CharacterSummary => _controller.CharacterSummary;

    // ---- what the user picks ---------------------------------------------------------------

    /// <summary>The client to attach to.</summary>
    public ClientOption? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (Set(ref _selectedClient, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>How to play.</summary>
    public BotBaseOption SelectedBotBase
    {
        get => _selectedBotBase;
        set
        {
            if (Set(ref _selectedBotBase, value))
            {
                Raise(nameof(NeedsProfile));
                RaiseCommandStates();
                Remember(_settings with { BotBase = value.Name });
            }
        }
    }

    /// <summary>Which routine plays the character.</summary>
    public ICombatRoutine? SelectedRoutine
    {
        get => _selectedRoutine;
        set
        {
            if (Set(ref _selectedRoutine, value))
            {
                RaiseCommandStates();
                Remember(_settings with { Routine = value?.Name ?? string.Empty });
            }
        }
    }

    /// <summary>The profile file, when the chosen bot base needs one.</summary>
    public string ProfilePath
    {
        get => _profilePath;
        set
        {
            if (Set(ref _profilePath, value))
            {
                LoadProfile();
                Remember(_settings with { ProfilePath = value });
            }
        }
    }

    /// <summary>What the profile loader said about it.</summary>
    public string ProfileReport
    {
        get => _profileReport;
        private set => Set(ref _profileReport, value);
    }

    /// <summary>True when the chosen bot base cannot run without a profile.</summary>
    public bool NeedsProfile => SelectedBotBase.NeedsProfile;

    // ---- what the user can do --------------------------------------------------------------

    /// <summary>Looks for clients again.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>Attaches to the chosen client.</summary>
    public RelayCommand AttachCommand { get; }

    /// <summary>Detaches from the client.</summary>
    public RelayCommand DetachCommand { get; }

    /// <summary>Lets the bot run code inside the client, and asks what it supports.</summary>
    public RelayCommand EnableExecutionCommand { get; }

    /// <summary>Starts playing.</summary>
    public RelayCommand StartCommand { get; }

    /// <summary>Stops playing.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>True once attached.</summary>
    public bool IsAttached => _controller.IsAttached;

    /// <summary>True while playing.</summary>
    public bool IsRunning => _controller.IsRunning;

    /// <summary>True once the bot can act on the game rather than only read it.</summary>
    public bool CanExecute => _controller.CanExecute;

    /// <summary>Whether enabling execution is possible now.</summary>
    public bool CanEnableExecution => IsAttached && !CanExecute;

    /// <summary>What the client can and cannot tell the bot.</summary>
    public string ClientCapabilities => _controller.ClientCapabilities;

    /// <summary>
    /// True when attaching is possible.
    /// </summary>
    /// <remarks>
    /// An unsupported build is refused rather than attempted. Every offset in the table is for
    /// 3.3.5a build 12340; against another build they point at whatever happens to be there,
    /// and the bot would read plausible-looking rubbish and act on it.
    /// </remarks>
    public bool CanAttach => !IsAttached && SelectedClient is { IsSupported: true };

    /// <summary>True when the bot could start now.</summary>
    public bool CanStart =>
        IsAttached
        && CanExecute
        && !IsRunning
        && SelectedRoutine is not null
        && (!NeedsProfile || _profile is not null);

    /// <summary>
    /// Why the bot cannot start, for the window to show instead of a disabled button with no
    /// explanation.
    /// </summary>
    public string StartBlockedReason
    {
        get
        {
            if (IsRunning)
            {
                return "Already running.";
            }

            if (!IsAttached)
            {
                return "Attach to a client first.";
            }

            if (!CanExecute)
            {
                return "Enable execution first. The bot can read the client but not act on it.";
            }

            if (SelectedRoutine is null)
            {
                return "Choose a combat routine.";
            }

            if (NeedsProfile && _profile is null)
            {
                return _profilePath.Length == 0
                    ? $"The {SelectedBotBase.Name} base needs a profile."
                    : "That profile has errors. Fix them, or choose another.";
            }

            return string.Empty;
        }
    }

    // ---- doing it ---------------------------------------------------------------------------

    /// <summary>Looks for clients again, keeping the selection where it can.</summary>
    public void RefreshClients()
    {
        int? previous = SelectedClient?.ProcessId;

        Clients.Clear();

        foreach (ClientOption client in _discovery.Find())
        {
            Clients.Add(client);
        }

        // Written out rather than with FirstOrDefault: ClientOption is a struct, so the
        // "default" it returns for no match is a real value with a process id of zero, and
        // pattern-matching it as present would silently select a client that does not exist.
        ClientOption? kept = null;

        foreach (ClientOption client in Clients)
        {
            if (client.ProcessId == previous)
            {
                kept = client;
                break;
            }
        }

        SelectedClient = kept ?? (Clients.Count > 0 ? Clients[0] : null);

        Status = Clients.Count switch
        {
            0 => "No WoW client found. Start the game and log a character in.",
            1 => "One client found.",
            _ => $"{Clients.Count} clients found.",
        };
    }

    /// <summary>Rebuilds the plugin list from the manager.</summary>
    public void RefreshPlugins()
    {
        Plugins.Clear();

        if (_plugins is null)
        {
            return;
        }

        foreach (LoadedPlugin plugin in _plugins.Plugins)
        {
            Plugins.Add(plugin);
        }
    }

    private void Attach()
    {
        if (SelectedClient is not { } client)
        {
            Status = "Select a client first.";
            return;
        }

        AttachOutcome outcome = _controller.Attach(client);

        VerificationReport = outcome.Report;
        Status = outcome.Message;

        if (outcome.Success)
        {
            // The routine list is worth narrowing once the character's class is known: thirty
            // entries is a list to search, three is a choice.
            NarrowRoutinesToCharacter();
        }

        RaiseAttachmentStates();
    }

    private void EnableExecution()
    {
        Status = _controller.EnableExecution();

        Raise(nameof(ClientCapabilities));
        RaiseAttachmentStates();
    }

    private void Detach()
    {
        // Stopping first is the controller's job, but saying so is this one's: a user who
        // clicks detach mid-session should not have to wonder whether the bot is still playing.
        bool wasRunning = IsRunning;

        _controller.Detach();

        Status = wasRunning ? "Stopped and detached." : "Detached.";
        RaiseAttachmentStates();
    }

    private void Start()
    {
        if (!CanStart)
        {
            Status = StartBlockedReason;
            return;
        }

        Status = _controller.Start(
            SelectedBotBase.Name,
            SelectedRoutine!.Name,
            NeedsProfile ? _profilePath : null);

        RaiseAttachmentStates();
    }

    private void Stop()
    {
        _controller.Stop();
        Status = "Stopped.";
        RaiseAttachmentStates();
    }

    /// <summary>
    /// Reads the chosen profile and says what is wrong with it, before anything runs.
    /// </summary>
    /// <remarks>
    /// The whole point of validating at load: a profile with a mistake in it should say so when
    /// it is chosen, not four hours into a session. A profile with warnings is still usable and
    /// is loaded; one with errors is not.
    /// </remarks>
    private void LoadProfile()
    {
        _profile = null;

        if (_profilePath.Length == 0)
        {
            ProfileReport = string.Empty;
            RaiseCommandStates();
            return;
        }

        ProfileLoadResult result = ProfileLoader.LoadFile(_profilePath);

        ProfileReport = result.Describe();

        if (result.Success)
        {
            _profile = result.Profile;

            Status = result.Warnings.Any()
                ? $"Loaded {result.Profile!.Name} with {result.Warnings.Count()} warning(s)."
                : $"Loaded {result.Profile!.Name}.";
        }
        else
        {
            Status = $"That profile has {result.Errors.Count()} error(s) and cannot be used.";
        }

        RaiseCommandStates();
    }

    /// <summary>
    /// Cuts the routine list down to the character's own class, once it is known.
    /// </summary>
    private void NarrowRoutinesToCharacter()
    {
        if (!_controller.IsAttached || _routines.Classes.Count == 0)
        {
            return;
        }

        // The controller reports the class as part of its summary; without a parsed class
        // there is nothing to narrow to, and showing everything is the safe answer.
        Raise(nameof(CharacterSummary));
    }

    /// <summary>What the window remembers between sessions.</summary>
    public BotSettings Settings => _settings;

    /// <summary>The roles a character can be told it is playing.</summary>
    public IReadOnlyList<PartyRole> Roles { get; } = Enum.GetValues<PartyRole>();

    /// <summary>What the character plays in a group.</summary>
    public PartyRole Role
    {
        get => _settings.Role;
        set => Remember(_settings with { Role = value }, nameof(Role));
    }

    /// <summary>Whether the character can skin.</summary>
    public bool CanSkin
    {
        get => _settings.CanSkin;
        set => Remember(_settings with { CanSkin = value }, nameof(CanSkin));
    }

    /// <summary>Who to post keepable items to.</summary>
    public string MailRecipient
    {
        get => _settings.MailRecipient;
        set => Remember(_settings with { MailRecipient = value }, nameof(MailRecipient));
    }

    /// <summary>Where the navigation meshes are, or empty for the folder beside the bot.</summary>
    public string MmapsDirectory
    {
        get => _settings.MmapsDirectory;
        set => Remember(_settings with { MmapsDirectory = value }, nameof(MmapsDirectory));
    }

    /// <summary>
    /// Reads back what was chosen last time.
    /// </summary>
    /// <remarks>
    /// A missing or unreadable file leaves the defaults in place rather than failing: settings
    /// are a convenience, and losing them should never stop the bot starting.
    /// </remarks>
    public void LoadSettings()
    {
        if (_config is null)
        {
            return;
        }

        _loading = true;

        try
        {
            _settings = _config.Load<BotSettings>(BotSettings.FileName) ?? new BotSettings();

            foreach (BotBaseOption option in BotBases)
            {
                if (string.Equals(option.Name, _settings.BotBase, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedBotBase = option;
                    break;
                }
            }

            if (_routines.ByName(_settings.Routine) is { } routine)
            {
                SelectedRoutine = routine;
            }

            // Last, because setting it loads the profile and reports on it.
            if (_settings.ProfilePath.Length > 0)
            {
                ProfilePath = _settings.ProfilePath;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Keeps a changed setting.</summary>
    public void Remember(BotSettings settings, string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;

        if (propertyName is not null)
        {
            Raise(propertyName);
        }

        // Not while reading them back: every assignment during a load would write the file
        // again, and a half-applied load would be what got written.
        if (_loading || _config is null)
        {
            return;
        }

        try
        {
            _config.Save(BotSettings.FileName, _settings);
        }
        catch (IOException exception)
        {
            // A read-only folder, a full disk, a file open in something else. Worth saying and
            // not worth stopping for.
            // Fully qualified: this class has a Log property of its own, which is the window's
            // log panel rather than the logger.
            Common.Logging.Log.For<MainViewModel>().Warning(
                exception, "Could not keep the settings. They will not survive this session.");
        }
    }

    private void RaiseAttachmentStates()
    {
        Raise(nameof(IsAttached));
        Raise(nameof(IsRunning));
        Raise(nameof(CanExecute));
        Raise(nameof(CanEnableExecution));
        Raise(nameof(CharacterSummary));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        Raise(nameof(CanAttach));
        Raise(nameof(CanStart));
        Raise(nameof(StartBlockedReason));

        Raise(nameof(CanEnableExecution));

        AttachCommand.RaiseCanExecuteChanged();
        EnableExecutionCommand.RaiseCanExecuteChanged();
        DetachCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }
}
