using UnityEngine;

// The round countdown as plain state, engine-free so it is testable without
// play mode. GameManager owns one and feeds it Time.deltaTime; how to react to
// expiry (draw + freeze in human play, penalty + re-arm during training) stays
// GameManager's business.
public class RoundClock
{
    // Zero or negative disables the clock.
    public float Duration;

    // Only meaningful after Reset.
    public float Remaining { get; private set; }

    public bool Enabled => Duration > 0f;

    public void Reset()
    {
        Remaining = Duration;
    }

    // Keeps returning true every tick once expired, until someone finishes the
    // round or re-arms — that mirrors the caller's per-frame timeout check. A
    // disabled clock never ticks and never expires.
    public bool Tick(float deltaSeconds)
    {
        if (!Enabled) return false;
        Remaining = Mathf.Max(0f, Remaining - deltaSeconds);
        return Remaining <= 0f;
    }
}
