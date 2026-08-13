using System.Collections.Generic;
using UnityEngine;

// Per-episode movement bookkeeping the mode reward columns consume: how much
// ground was closed on the target since the last step (Hunt/Retreat) and whether
// the body just entered a patch of the arena it hasn't been in yet (Patrol).
// Both are pure state over positions, so EnemyAgent only has to feed it the
// distance and its own position once per step and reset it on episode begin.
public class EpisodeProgress
{
    // Side of the square patches the new-area reward counts. Big enough that
    // milling about in place isn't new ground, small enough that crossing an
    // arena pays several times.
    public float areaCellSize = 6f;

    // A respawn or a NavMesh warp moves the body metres in a single step; that
    // is not progress, so the delta is capped at what walking can produce.
    public float maxClosingDelta = 2f;

    readonly HashSet<long> visited = new HashSet<long>();
    float previousDistance = float.NaN;

    public int VisitedAreas => visited.Count;

    public void Reset()
    {
        visited.Clear();
        previousDistance = float.NaN;
    }

    // Metres closed on the target since the previous call; negative is opening
    // distance. Zero on the first call of an episode (no baseline) and whenever
    // the distance isn't a real number (no target).
    public float Closing(float distanceToTarget)
    {
        float previous = previousDistance;
        if (float.IsNaN(distanceToTarget) || float.IsInfinity(distanceToTarget))
        {
            previousDistance = float.NaN;
            return 0f;
        }
        previousDistance = distanceToTarget;
        if (float.IsNaN(previous)) return 0f;
        return Mathf.Clamp(previous - distanceToTarget, -maxClosingDelta, maxClosingDelta);
    }

    // True the first time this episode the body stands in this patch.
    public bool EnterArea(float x, float z)
    {
        if (areaCellSize <= 0f || float.IsNaN(x) || float.IsNaN(z)
            || float.IsInfinity(x) || float.IsInfinity(z))
        {
            return false;
        }
        long cellX = (long)Mathf.Floor(x / areaCellSize);
        long cellZ = (long)Mathf.Floor(z / areaCellSize);
        return visited.Add((cellX << 32) ^ (cellZ & 0xffffffffL));
    }
}
