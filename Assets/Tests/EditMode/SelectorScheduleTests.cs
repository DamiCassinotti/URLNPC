using NUnit.Framework;

// The mode-selector cadence (issue #124): an immediate first decision, the
// periodic ones after, event triggers gated by their own dwell so a fight
// can't thrash the mode, and the consecutive-failure count that hands the
// channel to the fallback.
public class SelectorScheduleTests
{
    static SelectorSchedule NewSchedule() => new SelectorSchedule
    {
        DecisionPeriodSeconds = 5f,
        MinEventDwellSeconds = 2f,
        LowHealthFraction = 0.35f,
        FailuresBeforeFallback = 3,
    };

    // Nothing happening: unseen, unhurt, full HP.
    static bool Quiet(SelectorSchedule schedule, float now)
    {
        return schedule.ShouldIssue(now, targetVisible: false, recentlyDamaged: false, healthFraction: 1f);
    }

    [Test]
    public void FirstAsk_IssuesImmediately()
    {
        Assert.That(Quiet(NewSchedule(), 0f), Is.True,
            "a fresh episode must not wait a full period for its first decision");
    }

    [Test]
    public void UntilMarkedIssued_TheAskStandsOpen()
    {
        SelectorSchedule schedule = NewSchedule();
        Assert.That(Quiet(schedule, 0f), Is.True);
        // Not marked (the driver had nothing to run): the ask must not be lost.
        Assert.That(Quiet(schedule, 0.02f), Is.True);
    }

    [Test]
    public void NothingHappening_IssuesOncePerPeriod()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);

        for (float t = 0.5f; t < 5f; t += 0.5f)
        {
            Assert.That(Quiet(schedule, t), Is.False, $"nothing changed at t={t}");
        }
        Assert.That(Quiet(schedule, 5f), Is.True);
    }

    [Test]
    public void VisibilityFlip_TriggersAnEarlyDecision_BothWays()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);

        Assert.That(schedule.ShouldIssue(3f, true, false, 1f), Is.True, "sight gained is an event");
        schedule.MarkIssued(3f);

        Assert.That(schedule.ShouldIssue(4f, true, false, 1f), Is.False, "still visible is not");
        Assert.That(schedule.ShouldIssue(6f, false, false, 1f), Is.True, "sight lost is an event too");
    }

    [Test]
    public void AnEventInsideTheDwell_IsDroppedNotDeferred()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);

        Assert.That(schedule.ShouldIssue(1f, true, false, 1f), Is.False, "inside the event dwell");
        // The dwell has passed but the edge is gone; the periodic call at 5
        // reads the same state soon enough.
        Assert.That(schedule.ShouldIssue(3f, true, false, 1f), Is.False);
        Assert.That(schedule.ShouldIssue(5f, true, false, 1f), Is.True, "the period still fires");
    }

    [Test]
    public void Damage_TriggersOnTheRisingEdgeOnly()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);

        Assert.That(schedule.ShouldIssue(2.5f, false, true, 1f), Is.True, "just got hit");
        schedule.MarkIssued(2.5f);

        // DamageMemory holds the flag for a while; a held flag is not a new hit.
        Assert.That(schedule.ShouldIssue(5f, false, true, 1f), Is.False);
        // The flag lapsing is not news either.
        Assert.That(schedule.ShouldIssue(6f, false, false, 1f), Is.False);
    }

    [Test]
    public void LowHealthCrossing_Triggers()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);

        Assert.That(schedule.ShouldIssue(3f, false, false, 0.3f), Is.True, "dropped under the low-water mark");
    }

    [Test]
    public void TheFirstSample_SeedsWithoutTriggering()
    {
        SelectorSchedule schedule = NewSchedule();
        // Episode starts already in sight and hurt: a starting condition, not
        // an event — the first ask issues on its own rule.
        Assert.That(schedule.ShouldIssue(0f, true, true, 0.2f), Is.True);
        schedule.MarkIssued(0f);
        Assert.That(schedule.ShouldIssue(2.5f, true, true, 0.2f), Is.False,
            "the unchanged state must not read as an edge against defaults");
    }

    [Test]
    public void ConsecutiveFailures_HandTheChannelToTheFallback()
    {
        SelectorSchedule schedule = NewSchedule();
        schedule.RecordFailure();
        schedule.RecordFailure();
        Assert.That(schedule.FallbackActive, Is.False);
        schedule.RecordFailure();
        Assert.That(schedule.FallbackActive, Is.True);
        Assert.That(schedule.ConsecutiveFailures, Is.EqualTo(3));
    }

    [Test]
    public void ASuccess_ResetsTheCountButNotTheTakeover()
    {
        SelectorSchedule schedule = NewSchedule();
        for (int i = 0; i < 3; i++) schedule.RecordFailure();
        schedule.RecordSuccess();

        Assert.That(schedule.ConsecutiveFailures, Is.Zero);
        Assert.That(schedule.FallbackActive, Is.True,
            "successes after the switch come from the fallback and say nothing about the primary");
    }

    [Test]
    public void ASuccessBeforeTheThreshold_KeepsThePrimary()
    {
        SelectorSchedule schedule = NewSchedule();
        schedule.RecordFailure();
        schedule.RecordFailure();
        schedule.RecordSuccess();
        schedule.RecordFailure();
        schedule.RecordFailure();

        Assert.That(schedule.FallbackActive, Is.False, "the failures were not consecutive");
    }

    [Test]
    public void ZeroThreshold_NeverFailsOver()
    {
        SelectorSchedule schedule = NewSchedule();
        schedule.FailuresBeforeFallback = 0;
        for (int i = 0; i < 10; i++) schedule.RecordFailure();

        Assert.That(schedule.FallbackActive, Is.False);
    }

    [Test]
    public void Reset_StartsTheNextEpisodeFresh()
    {
        SelectorSchedule schedule = NewSchedule();
        Quiet(schedule, 0f);
        schedule.MarkIssued(0f);
        for (int i = 0; i < 3; i++) schedule.RecordFailure();

        schedule.Reset();

        Assert.That(schedule.FallbackActive, Is.False, "a recovered selector gets retried next episode");
        Assert.That(schedule.ConsecutiveFailures, Is.Zero);
        Assert.That(Quiet(schedule, 100f), Is.True, "the first ask of the new episode is immediate");
    }
}
