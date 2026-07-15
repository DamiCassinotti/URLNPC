using UnityEngine;

/// <summary>
/// The round countdown as plain state, engine-free so it is unit-testable
/// without play mode. GameManager owns one and feeds it Time.deltaTime; who
/// reacts to expiry (draw + freeze in human play, penalty + re-arm during
/// training) stays GameManager's business.
/// </summary>
public class RoundClock
{
    /// <summary>Round length in seconds. Zero or negative disables the clock.</summary>
    public float Duration;

    /// <summary>Seconds left. Clamped at 0 while ticking; only meaningful after <see cref="Reset"/>.</summary>
    public float Remaining { get; private set; }

    public bool Enabled => Duration > 0f;

    /// <summary>Rearm to the full duration.</summary>
    public void Reset()
    {
        Remaining = Duration;
    }

    /// <summary>
    /// Advance the clock. Returns true while the clock is expired — every
    /// tick until someone reacts (finishes the round or calls
    /// <see cref="Reset"/>), mirroring the caller's per-frame timeout check.
    /// A disabled clock never ticks and never expires.
    /// </summary>
    public bool Tick(float deltaSeconds)
    {
        if (!Enabled) return false;
        Remaining = Mathf.Max(0f, Remaining - deltaSeconds);
        return Remaining <= 0f;
    }
}
