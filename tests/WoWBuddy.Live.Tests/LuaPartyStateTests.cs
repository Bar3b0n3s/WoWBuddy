using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaPartyStateTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private static readonly Vector3 Me = new(100f, 100f, 50f);

    private static string Member(
        string unit,
        ulong guid,
        string name = "Someone",
        double health = 100d,
        bool alive = true,
        bool online = true,
        bool leader = false,
        bool inCombat = false,
        ulong target = 0UL) =>
        string.Join(Field,
            unit,
            "0x" + guid.ToString("X16"),
            name,
            health.ToString("F1"),
            alive ? "1" : "0",
            online ? "1" : "0",
            leader ? "1" : "0",
            inCombat ? "1" : "0",
            "0x" + target.ToString("X16"));

    private static LuaPartyState Build(
        FakeLua lua,
        Dictionary<ulong, Vector3>? positions = null,
        DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;

        return new LuaPartyState(
            lua,
            CapabilityProbes.Probe(lua),
            () => Me,
            guid => positions is not null && positions.TryGetValue(guid.Value, out Vector3 position)
                ? position
                : null,
            () => clock);
    }

    [Fact]
    public void ReadsTheWholeGroupInOneRoundTrip()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Member("party1", 0x11, "Tank", health: 40d, leader: true, inCombat: true, target: 0xAA),
            Member("party2", 0x22, "Healer", health: 90d));

        LuaPartyState party = Build(lua, new Dictionary<ulong, Vector3>
        {
            [0x11] = new Vector3(110f, 100f, 50f),
            [0x22] = new Vector3(130f, 100f, 50f),
        });

        IReadOnlyList<PartyMember> members = party.Members;

        Assert.Equal(2, members.Count);
        Assert.Equal(1, party.Reads);

        // Nearest first.
        Assert.Equal("Tank", members[0].Name);
        Assert.Equal(10f, members[0].Distance, 1);
        Assert.Equal(40d, members[0].HealthPercent);
        Assert.True(members[0].IsLeader);
        Assert.True(members[0].IsInCombat);
        Assert.Equal(0xAAUL, members[0].TargetGuid.Value);

        Assert.Equal(30f, members[1].Distance, 1);
    }

    [Fact]
    public void AMemberTheClientCannotSeeIsFarAwayRatherThanAtTheOrigin()
    {
        // The single most important line in the class. A member outside visible range is not in
        // the object manager, and reporting distance zero would have a healer decide someone
        // three zones away is in range.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Member("party1", 0x11, "Missing");

        PartyMember member = Assert.Single(Build(lua).Members);

        Assert.Equal(float.PositiveInfinity, member.Distance);
        Assert.False(member.CanBeHelped());
        Assert.Equal(Vector3.Zero, member.Position);
    }

    [Fact]
    public void TheSameQuestionTwiceCostsOneRead()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Member("party1", 0x11);

        LuaPartyState party = Build(lua);

        _ = party.Members;
        _ = party.Members;

        Assert.Equal(1, party.Reads);
    }

    [Fact]
    public void TheCacheIsShortBecauseAGroupMoves()
    {
        // Health and position change second by second, and a healer working from a stale
        // reading heals the wrong person.
        Assert.True(LuaPartyState.CacheLifetime < LuaQuestLog.CacheLifetime);

        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Member("party1", 0x11);

        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        LuaPartyState party = new(lua, CapabilityProbes.Probe(lua), () => Me, _ => null, () => now);

        _ = party.Members;
        now += LuaPartyState.CacheLifetime + TimeSpan.FromMilliseconds(1);
        _ = party.Members;

        Assert.Equal(2, party.Reads);
    }

    [Fact]
    public void PlayingAloneIsAnEmptyGroupRatherThanAFailure()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Empty;

        LuaPartyState party = Build(lua);

        Assert.Empty(party.Members);
        Assert.False(party.IsInGroup());
        Assert.Equal(0, party.Dropped);
    }

    [Fact]
    public void AFailedReadIsNotMistakenForPlayingAlone()
    {
        // Reporting an empty group would have the dungeon base decide it is alone and wander
        // off, which is much worse than doing nothing for a tick.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = null;

        Assert.Empty(Build(lua).Members);
    }

    [Fact]
    public void ARowItCannotReadIsDroppedRatherThanGuessedAt()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Member("party1", 0x11, "Fine"),
            "party2" + Field + "notaguid" + Field + "Broken",
            Member("party3", 0x00, "ZeroGuid"));

        LuaPartyState party = Build(lua);

        Assert.Equal("Fine", Assert.Single(party.Members).Name);
        Assert.Equal(2, party.Dropped);
    }

    [Fact]
    public void AClientWithoutThePartyCallsIsNotAskedAtAll()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("UnitAffectingCombat");
        lua.Answers["__wowbuddy_result"] = Member("party1", 0x11);

        LuaPartyState party = Build(lua);

        Assert.Empty(party.Members);
        Assert.Equal(0, party.Reads);
        Assert.False(party.Follow(new WoWGuid(0x11)));
    }

    [Fact]
    public void FollowingNeedsAUnitIdRatherThanAGuid()
    {
        // FollowUnit takes "party1", not a GUID, so the mapping from the last reading is what
        // makes following possible at all.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Member("party2", 0x22, "Leader", leader: true);

        LuaPartyState party = Build(lua);

        Assert.True(party.Follow(new WoWGuid(0x22)));
        Assert.Contains(lua.Asked, asked => asked.Contains("FollowUnit(\"party2\")", StringComparison.Ordinal));
    }

    [Fact]
    public void SomeoneNotInTheGroupCannotBeFollowed()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Member("party1", 0x11);

        Assert.False(Build(lua).Follow(new WoWGuid(0x99)));
    }

    [Fact]
    public void HealthOutsideTheSensibleRangeIsClamped()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Member("party1", 0x11, "High", health: 250d),
            Member("party2", 0x22, "Low", health: -5d));

        IReadOnlyList<PartyMember> members = Build(lua).Members;

        Assert.Equal(100d, members.Single(m => m.Name == "High").HealthPercent);
        Assert.Equal(0d, members.Single(m => m.Name == "Low").HealthPercent);
    }

    [Fact]
    public void TheDeadAndTheOfflineAreReportedAsSuch()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Member("party1", 0x11, "Ghost", alive: false),
            Member("party2", 0x22, "Gone", online: false));

        Dictionary<ulong, Vector3> positions = new()
        {
            [0x11] = new Vector3(105f, 100f, 50f),
            [0x22] = new Vector3(105f, 100f, 50f),
        };

        IReadOnlyList<PartyMember> members = Build(lua, positions).Members;

        Assert.False(members.Single(m => m.Name == "Ghost").CanBeHelped());
        Assert.False(members.Single(m => m.Name == "Gone").CanBeHelped());
    }

    [Fact]
    public void RoleIsASettingRatherThanAReading()
    {
        // The client may know; this project has not verified how to ask, and a bot wrong about
        // this tanks in cloth.
        LuaPartyState party = Build(FakeLua.Typical335a());

        Assert.Equal(PartyRole.None, party.MyRole);

        party.MyRole = PartyRole.Healer;

        Assert.Equal(PartyRole.Healer, party.MyRole);
    }

    [Theory]
    [InlineData("0x0000000000000011", 0x11UL, true)]
    [InlineData("0X00000000000000FF", 0xFFUL, true)]
    [InlineData("11", 0x11UL, true)]
    [InlineData("0x0000000000000000", 0UL, false)]
    [InlineData("notaguid", 0UL, false)]
    [InlineData("", 0UL, false)]
    public void GuidsAreReadTheWayTheClientWritesThem(string text, ulong expected, bool ok)
    {
        Assert.Equal(ok, LuaPartyState.TryParseGuid(text, out WoWGuid guid));
        Assert.Equal(expected, guid.Value);
    }

    [Fact]
    public void TheReadScriptCountsThePartyTheWayThisExpansionDoes()
    {
        // 3.3.5a counts the party as the members other than the character, which is the
        // opposite of later versions. Getting this wrong loses the last member.
        Assert.Contains("GetNumPartyMembers()", LuaPartyState.ReadScript, StringComparison.Ordinal);
        Assert.Contains("\"party\"", LuaPartyState.ReadScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ARaidIsReadFromTheRaidUnitsRatherThanTheParty()
    {
        // A character in a raid is also in a party as far as GetNumPartyMembers is concerned,
        // so the raid check has to come first — otherwise the bot reports four people out of
        // twenty-five.
        Assert.Contains("GetNumRaidMembers()", LuaPartyState.ReadScript, StringComparison.Ordinal);
        Assert.Contains("raid > 0 and raid or", LuaPartyState.ReadScript, StringComparison.Ordinal);
        Assert.Contains("raid > 0 and \"raid\" or \"party\"", LuaPartyState.ReadScript, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCharacterIsSkippedInARaidBecauseRaidUnitsIncludeIt()
    {
        // party1..N never includes the character; raid1..N does, and without the check it ends
        // up in its own group list — where a healer would happily heal it twice.
        Assert.Contains("not UnitIsUnit(unit, \"player\")", LuaPartyState.ReadScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ARaidIsReadTheSameWayOnceTheScriptHasRun()
    {
        // The rows come back in the same shape whichever set of unit ids produced them.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Member("raid3", 0x33, "Tank", leader: true),
            Member("raid7", 0x77, "Healer"));

        LuaPartyState party = Build(lua, new Dictionary<ulong, Vector3>
        {
            [0x33] = new Vector3(110f, 100f, 50f),
            [0x77] = new Vector3(120f, 100f, 50f),
        });

        Assert.Equal(2, party.Members.Count);
        Assert.True(party.Follow(new WoWGuid(0x33)));
        Assert.Contains(lua.Asked, a => a.Contains("FollowUnit(\"raid3\")", StringComparison.Ordinal));
    }
}
