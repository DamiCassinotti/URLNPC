using System.Collections.Generic;
using UnityEngine;

// Where to look next when the NPC has no one in sight (issue #93). Engine-free
// so the sweep rules are testable without an arena: the caller supplies the
// candidate points and "now", this picks between them and says when the current
// leg is spent.
//
// The old wander picked a point within 10 m of itself every time it arrived,
// i.e. a random walk — in a 60x60 m arena that covers ground so slowly the
// round times out before the two ever meet. A leg here crosses the arena, and
// ground already swept this episode is worth a fraction of ground that isn't,
// so consecutive legs pull the NPC into the parts of the map it hasn't been in.
public class SearchPlanner
{
    // Side of the square patches "already swept" is counted in. Roughly the
    // width a walk-through actually clears, not the sight range: a corridor
    // walked end to end shouldn't mark the rooms either side of it.
    public float cellSize = 8f;

    // What a candidate in an already-swept cell is worth against one that isn't.
    // Not zero: late in an episode every cell is swept, and the sweep has to
    // keep going somewhere.
    public float sweptWeight = 0.35f;

    // Close enough to call the leg walked.
    public float arrivalRadius = 2f;

    // Give up on a leg that hasn't got any closer for this long a stretch of
    // being driven (see maxTickSeconds). A cross-arena
    // destination can come back as a partial path that strands the agent
    // against geometry with a path it still considers valid, and without this
    // it would stand there for the rest of the round.
    public float legTimeoutSeconds = 4f;

    // Slack on "got closer", so per-step jitter doesn't keep a stalled leg alive.
    public float progressEpsilon = 0.5f;

    // Most one call can contribute to the stall clock. The policy picks a
    // primitive per step, so a leg can go seconds without a Wander step to
    // drive it; that gap is another primitive at the wheel, not the leg
    // stalling, and charging it in full would expire legs that were never
    // attempted. A guard on the caller's step rate, not a tuning knob.
    public float maxTickSeconds = 0.5f;

    readonly HashSet<long> swept = new HashSet<long>();

    public bool HasWaypoint { get; private set; }
    public Vector3 Waypoint { get; private set; }
    public int SweptCells => swept.Count;

    public void Reset()
    {
        swept.Clear();
        HasWaypoint = false;
        Waypoint = Vector3.zero;
        bestDistance = 0f;
        stalledSeconds = 0f;
        lastTickTime = 0f;
    }

    float bestDistance;
    float stalledSeconds;
    float lastTickTime;

    // The patch the body is standing in has been looked at.
    public void MarkSwept(Vector3 position)
    {
        if (GridCell.TryPack(position.x, position.z, cellSize, out long cell)) swept.Add(cell);
    }

    // Is the current leg done — never had one, walked it, or stopped making
    // headway? Also advances the stall timer, so call it once per step.
    //
    // The distance is how far the body still has to walk, which the caller
    // measures: the arena is full of buildings, so a straight line to the
    // waypoint says nothing about whether a leg routed around one is going
    // anywhere (EnemyBehavior.RemainingToWaypoint reads it off the path).
    public bool NeedsWaypoint(float distanceToWaypoint, float now)
    {
        float sinceTick = now - lastTickTime;
        lastTickTime = now;
        if (!HasWaypoint) return true;

        float distance = distanceToWaypoint;
        if (distance <= arrivalRadius) return true;
        if (distance < bestDistance - progressEpsilon)
        {
            bestDistance = distance;
            stalledSeconds = 0f;
            return false;
        }
        // Charge the gap since the last call, capped at one step: a stretch
        // where another primitive was driving can't expire the leg on its own,
        // but it still counts for something. Cleared outright instead, a leg
        // the policy only comes back to every few steps would never expire at
        // all — and being stranded on a partial path is what the timeout is for.
        stalledSeconds += Mathf.Min(sinceTick, maxTickSeconds);
        return stalledSeconds >= legTimeoutSeconds;
    }

    // The farthest candidate, discounting the ones in ground already swept.
    // False when the caller had nothing to offer (no arena, no NavMesh), which
    // leaves the current leg standing rather than clearing it.
    public bool TryChoose(Vector3 from, IReadOnlyList<Vector3> candidates, float now, out Vector3 waypoint)
    {
        waypoint = Vector3.zero;
        if (candidates == null || candidates.Count == 0) return false;

        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            float score = FlatDistance(from, candidates[i]);
            if (GridCell.TryPack(candidates[i].x, candidates[i].z, cellSize, out long cell)
                && swept.Contains(cell))
            {
                score *= sweptWeight;
            }
            if (score > bestScore)
            {
                bestScore = score;
                waypoint = candidates[i];
            }
        }

        Waypoint = waypoint;
        HasWaypoint = true;
        // The first measurement sets the bar, whatever the caller measures in:
        // seeding it with the straight line would have a leg routed around a
        // building start out already "not making headway".
        bestDistance = float.PositiveInfinity;
        stalledSeconds = 0f;
        lastTickTime = now;
        return true;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
