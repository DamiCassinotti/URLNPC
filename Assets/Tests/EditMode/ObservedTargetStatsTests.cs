using NUnit.Framework;
using UnityEngine;

// ObservedTargetStats: the visible-only accumulators behind the snapshot's
// observed-player stats (#123). The sensory contract's acceptance test: every
// figure freezes the moment the target is unseen.
public class ObservedTargetStatsTests
{
    [Test]
    public void NothingObserved_ReportsZerosRatherThanDividingByThem()
    {
        var stats = new ObservedTargetStats();

        Assert.That(stats.VisibleSeconds, Is.Zero);
        Assert.That(stats.MeanEngagementDistance, Is.Zero);
        Assert.That(stats.MeanSpeed, Is.Zero);
        Assert.That(stats.ShotsPer10Seconds, Is.Zero);
    }

    [Test]
    public void StatsFreeze_WhileTheTargetIsUnseen()
    {
        var stats = new ObservedTargetStats();
        stats.Sample(true, Vector3.zero, 10f, 1f);
        stats.Sample(true, Vector3.zero, 10f, 1f);

        // The target keeps moving and shooting out of sight; none of it lands.
        for (int i = 0; i < 100; i++)
        {
            stats.Sample(false, new Vector3(i, 0f, 0f), 50f, 1f);
            stats.RecordShot();
        }

        Assert.That(stats.VisibleSeconds, Is.EqualTo(2f).Within(1e-5f));
        Assert.That(stats.MeanEngagementDistance, Is.EqualTo(10f).Within(1e-5f));
        Assert.That(stats.MeanSpeed, Is.Zero, "watched standing still, never watched walking");
        Assert.That(stats.ShotsHeard, Is.Zero);
    }

    [Test]
    public void MeanEngagementDistance_IsTimeWeighted()
    {
        // 10 m for 1 s, then 20 m for 3 s: the mean weighs the seconds, not the
        // samples, so a variable frame rate doesn't skew it.
        var stats = new ObservedTargetStats();
        stats.Sample(true, Vector3.zero, 10f, 1f);
        stats.Sample(true, Vector3.zero, 20f, 3f);

        Assert.That(stats.MeanEngagementDistance, Is.EqualTo(17.5f).Within(1e-5f));
    }

    [Test]
    public void Speed_IsMeasuredOverChainedVisibleSamplesOnly()
    {
        var stats = new ObservedTargetStats();
        // First sample of the interval: no previous position, no speed yet.
        stats.Sample(true, new Vector3(0f, 0f, 0f), 10f, 1f);
        Assert.That(stats.MeanSpeed, Is.Zero);

        // Watched walking 2 m over 1 s.
        stats.Sample(true, new Vector3(2f, 0f, 0f), 10f, 1f);
        Assert.That(stats.MeanSpeed, Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void ReacquiringTheTarget_DoesNotCountTheUnseenJumpAsSpeed()
    {
        var stats = new ObservedTargetStats();
        stats.Sample(true, new Vector3(0f, 0f, 0f), 10f, 1f);
        stats.Sample(true, new Vector3(1f, 0f, 0f), 10f, 1f);

        // Lost, crossed the arena unseen, reacquired 30 m away.
        stats.Sample(false, Vector3.zero, 0f, 5f);
        stats.Sample(true, new Vector3(31f, 0f, 0f), 20f, 1f);
        Assert.That(stats.MeanSpeed, Is.EqualTo(1f).Within(1e-5f),
            "the jump across the unseen interval is not a watched move");

        // The chain rebuilds from the reacquisition point.
        stats.Sample(true, new Vector3(34f, 0f, 0f), 20f, 1f);
        Assert.That(stats.MeanSpeed, Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void Speed_IgnoresVerticalMovement()
    {
        // A hop onto a crate isn't walking speed, and the two body origins sit
        // at different heights (#103).
        var stats = new ObservedTargetStats();
        stats.Sample(true, new Vector3(0f, 0f, 0f), 10f, 1f);
        stats.Sample(true, new Vector3(0f, 2f, 0f), 10f, 1f);

        Assert.That(stats.MeanSpeed, Is.Zero);
    }

    [Test]
    public void Shots_CountOnlyWhileVisible_AndNormalizePerTenSeconds()
    {
        var stats = new ObservedTargetStats();
        stats.RecordShot(); // before any sighting — dropped
        Assert.That(stats.ShotsHeard, Is.Zero);

        stats.Sample(true, Vector3.zero, 10f, 5f);
        stats.RecordShot();
        stats.RecordShot();

        Assert.That(stats.ShotsHeard, Is.EqualTo(2));
        Assert.That(stats.ShotsPer10Seconds, Is.EqualTo(4f).Within(1e-5f),
            "2 shots over 5 visible seconds is 4 per 10");
    }

    [Test]
    public void ZeroDelta_AccumulatesNoTime()
    {
        var stats = new ObservedTargetStats();
        stats.Sample(true, Vector3.zero, 10f, 0f);

        Assert.That(stats.VisibleSeconds, Is.Zero);
        Assert.That(stats.TargetVisible, Is.True, "the sighting itself still registers");
    }

    [Test]
    public void Reset_StartsTheNextEpisodeBlank()
    {
        var stats = new ObservedTargetStats();
        stats.Sample(true, Vector3.zero, 10f, 1f);
        stats.Sample(true, new Vector3(2f, 0f, 0f), 10f, 1f);
        stats.RecordShot();
        stats.Reset();

        Assert.That(stats.TargetVisible, Is.False);
        Assert.That(stats.VisibleSeconds, Is.Zero);
        Assert.That(stats.ShotsHeard, Is.Zero);
        Assert.That(stats.MeanEngagementDistance, Is.Zero);
        Assert.That(stats.MeanSpeed, Is.Zero);

        // And the speed chain is broken: the first sample after the reset must
        // not measure against last episode's position.
        stats.Sample(true, new Vector3(50f, 0f, 0f), 10f, 1f);
        Assert.That(stats.MeanSpeed, Is.Zero);
    }
}
