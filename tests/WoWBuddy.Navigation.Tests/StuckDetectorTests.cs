using WoWBuddy.Common.Geometry;
using WoWBuddy.Navigation.Movement;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

/// <summary>
/// Covers the thing that decides whether an overnight run works or spends six hours facing
/// a tree.
/// </summary>
public sealed class StuckDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Vector3 At(float x) => new(x, 0f, 0f);

    [Fact]
    public void AMovingCharacterIsNeverReportedStuck()
    {
        var detector = new StuckDetector();

        for (int i = 0; i < 20; i++)
        {
            StuckSeverity severity = detector.Update(At(i * 7f), Start.AddSeconds(i));
            Assert.Equal(StuckSeverity.Moving, severity);
        }
    }

    [Fact]
    public void StandingStillEscalatesThroughSuspicionToStuck()
    {
        var detector = new StuckDetector();
        detector.Update(At(0f), Start);

        Assert.Equal(StuckSeverity.Moving, detector.Update(At(0f), Start.AddSeconds(0.5)));
        Assert.Equal(StuckSeverity.Suspected, detector.Update(At(0f), Start.AddSeconds(2)));
        Assert.Equal(StuckSeverity.Stuck, detector.Update(At(0f), Start.AddSeconds(5)));
    }

    [Fact]
    public void SmallJitterDoesNotCountAsProgress()
    {
        // A character wedged against geometry still drifts a little.
        var detector = new StuckDetector();
        detector.Update(At(0f), Start);

        Assert.Equal(StuckSeverity.Stuck, detector.Update(At(0.3f), Start.AddSeconds(5)));
    }

    [Fact]
    public void RealProgressResetsTheClock()
    {
        var detector = new StuckDetector();
        detector.Update(At(0f), Start);
        detector.Update(At(0f), Start.AddSeconds(2));

        Assert.Equal(StuckSeverity.Moving, detector.Update(At(10f), Start.AddSeconds(3)));
        Assert.Equal(TimeSpan.Zero, detector.TimeWithoutProgress);
    }

    [Fact]
    public void RepeatedEpisodesEscalateToPersistent()
    {
        // A character that wriggles free and immediately gets stuck again is worse off than
        // one stuck once, and the escalation has to reflect that or the bot loops forever.
        var detector = new StuckDetector();
        DateTimeOffset now = Start;

        for (int episode = 0; episode < StuckDetector.PersistentEpisodeCount - 1; episode++)
        {
            detector.Update(At(episode * 100f), now);
            now = now.AddSeconds(5);
            Assert.Equal(StuckSeverity.Stuck, detector.Update(At(episode * 100f), now));

            detector.NoteRecoveryAttempt(At(episode * 100f), now);
            now = now.AddSeconds(1);
        }

        detector.Update(At(999f), now);
        now = now.AddSeconds(5);

        Assert.Equal(StuckSeverity.Persistent, detector.Update(At(999f), now));
    }

    [Fact]
    public void ARecoveryAttemptGivesAFreshWindowButRemembersTheEpisode()
    {
        var detector = new StuckDetector();
        detector.Update(At(0f), Start);
        detector.Update(At(0f), Start.AddSeconds(5));

        Assert.Equal(1, detector.Episodes);

        detector.NoteRecoveryAttempt(At(0f), Start.AddSeconds(5));

        Assert.Equal(StuckSeverity.Moving, detector.Severity);
        Assert.Equal(1, detector.Episodes);
    }

    [Fact]
    public void ResetForgetsEverythingBecauseANewPathSaysNothingAboutTheOldOne()
    {
        var detector = new StuckDetector();
        detector.Update(At(0f), Start);
        detector.Update(At(0f), Start.AddSeconds(5));

        detector.Reset();

        Assert.Equal(0, detector.Episodes);
        Assert.Equal(StuckSeverity.Moving, detector.Severity);
    }

    [Fact]
    public void TheFirstObservationEstablishesAReferenceRatherThanJudging()
    {
        var detector = new StuckDetector();

        Assert.Equal(StuckSeverity.Moving, detector.Update(At(0f), Start));
    }
}
