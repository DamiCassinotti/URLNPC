using NUnit.Framework;

// ModeComplianceTracker: the per-mode compliance rules and the per-episode
// tally EnemyAgent flushes to TensorBoard and the telemetry log (#45).
public class ModeComplianceTrackerTests
{
    static ModeComplianceTracker Fresh() => new ModeComplianceTracker { movementDeadband = 0.05f };

    static ComplianceSample Step(NpcMode mode) => new ComplianceSample { mode = mode };

    [Test]
    public void Hunt_CompliesByClosingOrByShooting()
    {
        var tracker = Fresh();
        var closing = Step(NpcMode.Hunt);
        closing.closingDelta = 0.4f;
        var shooting = Step(NpcMode.Hunt);
        shooting.shotFired = true;
        shooting.closingDelta = -0.4f; // backing off, but trading shots

        Assert.That(tracker.Compliant(closing), Is.True);
        Assert.That(tracker.Compliant(shooting), Is.True);
        Assert.That(tracker.Compliant(Step(NpcMode.Hunt)), Is.False, "standing still is not hunting");
    }

    [Test]
    public void Retreat_CompliesOnlyByOpeningDistance()
    {
        var tracker = Fresh();
        var opening = Step(NpcMode.Retreat);
        opening.closingDelta = -0.4f;
        var closing = Step(NpcMode.Retreat);
        closing.closingDelta = 0.4f;
        var shooting = Step(NpcMode.Retreat);
        shooting.shotFired = true;

        Assert.That(tracker.Compliant(opening), Is.True);
        Assert.That(tracker.Compliant(closing), Is.False);
        Assert.That(tracker.Compliant(shooting), Is.False, "firing is Hunt's rule, not Retreat's");
    }

    [Test]
    public void MovementJitter_CountsForNeitherHuntNorRetreat()
    {
        // Rotation and NavMesh settling move the body a hair every step; that
        // is not closing, and it is not opening either.
        var tracker = Fresh();
        var drift = Step(NpcMode.Hunt);
        drift.closingDelta = 0.01f;
        Assert.That(tracker.Compliant(drift), Is.False);

        drift.mode = NpcMode.Retreat;
        drift.closingDelta = -0.01f;
        Assert.That(tracker.Compliant(drift), Is.False);
    }

    [Test]
    public void HoldCover_CompliesWhileTheEyeLineIsBroken()
    {
        var tracker = Fresh();
        var hidden = Step(NpcMode.HoldCover);
        hidden.inCover = true;
        var exposed = Step(NpcMode.HoldCover);
        exposed.closingDelta = -0.4f; // running away in the open is not cover

        Assert.That(tracker.Compliant(hidden), Is.True);
        Assert.That(tracker.Compliant(exposed), Is.False);
    }

    [Test]
    public void Patrol_CompliesOnStepsThatCoverNewGround()
    {
        var tracker = Fresh();
        var fresh = Step(NpcMode.Patrol);
        fresh.enteredNewArea = true;
        var trodden = Step(NpcMode.Patrol);
        trodden.closingDelta = 0.4f;

        Assert.That(tracker.Compliant(fresh), Is.True);
        Assert.That(tracker.Compliant(trodden), Is.False);
    }

    [Test]
    public void RateIsPerMode_AndModesDoNotBleedIntoEachOther()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 3);
        Record(tracker, NpcMode.Hunt, closing: -0.4f, times: 1);
        Record(tracker, NpcMode.Retreat, closing: -0.4f, times: 1);

        Assert.That(tracker.Steps(NpcMode.Hunt), Is.EqualTo(4));
        Assert.That(tracker.EligibleSteps(NpcMode.Hunt), Is.EqualTo(4));
        Assert.That(tracker.CompliantSteps(NpcMode.Hunt), Is.EqualTo(3));
        Assert.That(tracker.Rate(NpcMode.Hunt), Is.EqualTo(0.75f).Within(1e-6f));
        Assert.That(tracker.Rate(NpcMode.Retreat), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(tracker.TotalSteps, Is.EqualTo(5));
    }

    [Test]
    public void AModeNeverCommanded_HasNoStepsAndNoRate()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 1);

        Assert.That(tracker.Steps(NpcMode.Patrol), Is.Zero);
        Assert.That(tracker.Rate(NpcMode.Patrol), Is.EqualTo(0f), "no steps is reported as zero, not as a division by zero");
        Assert.That(tracker.ComplianceJson(), Does.Not.Contain("Patrol"));
    }

    [Test]
    public void Reset_StartsTheNextEpisodeEmpty()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 2);
        tracker.Reset();

        Assert.That(tracker.TotalSteps, Is.Zero);
        Assert.That(tracker.Steps(NpcMode.Hunt), Is.Zero);
        Assert.That(tracker.EligibleSteps(NpcMode.Hunt), Is.Zero);
        Assert.That(tracker.CompliantSteps(NpcMode.Hunt), Is.Zero);
        Assert.That(tracker.ComplianceJson(), Is.EqualTo("\"compliance\":{}"));
    }

    [Test]
    public void ComplianceJson_ReportsStepsAndRatePerCommandedMode()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 3);
        Record(tracker, NpcMode.Hunt, closing: 0f, times: 1);
        Record(tracker, NpcMode.Retreat, closing: 0.4f, times: 2);

        Assert.That(tracker.ComplianceJson(), Is.EqualTo(
            "\"compliance\":{\"Hunt\":{\"steps\":4,\"eligible\":4,\"compliant\":3,\"rate\":0.75},"
            + "\"Retreat\":{\"steps\":2,\"eligible\":2,\"compliant\":0,\"rate\":0}}"));
    }

    [Test]
    public void HuntAndRetreat_AreOnlyScoredOnStepsTheTargetIsVisible()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 2, visible: true);
        Record(tracker, NpcMode.Hunt, closing: 0f, times: 8, visible: false);

        Assert.That(tracker.Steps(NpcMode.Hunt), Is.EqualTo(10), "the raw count keeps every step");
        Assert.That(tracker.EligibleSteps(NpcMode.Hunt), Is.EqualTo(2));
        Assert.That(tracker.Rate(NpcMode.Hunt), Is.EqualTo(1f).Within(1e-6f),
            "a mode that did its job whenever it could see the target scores 1, not 0.2");

        Assert.That(ModeComplianceTracker.Eligible(new ComplianceSample { mode = NpcMode.Retreat, targetVisible = false }), Is.False);
        Assert.That(ModeComplianceTracker.Eligible(new ComplianceSample { mode = NpcMode.Retreat, targetVisible = true }), Is.True);
    }

    [Test]
    public void HoldCoverAndPatrol_AreScoredOnEveryStep()
    {
        // Neither rule needs a bearing on the target, so an unseen target is
        // not an excuse: breaking the eye-line and covering new ground are
        // things the policy can do blind.
        foreach (NpcMode mode in new[] { NpcMode.HoldCover, NpcMode.Patrol })
        {
            Assert.That(ModeComplianceTracker.Eligible(new ComplianceSample { mode = mode, targetVisible = false }), Is.True, mode.ToString());
        }

        var tracker = Fresh();
        var hidden = Step(NpcMode.HoldCover);
        hidden.inCover = true;
        tracker.Record(hidden);
        tracker.Record(Step(NpcMode.HoldCover));

        Assert.That(tracker.EligibleSteps(NpcMode.HoldCover), Is.EqualTo(2));
        Assert.That(tracker.Rate(NpcMode.HoldCover), Is.EqualTo(0.5f).Within(1e-6f));
    }

    [Test]
    public void AModeWithNoEligibleStep_ReportsItsStepsAndNoRate()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 3, visible: false);

        Assert.That(tracker.EligibleSteps(NpcMode.Hunt), Is.Zero);
        Assert.That(tracker.Rate(NpcMode.Hunt), Is.EqualTo(0f), "no eligible steps is reported as zero, not as a division by zero");
        Assert.That(tracker.ComplianceJson(), Is.EqualTo(
            "\"compliance\":{\"Hunt\":{\"steps\":3,\"eligible\":0,\"compliant\":0,\"rate\":0}}"),
            "the steps still show up, so a never-engaged mode is visible rather than missing");
    }

    [Test]
    public void OnlyHoldCover_NeedsTheCoverProbe()
    {
        // The flag costs a raycast, so the agent only produces it for the modes
        // whose rules read it.
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.HoldCover), Is.True);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Hunt), Is.False);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Retreat), Is.False);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Patrol), Is.False);
    }

    static void Record(ModeComplianceTracker tracker, NpcMode mode, float closing, int times, bool visible = true)
    {
        var sample = new ComplianceSample { mode = mode, closingDelta = closing, targetVisible = visible };
        for (int i = 0; i < times; i++) tracker.Record(sample);
    }
}
