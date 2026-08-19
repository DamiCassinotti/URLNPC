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
        Assert.That(Fresh().NeedsWaypoint(Vector3.zero, 0f), Is.True);
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
        Assert.That(planner.NeedsWaypoint(At(20f, 0f), 1f), Is.False);
        Assert.That(planner.NeedsWaypoint(At(28.5f, 0f), 2f), Is.True, "inside the arrival radius");
    }

    [Test]
    public void ALegThatStopsMakingHeadwayIsAbandoned()
    {
        // A cross-arena destination can come back as a partial path that leaves
        // the agent against geometry with a path it still considers valid.
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        Assert.That(planner.NeedsWaypoint(At(10f, 0f), 1f), Is.False);
        Assert.That(planner.NeedsWaypoint(At(10.2f, 0f), 4f), Is.False, "not stalled long enough yet");
        Assert.That(planner.NeedsWaypoint(At(10.2f, 0f), 5.1f), Is.True);
    }

    [Test]
    public void HeadwayRestartsTheStallTimer()
    {
        var planner = Fresh();
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        planner.NeedsWaypoint(At(10f, 0f), 3f);
        // Jitter under the epsilon is not headway; a real metre is.
        Assert.That(planner.NeedsWaypoint(At(10.1f, 0f), 5f), Is.False);
        Assert.That(planner.NeedsWaypoint(At(12f, 0f), 6f), Is.False);
        Assert.That(planner.NeedsWaypoint(At(12f, 0f), 9f), Is.False, "the timer restarted at 6");
        Assert.That(planner.NeedsWaypoint(At(12f, 0f), 10.1f), Is.True);
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
    public void ResetClearsTheSweepAndTheLeg()
    {
        var planner = Fresh();
        planner.MarkSwept(At(1f, 1f));
        planner.TryChoose(Vector3.zero, Candidates(At(30f, 0f)), 0f, out _);
        planner.Reset();
        Assert.That(planner.SweptCells, Is.EqualTo(0));
        Assert.That(planner.HasWaypoint, Is.False);
        Assert.That(planner.NeedsWaypoint(Vector3.zero, 0f), Is.True);
    }

    [Test]
    public void HeightIsIgnored()
    {
        // Arena points sit on stairs and platforms; a leg is a distance across
        // the floor, not through it.
        var planner = Fresh();
        planner.TryChoose(Vector3.zero,
                          Candidates(new Vector3(10f, 6f, 0f), new Vector3(12f, 0f, 0f)), 0f,
                          out Vector3 waypoint);
        Assert.That(waypoint, Is.EqualTo(new Vector3(12f, 0f, 0f)));
        Assert.That(planner.NeedsWaypoint(new Vector3(12f, 5f, 0f), 1f), Is.True);
    }
}
