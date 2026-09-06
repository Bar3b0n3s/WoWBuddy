namespace WoWBuddy.BotBases.Battlegrounds;

/// <summary>Where the character is in the queue for a battleground.</summary>
/// <remarks>
/// These mirror the four states the 3.3.5a client reports for a battlefield slot. The live
/// implementation reads them from <c>GetBattlefieldStatus</c>, which returns one of "none",
/// "queued", "confirm" or "active".
/// </remarks>
public enum BattlegroundStatus
{
    /// <summary>Not queued for anything.</summary>
    None = 0,

    /// <summary>In the queue, waiting.</summary>
    Queued,

    /// <summary>A place is ready and the invitation is waiting to be accepted.</summary>
    Confirmed,

    /// <summary>Inside a battleground.</summary>
    Active,
}

/// <summary>
/// Queueing for and sitting inside a battleground.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Lua behind these verbs is not verified.</b> Queueing on 3.3.5a goes through the PvP
/// frame — asking the server for instance information, then joining a slot, then accepting the
/// port when it is offered — and this project has not confirmed the exact call signatures
/// against a live client. The interface is therefore written as intentions rather than as calls,
/// so that getting the Lua right later changes one adapter and nothing else.
/// </para>
/// <para>
/// TODO: verify <c>RequestBattlegroundInstanceInfo</c>, <c>JoinBattlefield</c>,
/// <c>GetBattlefieldStatus</c>, <c>AcceptBattlefieldPort</c> and <c>LeaveBattlefield</c> on
/// 12340 — argument order, whether the index is zero or one based, and whether any of them are
/// protected. The Lua console in the inspector is the place to do it; see
/// <c>docs/phase-8-manual-test.md</c>.
/// </para>
/// </remarks>
public interface IBattlegroundActions
{
    /// <summary>Where the character is in the queue.</summary>
    BattlegroundStatus Status { get; }

    /// <summary>True when the character is standing inside a battleground.</summary>
    bool IsInside { get; }

    /// <summary>True when the match has begun and the gates are open.</summary>
    bool HasStarted { get; }

    /// <summary>True when the match is over and the bot should leave.</summary>
    bool IsFinished { get; }

    /// <summary>The battleground the character is queued for or inside, for logs.</summary>
    string Name { get; }

    /// <summary>Joins the queue for a battleground by name.</summary>
    /// <returns>False when it could not be queued for.</returns>
    bool Queue(string name);

    /// <summary>Accepts a waiting invitation.</summary>
    bool AcceptInvitation();

    /// <summary>Leaves the battleground.</summary>
    bool Leave();
}
