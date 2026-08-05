using UnityEngine;

/// <summary>
/// The remember/freeze state machine behind <see cref="PerceptionMemory"/>,
/// engine-free so it is unit-testable without geometry or Time: while the
/// target is visible the last-seen position tracks it; the moment visibility
/// ends the position freezes; <see cref="Forget"/> wipes everything for
/// episode resets. Time is injected as a plain "now" value.
/// </summary>
public class PerceptionState
{
    public bool CurrentlyVisible { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public bool HasEverSeen { get; private set; }

    float lastSeenTime;

    /// <summary>Seconds between <paramref name="now"/> and the last sighting. Infinity if never seen.</summary>
    public float TimeSinceSeen(float now) => HasEverSeen ? now - lastSeenTime : Mathf.Infinity;

    /// <summary>Record one sight-check result.</summary>
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

    /// <summary>Wipe the memory (episode resets — last episode's sighting must not leak).</summary>
    public void Forget()
    {
        CurrentlyVisible = false;
        HasEverSeen = false;
        LastSeenPosition = Vector3.zero;
    }
}
