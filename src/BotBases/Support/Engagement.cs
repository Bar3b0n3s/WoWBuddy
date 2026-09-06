using WoWBuddy.Behavior;

namespace WoWBuddy.BotBases.Support;

/// <summary>
/// Closing on the current target and opening the fight.
/// </summary>
/// <remarks>
/// Shared between the bot bases because getting it wrong is expensive in the same way for all
/// of them: the routine's own pull range decides where "close enough" is, which is what stops
/// a mage walking into melee for a fight it should open at thirty yards.
/// </remarks>
public static class Engagement
{
    /// <summary>Approaches the current target and pulls it.</summary>
    public static Node<IBotState> Build() =>
        new If<IBotState>(
            s => s.Target is { IsAlive: true },
            new PrioritySelector<IBotState>(
                new If<IBotState>(
                    s => s.Target!.Value.Distance > s.Routine.PullRange,
                    new Do<IBotState>(s =>
                        s.MoveTo(s.Target!.Value.Position) ? RunStatus.Running : RunStatus.Failure)
                    { Name = "Approach" }),

                new Do<IBotState>(s =>
                {
                    s.StopMoving();
                    s.Routine.PetControl(s.Combat);
                    s.Routine.Pull(s.Combat);
                    return RunStatus.Running;
                })
                { Name = "Pull" })
            { Name = "Engage" })
        { Name = "Has a target" };
}
