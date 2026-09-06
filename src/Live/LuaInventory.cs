using System.Globalization;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// The character's bags, money and gear wear, read from the client.
/// </summary>
/// <remarks>
/// <para>
/// Four numbers drive most of the bot's housekeeping: how much bag space is left, how worn the
/// gear is, and how much money there is. All three decide whether it is time to go and see a
/// vendor, and all three come back in one round trip.
/// </para>
/// <para>
/// <b>Using an item goes through the bag slot, not the name.</b> A profile names an item by id,
/// and turning an id into a name would need item data this project does not ship — so the bot
/// finds the slot holding that id and clicks it, which needs nothing but the bags themselves.
/// </para>
/// <para>
/// Durability is read across the equipped slots, and the worst one is what matters: gear breaks
/// one piece at a time, and a weapon at zero stops the character fighting whatever the rest of
/// the set says.
/// </para>
/// </remarks>
public sealed class LuaInventory
{
    /// <summary>Separates fields in the reading.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>How long a reading stays good for.</summary>
    /// <remarks>
    /// Bags fill a few times an hour, so this can be the longest of the three caches. The bot
    /// clears it itself after looting or selling.
    /// </remarks>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Counts bag space, finds the worst-worn item and reads the money, in one pass.
    /// </summary>
    /// <remarks>
    /// Bags are zero (the backpack) to four. Equipped slots are one to nineteen; slots that
    /// hold nothing, or hold something with no durability, report nothing and are skipped —
    /// otherwise every character would read as zero per cent worn because of its shirt.
    /// </remarks>
    internal const string ReadScript = """
        local free, total = 0, 0
        for bag = 0, 4 do
            local slots = GetContainerNumSlots(bag) or 0
            total = total + slots
            for slot = 1, slots do
                if not GetContainerItemLink(bag, slot) then free = free + 1 end
            end
        end
        local worst = 100
        for slot = 1, 19 do
            local current, maximum = GetInventoryItemDurability(slot)
            if current and maximum and maximum > 0 then
                local percent = (current / maximum) * 100
                if percent < worst then worst = percent end
            end
        end
        __wowbuddy_result = free .. "\31" .. total .. "\31" .. string.format("%.1f", worst)
            .. "\31" .. (GetMoney() or 0)
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private InventoryState _state;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Reads the bags of a client.</summary>
    public LuaInventory(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How full the bags are and how worn the gear is.</summary>
    public InventoryState State
    {
        get
        {
            Refresh();
            return _state;
        }
    }

    /// <summary>How many times the bags have actually been read.</summary>
    public int Reads { get; private set; }

    /// <summary>
    /// How many of an item the character is carrying, across every bag.
    /// </summary>
    /// <remarks>
    /// Not cached. This is asked about one item at a time, by a profile condition or a collect
    /// objective, and caching per item would keep a dictionary that goes stale the moment
    /// something is looted — which is precisely when the answer matters.
    /// </remarks>
    public int Count(uint itemId)
    {
        if (!Supports(GameCapability.Inventory))
        {
            return 0;
        }

        return _lua.EvaluateInt(
            $"GetItemCount({itemId.ToString(CultureInfo.InvariantCulture)})") ?? 0;
    }

    /// <summary>Uses an item from the bags, by id.</summary>
    /// <returns>False when the character is not carrying it.</returns>
    public bool Use(uint itemId)
    {
        if (!Supports(GameCapability.UseItem))
        {
            return false;
        }

        string id = itemId.ToString(CultureInfo.InvariantCulture);

        // Find and click in one script rather than two round trips, and so that nothing can
        // move between finding the slot and using it.
        bool used = _lua.EvaluateBool(
            "(function() for bag = 0, 4 do "
            + "for slot = 1, (GetContainerNumSlots(bag) or 0) do "
            + "local link = GetContainerItemLink(bag, slot) "
            + $"if link and string.match(link, \"item:{id}:\") then "
            + "UseContainerItem(bag, slot) return true end "
            + "end end return false end)()");

        if (used)
        {
            // Using something may have consumed it.
            Invalidate();
        }

        return used;
    }

    /// <summary>Forgets the last reading, for after looting or selling.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    private bool Supports(GameCapability capability)
    {
        if (_capabilities.Supports(capability))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaInventory>().Warning("{Explanation}", _capabilities.Explain(capability));
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

        if (!Supports(GameCapability.Inventory))
        {
            // Zero total slots, which reads as "no space known" rather than "bags are full".
            // The errand planner treats an unknown bag as nothing to act on, which is right:
            // it should not send the character to a vendor on the strength of a failed read.
            _state = default;
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            _state = default;
            return;
        }

        string[] fields = raw.Split(FieldSeparator);

        if (fields.Length < 4)
        {
            Log.For<LuaInventory>().Warning("The bags could not be read: {Raw}", raw);
            _state = default;
            return;
        }

        _state = new InventoryState(
            ParseInt(fields[0]),
            ParseInt(fields[1]),
            ParseDouble(fields[2]),
            ParseLong(fields[3]));
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out long value) ? value : 0L;

    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, 0d, 100d)
            : 100d;
}
