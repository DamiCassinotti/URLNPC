using UnityEngine;

// The remember/freeze state machine behind PerceptionMemory, engine-free so it
// is testable without geometry or Time: the last-seen position tracks the
// target while visible, freezes the moment visibility ends, and lapses back to
// never-seen once it is older than memorySeconds. Time is injected as a plain
// "now" value.
public class PerceptionState
{
    // How long a sighting is worth anything (issue #72). Without a horizon the
    // NPC kept navigating and firing at a spot the player left long ago;
    // lapsing hands the bearing-relative primitives back to Wander, i.e. it
    // searches. Matched to the staleness observation's scale, so that input
    // spans its whole range and then reads as the never-seen fixed point.
    public float memorySeconds = NpcObservations.SeenHorizonSeconds;

    public bool CurrentlyVisible { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public bool HasEverSeen { get; private set; }

    float lastSeenTime;

    // Infinity if never seen.
    public float TimeSinceSeen(float now) => HasEverSeen ? now - lastSeenTime : Mathf.Infinity;

    // Is the sighting no older than the given window? False when never seen.
    public bool SeenWithin(float seconds, float now) => TimeSinceSeen(now) <= seconds;

    public void Observe(bool targetVisible, Vector3 targetPosition, float now)
    {
        CurrentlyVisible = targetVisible;
        if (targetVisible)
        {
            LastSeenPosition = targetPosition;
            lastSeenTime = now;
            HasEverSeen = true;
        }
        else if (HasEverSeen && now - lastSeenTime > memorySeconds)
        {
            // Back to the clean never-seen fixed point rather than a stale
            // position the policy would still read a bearing off.
            Forget();
        }
    }

    // Episode resets: last episode's sighting must not leak into the next.
    public void Forget()
    {
        CurrentlyVisible = false;
        HasEverSeen = false;
        LastSeenPosition = Vector3.zero;
    }
}
