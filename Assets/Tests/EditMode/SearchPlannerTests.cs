using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// SearchPlanner: which way the sweep goes when the target is unseen, and when a
// leg is given up on (#93).
public class SearchPlannerTests
{
    static SearchPlanner Fresh() => new SearchPlanner
    {
        cellSize = 8f,
        sweptWeight = 0.35f,
        arrivalRadius = 2f,
        legTimeoutSeconds = 4f,
        progressEpsilon = 0.5f,
    };

    static Vector3 At(float x, float z) => new Vector3(x, 0f, z);

    static List<Vector3> Candidates(params Vector3[] points) => new List<Vector3>(points);

    [Test]
    public void WithNoLeg_ItNeedsAWaypoint()
    {
        Assert.That(Fresh().NeedsWaypoint(30f, 0f), Is.True);
    }

    [Test]
    public void ItWalksToTheFarthestCandidate()
    {
        var planner = Fresh();
        Assert.That(planner.TryChoose(Vector3.zero, Candidates(At(5f, 0f), At(30f, 0f), At(12f, 0f)),
                                      0f, out Vector3 waypoint), Is.True);
        Assert.That(waypoint, Is.EqualTo(At(30f, 0f)));
        Assert.That(planner.Waypoint, Is.EqualTo(At(30f, 0f)));
        Assert.That(planner.HasWaypoint, Is.True);
    }

    [Test]
    public void SweptGroundLosesToUnsweptGroundEvenWhenItIsFurther()
    {
        var planner = Fresh();
        planner.MarkSwept(At(40f, 0f));
        // 40 m swept scores 14, 20 m unswept scores 20.
        planner.TryChoose(Vector3.zero, Candidates(At(40f, 0f), At(20f, 0f)), 0f, out Vector3 waypoint);
        Assert.That(waypoint, Is.EqualTo(At(20f, 0f)));
    }

    [Test]
    public void OnceEverythingIsSweptItStillPicksTheFarthest()
    {
        var planner = Fresh();
        planner.MarkSwept(At(40f, 0f));
        planner.MarkSwept(At(20f, 0f));
        planner.TryChoose(Vector3.zero, Candidates(At(20f, 0f), At(40f, 0f)), 0f, out Vector3 waypoint);
        Assert.That(waypoint, Is.EqualTo(At(40f, 0f)));
    }

    [Test]
    public void MarkingSweptCountsPatches_NotSteps()
    {
        var planner = Fresh();
        planner.MarkSwept(At(1f, 1f));
        planner.MarkSwept(At(2f, 2f)); // same 8 m cell
        Assert.That(planner.SweptCells, Is.EqualTo(1));
        planner.MarkSwept(At(9f, 1f));
        Assert.That(planner.SweptCells, Is.EqualTo(2));
        // Negative coordinates are their own cells, not folded onto the positive ones.
        planner.MarkSwept(At(-1f, -1f));
        Assert.That(planner.SweptCells, Is.EqualTo(3));
    }

    [Test]
    public void AWalkedLegIsDone()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        Assert.That(planner.NeedsWaypoint(10f, 1f), Is.False);
        Assert.That(planner.NeedsWaypoint(1.5f, 2f), Is.True, "inside the arrival radius");
    }

    // The caller measures along the path, so a leg routed the long way around a
    // building can report further to walk than the straight line it was picked
    // on. That is the first reading, not a leg already failing to make headway.
    [Test]
    public void TheFirstReadingSetsTheBar_NotTheStraightLine()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        // A 45 m path to a waypoint 30 m away, walked down steadily.
        float now = 0f;
        for (float remaining = 45f; remaining > 3f; remaining -= 1f)
        {
            now += Step;
            Assert.That(planner.NeedsWaypoint(remaining, now), Is.False,
                $"{remaining:0} m of path left is progress, not a stall");
        }
    }

    // One decision step. The planner only hears from the adapter on the steps
    // the policy picks Wander, so the tests drive it at that cadence.
    const float Step = 0.1f;

    // Hold position and keep ticking; how long until the leg is given up on.
    // Granular to a tick, so the assertions allow a couple either way.
    static float SecondsUntilAbandoned(SearchPlanner planner, float remaining, float from,
                                       float tick = Step)
    {
        float now = from;
        for (int i = 0; i < 500; i++)
        {
            now += tick;
            if (planner.NeedsWaypoint(remaining, now)) return now - from;
        }
        return float.NaN;
    }

    [Test]
    public void ALegThatStopsMakingHeadwayIsAbandoned()
    {
        // A cross-arena destination can come back as a partial path that leaves
        // the agent against geometry with a path it still considers valid.
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        Assert.That(SecondsUntilAbandoned(planner, 20f, 0f), Is.EqualTo(4f).Within(0.3f));
    }

    [Test]
    public void HeadwayRestartsTheStallTimer()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        float now = 0f;
        for (int i = 1; i <= 20; i++) // 2 s of walking, a metre a step
        {
            now += Step;
            Assert.That(planner.NeedsWaypoint(30f - i, now), Is.False, $"step {i} was headway");
        }
        // Jitter under the epsilon is not headway, so the timer runs from here.
        Assert.That(SecondsUntilAbandoned(planner, 9.9f, now), Is.EqualTo(4f).Within(0.3f));
    }

    // The policy picks a primitive per step, so a leg can go seconds without a
    // Wander step to drive it. That is another primitive at the wheel, not the
    // leg stalling — counting it would drop legs that were never attempted.
    [Test]
    public void AGapWithNoWanderStepIsNotStalling()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);

        // The same distance to walk throughout, so after the first reading sets
        // the bar nothing here is headway.
        planner.NeedsWaypoint(32f, 0.1f);
        Assert.That(planner.NeedsWaypoint(32f, 6.1f), Is.False, "six seconds of some other primitive");
        // The gap cost it one step of the budget, not six seconds of it.
        Assert.That(SecondsUntilAbandoned(planner, 32f, 6.1f), Is.EqualTo(3.5f).Within(0.3f));
    }

    // ...but the gaps still have to add up, or a leg the policy only comes back
    // to every few steps would never expire — and a stranded one is exactly
    // what the timeout is for.
    [Test]
    public void RepeatedGapsStillExpireAStrandedLeg()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);

        // The policy comes back to Wander every 0.6 s and there is never any
        // less of the leg to walk: the budget goes at a capped 0.5 s a call.
        float seconds = SecondsUntilAbandoned(planner, 40f, 0f, tick: 0.6f);
        Assert.That(seconds, Is.Not.NaN, "a stranded leg must be given up on eventually");
        Assert.That(seconds, Is.GreaterThan(4f), "and not faster than its budget");
        Assert.That(seconds, Is.LessThan(8f), "nor an order of magnitude later");
    }

    [Test]
    public void WithNoCandidatesTheCurrentLegStands()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        Assert.That(planner.TryChoose(Vector3.zero, Candidates(), 1f, out _), Is.False);
        Assert.That(planner.TryChoose(Vector3.zero, null, 1f, out _), Is.False);
        Assert.That(planner.Waypoint, Is.EqualTo(At(30f, 0f)));
        Assert.That(planner.HasWaypoint, Is.True);
    }

    [Test]
    public void ADegenerateCellSizeOrCoordinateSweepsNothing()
    {
        var offGrid = Fresh();
        offGrid.cellSize = 0f;
        offGrid.MarkSwept(At(1f, 1f));
        Assert.That(offGrid.SweptCells, Is.EqualTo(0));

        var planner = Fresh();
        planner.MarkSwept(new Vector3(float.NaN, 0f, 1f));
        Assert.That(planner.SweptCells, Is.EqualTo(0));
    }

    [Test]
    public void ResetClearsTheSweepAndTheLeg()
    {
        var planner = Fresh();
        planner.MarkSwept(At(1f, 1f));
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        planner.Reset();
        Assert.That(planner.SweptCells, Is.EqualTo(0));
        Assert.That(planner.HasWaypoint, Is.False);
        Assert.That(planner.NeedsWaypoint(30f, 0f), Is.True);
    }

    [Test]
    public void HeightIsIgnoredWhenScoringCandidates()
    {
        // Arena points sit on stairs and platforms; how far a leg goes is a
        // distance across the floor, not through it.
        var planner = Fresh();
        planner.TryChoose(Vector3.zero,
                          Candidates(new Vector3(10f, 6f, 0f), new Vector3(12f, 0f, 0f)), 0f,
                          out Vector3 waypoint);
        Assert.That(waypoint, Is.EqualTo(new Vector3(12f, 0f, 0f)));
    }
}
