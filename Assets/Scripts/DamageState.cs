using UnityEngine;

// The decay-timer memory behind DamageMemory, engine-free so it is testable
// without Time or geometry: a hit is remembered for memorySeconds along with
// the quarter it came from, then lapses. Time is injected as a plain "now".
public class DamageState
{
    public float memorySeconds = 2f;

    bool hasHit;
    float lastHitTime;
    HitDirection direction;

    public bool RecentlyDamaged(float now) => hasHit && now - lastHitTime < memorySeconds;

    // None once the memory has lapsed, so the flag and the direction can never
    // disagree.
    public HitDirection LastHitDirection(float now) => RecentlyDamaged(now) ? direction : HitDirection.None;

    // Infinity if never hit.
    public float TimeSinceDamaged(float now) => hasHit ? now - lastHitTime : Mathf.Infinity;

    public void Record(HitDirection from, float now)
    {
        hasHit = true;
        lastHitTime = now;
        direction = from;
    }

    // Episode resets: last episode's hits must not leak into the next.
    public void Forget()
    {
        hasHit = false;
        lastHitTime = 0f;
        direction = HitDirection.None;
    }

    // Horizontal only: a shooter directly overhead has no quarter, and neither
    // does one standing on top of the victim. Boundaries go Front on ±45° and
    // Right/Left on ±135°, so the four buckets stay disjoint.
    public static HitDirection Bucket(Vector3 toSource, Vector3 forward)
    {
        toSource.y = 0f;
        forward.y = 0f;
        if (toSource.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f) return HitDirection.None;

        float angle = Vector3.SignedAngle(forward, toSource, Vector3.up);
        if (angle >= -45f && angle <= 45f) return HitDirection.Front;
        if (angle > 135f || angle < -135f) return HitDirection.Back;
        return angle > 0f ? HitDirection.Right : HitDirection.Left;
    }
}
