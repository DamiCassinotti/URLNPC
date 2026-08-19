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

    // Give up on a leg that hasn't got any closer for this long. A cross-arena
    // destination can come back as a partial path that strands the agent
    // against geometry with a path it still considers valid, and without this
    // it would stand there for the rest of the round.
    public float legTimeoutSeconds = 4f;

    // Slack on "got closer", so per-step jitter doesn't keep a stalled leg alive.
    public float progressEpsilon = 0.5f;

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
        lastProgressTime = 0f;
    }

    float bestDistance;
    float lastProgressTime;

    // The patch the body is standing in has been looked at.
    public void MarkSwept(Vector3 position)
    {
        if (cellSize <= 0f) return;
        swept.Add(Cell(position));
    }

    // Is the current leg done — never had one, walked it, or stopped making
    // headway? Also advances the stall timer, so call it once per step.
    public bool NeedsWaypoint(Vector3 position, float now)
    {
        if (!HasWaypoint) return true;
        float distance = FlatDistance(position, Waypoint);
        if (distance <= arrivalRadius) return true;
        if (distance < bestDistance - progressEpsilon)
        {
            bestDistance = distance;
            lastProgressTime = now;
            return false;
        }
        return now - lastProgressTime >= legTimeoutSeconds;
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
            if (cellSize > 0f && swept.Contains(Cell(candidates[i]))) score *= sweptWeight;
            if (score > bestScore)
            {
                bestScore = score;
                waypoint = candidates[i];
            }
        }

        Waypoint = waypoint;
        HasWaypoint = true;
        bestDistance = FlatDistance(from, waypoint);
        lastProgressTime = now;
        return true;
    }

    long Cell(Vector3 position)
    {
        long cellX = (long)Mathf.Floor(position.x / cellSize);
        long cellZ = (long)Mathf.Floor(position.z / cellSize);
        return (cellX << 32) ^ (cellZ & 0xffffffffL);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
