using UnityEngine;

// Packing a square floor patch into one long, shared by the two per-episode
// "ground already covered" sets — EpisodeProgress's new-area reward and
// SearchPlanner's sweep — so the two can't drift apart.
public static class GridCell
{
    // False for a cell size that isn't positive, or a coordinate that isn't a
    // real number: nothing to count rather than a hash of garbage.
    public static bool TryPack(float x, float z, float cellSize, out long cell)
    {
        cell = 0L;
        if (cellSize <= 0f || float.IsNaN(x) || float.IsNaN(z)
            || float.IsInfinity(x) || float.IsInfinity(z))
        {
            return false;
        }
        long cellX = (long)Mathf.Floor(x / cellSize);
        long cellZ = (long)Mathf.Floor(z / cellSize);
        cell = (cellX << 32) ^ (cellZ & 0xffffffffL);
        return true;
    }
}
