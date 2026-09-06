using System.Globalization;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Learning abilities from a trainer, through the client's own scripting.
/// </summary>
/// <remarks>
/// <para>
/// <b>No spell data ships with this project, and none is needed.</b> The trainer already knows
/// what this character's class, level and money allow, what it has already learned and what it
/// cannot use, and it will list exactly that. Any table this project shipped would be a worse
/// answer that also went stale.
/// </para>
/// <para>
/// The list is filtered to what is available before it is read. A trainer's window otherwise
/// includes what the character already knows and what it is too low to learn, and buying by
/// index into an unfiltered list learns the wrong thing — or nothing, expensively.
/// </para>
/// </remarks>
public sealed class LuaTrainer : ITrainerActions
{
    /// <summary>Separates fields within a row.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>How long a reading of the trainer's list stays good for.</summary>
    /// <remarks>
    /// Short, because learning one thing can reveal another and shifts every index after it.
    /// Learning invalidates the reading outright; this only covers the gap between ticks.
    /// </remarks>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads everything the open trainer will teach right now.
    /// </summary>
    /// <remarks>
    /// The filter is set first so that the indices belong to a list of available services only.
    /// It is a display setting the player may have changed, and leaving it as found would mean
    /// reading one list and learning from another.
    /// </remarks>
    internal const string ReadScript = """
        SetTrainerServiceTypeFilter("available", 1)
        SetTrainerServiceTypeFilter("unavailable", 0)
        SetTrainerServiceTypeFilter("used", 0)
        local rows = {}
        for i = 1, (GetNumTrainerServices() or 0) do
            local name, rank, category = GetTrainerServiceInfo(i)
            local cost = GetTrainerServiceCost(i)
            if name and category == "available" then
                rows[#rows + 1] = i .. "\31" .. name .. "\31" .. (rank or "")
                    .. "\31" .. (cost or 0)
            end
        end
        __wowbuddy_result = table.concat(rows, "\30")
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private IReadOnlyList<TrainerService> _services = [];
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Builds trainer actions over an attached client.</summary>
    public LuaTrainer(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool IsTrainerOpen =>
        Supports()
        && _lua.EvaluateBool("(ClassTrainerFrame and ClassTrainerFrame:IsVisible()) and true or false");

    /// <inheritdoc />
    public IReadOnlyList<TrainerService> Available
    {
        get
        {
            Refresh();
            return _services;
        }
    }

    /// <summary>How many times the trainer's list has actually been read.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Guarded on the window, like every other verb here: <c>BuyTrainerService</c> with a
    /// trainer closed does nothing, and the caller would walk away believing the character had
    /// learned something.
    /// </remarks>
    public bool Learn(TrainerService service)
    {
        if (!Supports() || service.Index <= 0 || !IsTrainerOpen)
        {
            return false;
        }

        _lua.Execute(
            $"BuyTrainerService({service.Index.ToString(CultureInfo.InvariantCulture)})");

        // The money is gone and every index after this one has moved.
        Invalidate();

        Log.For<LuaTrainer>().Information("Learning {Service}", service);
        return true;
    }

    /// <inheritdoc />
    public bool CloseTrainer()
    {
        if (!Supports())
        {
            return false;
        }

        Invalidate();
        return _lua.Execute("CloseTrainer()");
    }

    /// <summary>Forgets the last reading of the trainer's list.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Trainer))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaTrainer>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.Trainer));
        }

        return false;
    }

    private void Refresh()
    {
        DateTimeOffset now = _clock();

        if (now - _readAt < CacheLifetime)
        {
            return;
        }

        _readAt = now;

        if (!Supports() || !IsTrainerOpen)
        {
            _services = [];
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        if (_lua.Evaluate("__wowbuddy_result") is not { } raw)
        {
            // Unknown, not "nothing to learn". Both end the errand here, but only one of them
            // should say the character is up to date.
            _services = [];
            return;
        }

        List<TrainerService> services = [];

        foreach (string row in raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(FieldSeparator);

            if (fields.Length < 4 || !int.TryParse(fields[0], out int index) || index <= 0)
            {
                Log.For<LuaTrainer>().Warning(
                    "A trainer row could not be read and was skipped: {Row}", row);
                continue;
            }

            services.Add(new TrainerService(
                index,
                fields[1],
                fields[2],
                long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long cost)
                    ? cost
                    : 0L));
        }

        _services = services;
    }
}
