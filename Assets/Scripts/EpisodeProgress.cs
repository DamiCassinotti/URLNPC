using System.Collections.Generic;
using UnityEngine;

// Per-episode movement bookkeeping the mode reward columns consume: how much
// ground was closed on the target since the last step (Hunt/Retreat), whether
// the body just entered a patch of the arena it hasn't been in yet (Patrol) and
// how far it moved. All of it is pure state over positions, so EnemyAgent only
// has to feed it the distance and its own position once per step and reset it
// on episode begin.
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
    long currentCell;
    bool hasCell;
    Vector2 previousPosition;
    bool hasPosition;

    public int VisitedAreas => visited.Count;

    // True while the body is still standing in the patch it walked into new.
    // The reward pays once on entry; Patrol's compliance rule scores the whole
    // crossing, so it needs to know the ground under the current step is fresh
    // and not only the step that reached it.
    public bool InNewArea { get; private set; }

    public void Reset()
    {
        visited.Clear();
        previousDistance = float.NaN;
        hasCell = false;
        InNewArea = false;
        hasPosition = false;
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

    // True the first time this episode the body stands in this patch. Also
    // updates InNewArea, which holds that verdict until the body leaves.
    public bool EnterArea(float x, float z)
    {
        if (!GridCell.TryPack(x, z, areaCellSize, out long cell)) return false;
        bool isNew = visited.Add(cell);
        if (!hasCell || cell != currentCell)
        {
            currentCell = cell;
            hasCell = true;
            InNewArea = isNew;
        }
        return isNew;
    }

    // Metres the body moved since the previous call. Zero on the first call of
    // an episode (no baseline) and capped like Closing, so a respawn across the
    // arena doesn't read as ground covered.
    public float Travelled(float x, float z)
    {
        var position = new Vector2(x, z);
        if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z))
        {
            hasPosition = false;
            return 0f;
        }
        float travelled = hasPosition ? Vector2.Distance(previousPosition, position) : 0f;
        previousPosition = position;
        hasPosition = true;
        return Mathf.Min(travelled, maxClosingDelta);
    }
}
