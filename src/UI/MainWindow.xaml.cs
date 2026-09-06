using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Client;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Objects;

namespace WoWBuddy.UI;

/// <summary>
/// The phase 0/1 shell: pick a client, attach, and see what the bot can read.
/// </summary>
/// <remarks>
/// Deliberately small. The bot base, combat routine and profile selectors arrive with the
/// features they drive; putting dead controls on screen now would only invite the question
/// of why they do nothing.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<WowClientCandidate> _candidates = [];
    private GameClient? _client;
    private World? _world;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += OnRefreshTick;

        RefreshClientList();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => RefreshClientList();

    private void RefreshClientList()
    {
        _candidates.Clear();
        ClientPicker.Items.Clear();

        foreach (WowClientCandidate candidate in WowClientLocator.FindAll())
        {
            _candidates.Add(candidate);
            string label = candidate.Build.IsSupported
                ? candidate.Describe()
                : candidate.Describe() + "  [unsupported]";
            ClientPicker.Items.Add(label);
        }

        if (ClientPicker.Items.Count > 0)
        {
            ClientPicker.SelectedIndex = 0;
            StatusText.Text = $"{ClientPicker.Items.Count} client process(es) found.";
        }
        else
        {
            StatusText.Text = "No WoW client found. Start the game and log a character in.";
        }
    }

    private void OnAttachClicked(object sender, RoutedEventArgs e)
    {
        int index = ClientPicker.SelectedIndex;
        if (index < 0 || index >= _candidates.Count)
        {
            StatusText.Text = "Select a client first.";
            return;
        }

        Detach();

        AttachResult result = GameClient.Attach(_candidates[index].Process);
        VerificationText.Text = result.Report.ToString();

        if (!result.Success)
        {
            StatusText.Text = "Attach failed. See the verification report.";
            Log.For<MainWindow>().Warning("Attach failed: {Reason}", result.FailureReason);
            return;
        }

        _client = result.Client;
        _world = new World(_client!);

        AttachButton.IsEnabled = false;
        DetachButton.IsEnabled = true;
        StatusText.Text = $"Attached to pid {_client!.ProcessId}.";
        _refreshTimer.Start();
    }

    private void OnDetachClicked(object sender, RoutedEventArgs e)
    {
        Detach();
        StatusText.Text = "Detached.";
    }

    private void Detach()
    {
        _refreshTimer.Stop();
        _world = null;
        _client?.Dispose();
        _client = null;

        AttachButton.IsEnabled = true;
        DetachButton.IsEnabled = false;
        PlayerText.Text = "Not attached.";
    }

    private void OnRefreshTick(object? sender, System.EventArgs e)
    {
        if (_client is null || _world is null)
        {
            return;
        }

        if (!_client.IsAttached)
        {
            Detach();
            StatusText.Text = "Lost the client: it exited, or a different character logged in.";
            return;
        }

        WoWLocalPlayer? me = _world.Me;
        if (me is null)
        {
            PlayerText.Text = "Not in the world.";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"GUID       {me.Guid}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Race       {me.Race}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Class      {me.Class}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Level      {me.Level}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Health     {me.Health}/{me.MaxHealth} ({me.HealthPercent:F1}%)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{me.PowerType,-10} {me.Power}/{me.MaxPower}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Experience {me.Experience}/{me.NextLevelExperience}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Money      {me.Gold:F4}g");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Position   {me.Position}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Facing     {me.Facing:F3} rad");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Map/zone   {me.MapId} / {me.ZoneId}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"In combat  {me.IsInCombat}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Mounted    {me.IsMounted}");
        builder.AppendLine();

        WorldSnapshot snapshot = _world.Snapshot();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Objects    {snapshot.Objects.Count}");
        foreach (var pair in snapshot.CountByType.OrderByDescending(kv => kv.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {pair.Key,-14} {pair.Value}");
        }

        PlayerText.Text = builder.ToString();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        Detach();
        base.OnClosed(e);
    }
}
