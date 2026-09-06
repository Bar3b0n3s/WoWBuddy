using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class FollowControllerTests
{
    private static FollowController Controller(float follow = 12f, float stop = 6f, float giveUp = 150f) =>
        new(new FollowSettings { FollowDistance = follow, StopDistance = stop, GiveUpDistance = giveUp });

    [Fact]
    public void StaysPutInsideTheFollowDistance()
    {
        Assert.Equal(FollowAction.Stay, Controller().Decide(8f));
    }

    [Fact]
    public void ClosesOnceTooFar()
    {
        Assert.Equal(FollowAction.Close, Controller().Decide(20f));
    }

    [Fact]
    public void KeepsGoingUntilProperlyCaughtUp()
    {
        // The whole point of the band. Stopping the moment the distance drops back below the
        // follow distance produces a character that starts and stops many times a second,
        // which wastes the tick, fights the movement controller, and looks exactly like a bot.
        FollowController controller = Controller();

        Assert.Equal(FollowAction.Close, controller.Decide(20f));
        Assert.Equal(FollowAction.Close, controller.Decide(11f));
        Assert.Equal(FollowAction.Close, controller.Decide(7f));
        Assert.Equal(FollowAction.Stay, controller.Decide(5f));

        // And having stopped, it does not set off again until it is genuinely too far.
        Assert.Equal(FollowAction.Stay, controller.Decide(11f));
        Assert.Equal(FollowAction.Close, controller.Decide(13f));
    }

    [Fact]
    public void GivesUpOnSomeoneWhoHasZonedOrHearthed()
    {
        Assert.Equal(FollowAction.GiveUp, Controller().Decide(400f));
    }

    [Fact]
    public void GivingUpAlsoStopsClosing()
    {
        FollowController controller = Controller();

        Assert.Equal(FollowAction.Close, controller.Decide(20f));
        Assert.Equal(FollowAction.GiveUp, controller.Decide(400f));
        Assert.False(controller.IsClosing);
    }

    [Fact]
    public void RefusesSettingsThatWouldOscillateForever()
    {
        // A stop distance above the follow distance means the character can never satisfy
        // both, so it would shuffle on the spot until stopped by hand.
        FollowController controller = Controller(follow: 5f, stop: 10f);

        Assert.Equal(FollowAction.GiveUp, controller.Decide(7f));
    }

    [Fact]
    public void AMissingMemberIsSomethingToGiveUpOn()
    {
        Assert.Equal(FollowAction.GiveUp, Controller().Decide((PartyMember?)null));
    }

    [Fact]
    public void ResetForgetsThatItWasClosing()
    {
        FollowController controller = Controller();

        Assert.Equal(FollowAction.Close, controller.Decide(20f));
        controller.Reset();

        Assert.False(controller.IsClosing);
        Assert.Equal(FollowAction.Stay, controller.Decide(11f));
    }

    [Fact]
    public void StandingOffBacksAwayAlongTheLineAlreadyStoodOn()
    {
        // Deliberately not a fixed compass offset: standing at a set point relative to the
        // tank puts a healer in the fire as often as out of it, and moving the shortest
        // distance to a workable spot is both safer and less obviously mechanical.
        Vector3 anchor = new(100f, 100f, 50f);
        Vector3 me = new(140f, 100f, 50f);

        Vector3 spot = FollowController.StandOff(anchor, me, 25f);

        Assert.Equal(125f, spot.X, 2);
        Assert.Equal(100f, spot.Y, 2);
        Assert.Equal(25f, anchor.Distance2D(spot), 2);
    }

    [Fact]
    public void StandingOffOnTopOfSomeoneStaysPut()
    {
        // No direction to back away in, so wait for something to move rather than pick one.
        Vector3 anchor = new(100f, 100f, 50f);

        Assert.Equal(anchor, FollowController.StandOff(anchor, anchor, 25f));
    }
}
