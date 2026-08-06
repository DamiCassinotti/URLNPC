using UnityEngine;

// The remember/freeze state machine behind PerceptionMemory, engine-free so it
// is testable without geometry or Time: the last-seen position tracks the
// target while visible and freezes the moment visibility ends. Time is injected
// as a plain "now" value.
public class PerceptionState
{
    public bool CurrentlyVisible { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public bool HasEverSeen { get; private set; }

    float lastSeenTime;

    // Infinity if never seen.
    public float TimeSinceSeen(float now) => HasEverSeen ? now - lastSeenTime : Mathf.Infinity;

    public void Observe(bool targetVisible, Vector3 targetPosition, float now)
    {
        CurrentlyVisible = targetVisible;
        if (targetVisible)
        {
            LastSeenPosition = targetPosition;
            lastSeenTime = now;
            HasEverSeen = true;
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
