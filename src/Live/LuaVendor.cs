using System.Globalization;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Business with a vendor or a mailbox, through the client's own scripting.
/// </summary>
/// <remarks>
/// <para>
/// The verbs the errand handler needs, and nothing else. Reading the bags in enough detail to
/// decide what to sell is the expensive part — item name, quality, class, sell price and count
/// for every occupied slot — so it happens in one round trip and is cached until something is
/// sold or posted.
/// </para>
/// <para>
/// <b>Selling is <c>UseContainerItem</c> with a merchant open, and so is attaching to a
/// letter.</b> That is not a trick; it is how the client works, and it is also why every method
/// here checks its window first: the same call sells, attaches, or eats the item depending on
/// what happens to be open. Getting that wrong destroys things.
/// </para>
/// </remarks>
public sealed class LuaVendor : IVendorActions
{
    /// <summary>Separates fields within a row.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>How long a reading of the bags stays good for.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Reads every occupied bag slot with enough detail to decide what to do with it.
    /// </summary>
    /// <remarks>
    /// <c>GetItemInfo</c> can return nothing for an item the client has not cached yet, in
    /// which case the row is skipped rather than guessed at — an item with an unknown sell
    /// price must not be sold, and one with an unknown quality must not be posted.
    /// </remarks>
    internal const string ReadBagsScript = """
        local rows = {}
        for bag = 0, 4 do
            for slot = 1, (GetContainerNumSlots(bag) or 0) do
                local link = GetContainerItemLink(bag, slot)
                if link then
                    local name, _, quality, level, required, class, subclass, _, equip, _, price =
                        GetItemInfo(link)
                    local _, count = GetContainerItemInfo(bag, slot)
                    if name and price then
                        rows[#rows + 1] = bag .. "\31" .. slot .. "\31" .. name
                            .. "\31" .. (quality or 0) .. "\31" .. (level or 0)
                            .. "\31" .. (required or 0) .. "\31" .. (class or "")
                            .. "\31" .. (subclass or "") .. "\31" .. (equip or "")
                            .. "\31" .. (count or 1) .. "\31" .. price
                    end
                end
            end
        end
        __wowbuddy_result = table.concat(rows, "\30")
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private IReadOnlyList<BagSlot> _bags = [];
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Builds vendor actions over an attached client.</summary>
    public LuaVendor(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool IsMerchantOpen =>
        Supports() && _lua.EvaluateBool("(MerchantFrame and MerchantFrame:IsVisible()) and true or false");

    /// <inheritdoc />
    public bool IsMailboxOpen =>
        Supports() && _lua.EvaluateBool("(MailFrame and MailFrame:IsVisible()) and true or false");

    /// <inheritdoc />
    public bool CanRepairHere =>
        Supports() && _lua.EvaluateBool("CanMerchantRepair() and true or false");

    /// <inheritdoc />
    public IReadOnlyList<BagSlot> BagContents
    {
        get
        {
            Refresh();
            return _bags;
        }
    }

    /// <summary>How many times the bags have actually been read.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// The cost is checked first because <c>RepairAllItems</c> reports nothing: called without
    /// the money it silently does not repair, and the bot would walk away believing it had.
    /// </remarks>
    public bool Repair()
    {
        if (!Supports() || !IsMerchantOpen)
        {
            return false;
        }

        if (!_lua.EvaluateBool("(select(2, GetRepairAllCost())) and true or false"))
        {
            Log.For<LuaVendor>().Warning("Not enough money to repair");
            return false;
        }

        _lua.Execute("RepairAllItems()");
        Invalidate();
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Guarded on the merchant window because the same call attaches to a letter or uses the
    /// item when something else is open. Selling the wrong way round destroys things.
    /// </remarks>
    public bool Sell(BagSlot slot)
    {
        if (!Supports() || !IsMerchantOpen)
        {
            return false;
        }

        _lua.Execute(Use(slot));
        Invalidate();
        return true;
    }

    /// <inheritdoc />
    public bool Mail(string recipient, IReadOnlyList<BagSlot> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentNullException.ThrowIfNull(items);

        if (!Supports() || !IsMailboxOpen || items.Count == 0)
        {
            return false;
        }

        // One script: clear whatever was attached, attach this letter's items, send. Split
        // across round trips, a failure part way would leave items attached to an unsent letter
        // and out of the bags, where nothing would find them again.
        string attach = string.Join(" ", items.Select(Use));

        _lua.Execute(
            $"ClearSendMail() {attach} "
            + $"SendMail(\"{Escape(recipient)}\", \"{Escape(MailSubject)}\", \"\")");

        Invalidate();
        return true;
    }

    /// <summary>What the bot puts in the subject line.</summary>
    /// <remarks>
    /// A letter with an empty subject cannot be sent, and something recognisable is kinder to
    /// whoever opens the mailbox at the other end.
    /// </remarks>
    public const string MailSubject = "Bags";

    /// <inheritdoc />
    public bool Close()
    {
        if (!Supports())
        {
            return false;
        }

        // Both, unconditionally: whichever is not open ignores it, and leaving a window open
        // stops the character moving.
        return _lua.Execute("CloseMerchant() CloseMail()");
    }

    /// <summary>Forgets the last reading of the bags.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    private static string Use(BagSlot slot) =>
        $"UseContainerItem({slot.Bag.ToString(CultureInfo.InvariantCulture)}, "
        + $"{slot.Slot.ToString(CultureInfo.InvariantCulture)})";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Vendor))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaVendor>().Warning("{Explanation}", _capabilities.Explain(GameCapability.Vendor));
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

        if (!Supports())
        {
            _bags = [];
            return;
        }

        _lua.Execute(ReadBagsScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            // Unknown, not empty. An empty reading would have the handler decide there is
            // nothing to sell and walk away from full bags.
            _bags = [];
            return;
        }

        _bags = Parse(raw);
    }

    private static IReadOnlyList<BagSlot> Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        List<BagSlot> bags = [];

        foreach (string row in raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(FieldSeparator);

            if (fields.Length < 11
                || !int.TryParse(fields[0], out int bag)
                || !int.TryParse(fields[1], out int slot))
            {
                Log.For<LuaVendor>().Warning(
                    "A bag row could not be read and was skipped: {Row}", row);
                continue;
            }

            int count = Number(fields[9], 1);

            bags.Add(new BagSlot(
                bag,
                slot,
                new ItemInfo(
                    ItemId: 0,
                    fields[2],
                    (ItemQuality)Number(fields[3], 0),
                    Number(fields[4], 0),
                    Number(fields[5], 0),
                    fields[6],
                    fields[7],
                    fields[8],
                    count,
                    Number(fields[10], 0)),
                count));
        }

        return bags;
    }

    private static int Number(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
}
