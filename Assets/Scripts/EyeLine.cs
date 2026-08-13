using UnityEngine;

// The eye-line probe shared by the cover query (ArenaManager.NearestCoverPoint)
// and the in-cover reward flag (EnemyBehavior.IsHiddenFromTarget). They have to
// agree: a policy sent to a point the reward then calls exposed learns nothing
// about cover.
public static class EyeLine
{
    // Physics queries only run on the main thread, so one buffer serves every
    // caller. RaycastNonAlloc doesn't sort its hits, hence the walk below.
    static readonly RaycastHit[] hits = new RaycastHit[16];

    // Is the line from eye to otherEye interrupted by something other than the
    // bodies at its ends? A plain Physics.Raycast would accept those own
    // capsules as the blocker and call an exposed point covered.
    public static bool Blocked(Vector3 eye, Vector3 otherEye, LayerMask sightObstacleMask,
        Transform ignore, Transform alsoIgnore = null)
    {
        Vector3 toOther = otherEye - eye;
        float distance = toOther.magnitude - 0.1f;
        if (distance <= 0f) return false;

        int count = Physics.RaycastNonAlloc(eye, toOther.normalized, hits, distance,
            sightObstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].transform;
            if (ignore != null && hit.IsChildOf(ignore)) continue;
            if (alsoIgnore != null && hit.IsChildOf(alsoIgnore)) continue;
            return true;
        }
        return false;
    }
}
