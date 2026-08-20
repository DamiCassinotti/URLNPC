using NUnit.Framework;

// ModeComplianceTracker: the per-mode compliance rules and the per-episode
// tally EnemyAgent flushes to TensorBoard and the telemetry log (#45).
public class ModeComplianceTrackerTests
{
    static ModeComplianceTracker Fresh() => new ModeComplianceTracker { movementDeadband = 0.05f };

    // Out of engagement range and long out of contact, so a mode's own rule is
    // what decides the step rather than the ranges the defaults hand it.
    static ComplianceSample Step(NpcMode mode) =>
        new ComplianceSample { mode = mode, distanceToTarget = 40f, timeSinceSeen = float.PositiveInfinity };

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
        Assert.That(tracker.Compliant(Step(NpcMode.Hunt)), Is.False, "standing still out of range is not hunting");
    }

    [Test]
    public void Hunt_CompliesWhileHoldingAtARangeItCanShootFrom()
    {
        // The cooldown is five decision steps long, so a hunter that has closed
        // and is trading shots is neither moving nor firing on most of them
        // (#91).
        var tracker = Fresh();
        var inRange = Step(NpcMode.Hunt);
        inRange.targetVisible = true;
        inRange.distanceToTarget = 10f;
        var outOfRange = inRange;
        outOfRange.distanceToTarget = 30f;
        var unseen = inRange;
        unseen.targetVisible = false;

        Assert.That(tracker.Compliant(inRange), Is.True);
        Assert.That(tracker.Compliant(outOfRange), Is.False, "far off, closing is still the job");
        Assert.That(tracker.Compliant(unseen), Is.False, "in range of someone it cannot see is not engaging");
    }

    [Test]
    public void Hunt_DoesNotCreditHoldingWhileBackingAwayWithoutFiring()
    {
        // #102's holding credit paid for every in-range step whatever the body
        // did with it, which floored the random control at 76% (#105).
        var tracker = Fresh();
        var backingOff = Step(NpcMode.Hunt);
        backingOff.targetVisible = true;
        backingOff.distanceToTarget = 10f;
        backingOff.closingDelta = -0.4f;
        var backingOffFiring = backingOff;
        backingOffFiring.shotFired = true;

        Assert.That(tracker.Compliant(backingOff), Is.False);
        Assert.That(tracker.Compliant(backingOffFiring), Is.True, "shooting is hunting wherever it walks");
    }

    [Test]
    public void Retreat_CompliesByOpeningDistanceOrByBreakingTheEyeLine()
    {
        var tracker = Fresh();
        var opening = Step(NpcMode.Retreat);
        opening.closingDelta = -0.4f;
        var closing = Step(NpcMode.Retreat);
        closing.closingDelta = 0.4f;
        var hidden = closing;
        hidden.inCover = true;
        var shooting = Step(NpcMode.Retreat);
        shooting.shotFired = true;

        Assert.That(tracker.Compliant(opening), Is.True);
        Assert.That(tracker.Compliant(closing), Is.False);
        Assert.That(tracker.Compliant(hidden), Is.True, "contact broken by cover is a retreat that arrived");
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
        fresh.inNewArea = true;
        fresh.distanceMoved = 0.3f;
        var trodden = Step(NpcMode.Patrol);
        trodden.distanceMoved = 0.3f;
        var parked = fresh;
        parked.distanceMoved = 0f;
        // A step is a physics step: at 3.5 m/s the body covers ~0.07 m, so the
        // walking test has to sit well under the closing deadband or a body
        // that slows to turn reads as parked.
        var slow = fresh;
        slow.distanceMoved = 0.04f;

        Assert.That(tracker.Compliant(fresh), Is.True);
        Assert.That(tracker.Compliant(trodden), Is.False, "ground it has already been over");
        Assert.That(tracker.Compliant(parked), Is.False, "standing in fresh ground is not covering it");
        Assert.That(tracker.Compliant(slow), Is.True, "walking slowly is still walking");
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
    public void Hunt_IsOnlyScoredOnStepsTheTargetIsVisible()
    {
        var tracker = Fresh();
        Record(tracker, NpcMode.Hunt, closing: 0.4f, times: 2, visible: true);
        Record(tracker, NpcMode.Hunt, closing: 0f, times: 8, visible: false);

        Assert.That(tracker.Steps(NpcMode.Hunt), Is.EqualTo(10), "the raw count keeps every step");
        Assert.That(tracker.EligibleSteps(NpcMode.Hunt), Is.EqualTo(2));
        Assert.That(tracker.Rate(NpcMode.Hunt), Is.EqualTo(1f).Within(1e-6f),
            "a mode that did its job whenever it could see the target scores 1, not 0.2");
    }

    [Test]
    public void Retreat_IsScoredWhileThereIsSomeoneToBreakContactWith()
    {
        // Cover is what a retreat is aiming for, and cover ends visibility — so
        // scoring only the visible steps would have made the new rule
        // unreachable. The window past the last sighting is the denominator
        // instead.
        var tracker = Fresh();
        var justHidden = Step(NpcMode.Retreat);
        justHidden.timeSinceSeen = 1f;
        var longGone = Step(NpcMode.Retreat);
        longGone.timeSinceSeen = 30f;
        var visible = Step(NpcMode.Retreat);
        visible.targetVisible = true;
        visible.timeSinceSeen = 0f;

        Assert.That(tracker.Eligible(justHidden), Is.True);
        Assert.That(tracker.Eligible(visible), Is.True);
        Assert.That(tracker.Eligible(longGone), Is.False, "nobody in contact to retreat from");
        Assert.That(tracker.Eligible(Step(NpcMode.Retreat)), Is.False, "never seen is not contact either");
    }

    [Test]
    public void HoldCover_IsScoredWhileThereIsSomeoneToTakeCoverFrom()
    {
        // Scored over every step it measured the arena's ambient occlusion —
        // the target is unseen most of a round, so the whole map reads as
        // cover and a random walk outscored the heuristic bot (#105). The
        // window is PerceptionMemory's horizon: holding cover is worth credit
        // for as long as the threat is still known about.
        var tracker = Fresh();
        var stillKnown = Step(NpcMode.HoldCover);
        stillKnown.timeSinceSeen = 8f;
        var longGone = Step(NpcMode.HoldCover);
        longGone.timeSinceSeen = 30f;
        var visible = Step(NpcMode.HoldCover);
        visible.targetVisible = true;
        visible.timeSinceSeen = 0f;

        Assert.That(tracker.Eligible(stillKnown), Is.True);
        Assert.That(tracker.Eligible(visible), Is.True);
        Assert.That(tracker.Eligible(longGone), Is.False, "an empty arena is not cover");
        Assert.That(tracker.Eligible(Step(NpcMode.HoldCover)), Is.False, "never seen is not contact either");

        var retreating = stillKnown;
        retreating.mode = NpcMode.Retreat;
        Assert.That(tracker.Eligible(retreating), Is.False,
            "Retreat's window is the shorter one — rounding a corner, not holding a position");
    }

    [Test]
    public void Patrol_IsScoredOnEveryStep()
    {
        // Its rule needs no bearing on the target: covering new ground is
        // something the policy can do blind.
        var tracker = Fresh();
        Assert.That(tracker.Eligible(Step(NpcMode.Patrol)), Is.True);

        var covering = Step(NpcMode.Patrol);
        covering.inNewArea = true;
        covering.distanceMoved = 0.3f;
        tracker.Record(covering);
        tracker.Record(Step(NpcMode.Patrol));

        Assert.That(tracker.EligibleSteps(NpcMode.Patrol), Is.EqualTo(2));
        Assert.That(tracker.Rate(NpcMode.Patrol), Is.EqualTo(0.5f).Within(1e-6f));
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
    public void OnlyTheCoverRules_NeedTheCoverProbe()
    {
        // The flag costs a raycast, so the agent only produces it for the modes
        // whose rules read it.
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.HoldCover), Is.True);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Retreat), Is.True);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Hunt), Is.False);
        Assert.That(ModeComplianceTracker.ReadsCover(NpcMode.Patrol), Is.False);
    }

    static void Record(ModeComplianceTracker tracker, NpcMode mode, float closing, int times, bool visible = true)
    {
        var sample = Step(mode);
        sample.closingDelta = closing;
        sample.targetVisible = visible;
        if (visible) sample.timeSinceSeen = 0f;
        for (int i = 0; i < times; i++) tracker.Record(sample);
    }
}
