// When the mode selector is asked for a decision, and when its failures hand
// the channel to the fallback — engine-free so the cadence is testable with an
// injected "now", the way ModeSchedule is for ModeDirector.
//
// Two paths issue a call: the periodic one every DecisionPeriodSeconds, and
// the event one — target visibility flipped, took a hit, own HP crossed the
// low-water mark — gated by MinEventDwellSeconds so a noisy fight can't
// thrash the mode. An event landing inside that dwell is dropped, not
// deferred: the next periodic call reads the same state soon enough.
//
// An event also can't preempt a call already in flight (#134): a real selector
// needs wall-clock seconds to answer, and re-issuing on every event would
// cancel it before it lands, scoring the run on the fallback. Only the periodic
// deadline cancels an in-flight call — that cancellation is the selector's
// timeout.
public class SelectorSchedule
{
    public float DecisionPeriodSeconds = 5f;
    public float MinEventDwellSeconds = 2f;
    public float LowHealthFraction = 0.35f;
    // Consecutive failures before the fallback selector takes over. Zero or
    // less never fails over.
    public int FailuresBeforeFallback = 3;

    public int ConsecutiveFailures { get; private set; }

    // Latched until Reset: successes after the switch come from the fallback,
    // so they say nothing about the primary having recovered.
    public bool FallbackActive { get; private set; }

    bool issuedAny;
    float lastIssueTime;
    bool seeded;
    bool wasVisible;
    bool wasDamaged;
    bool wasLow;

    // Call exactly once per tick: it advances the edge detection as well as
    // answering. The first call after Reset always issues — a fresh episode
    // should not wait a full period for its first decision. The first call
    // also seeds the edge state without triggering, so starting an episode in
    // sight or already hurt is a starting condition, not an event.
    public bool ShouldIssue(float now, bool targetVisible, bool recentlyDamaged, float healthFraction, bool callInFlight = false)
    {
        bool low = healthFraction <= LowHealthFraction;
        bool edge = seeded && (targetVisible != wasVisible
                            // Rising only: damage lapsing from memory is not news.
                            || (recentlyDamaged && !wasDamaged)
                            || low != wasLow);
        wasVisible = targetVisible;
        wasDamaged = recentlyDamaged;
        wasLow = low;
        seeded = true;

        if (!issuedAny) return true;
        float sinceIssue = now - lastIssueTime;
        // The periodic deadline fires regardless — cancelling any in-flight call
        // is the timeout.
        if (sinceIssue >= DecisionPeriodSeconds) return true;
        // An event can't cut in front of a call still being answered.
        if (callInFlight) return false;
        return edge && sinceIssue >= MinEventDwellSeconds;
    }

    public void MarkIssued(float now)
    {
        issuedAny = true;
        lastIssueTime = now;
    }

    public void RecordSuccess()
    {
        ConsecutiveFailures = 0;
    }

    public void RecordFailure()
    {
        ConsecutiveFailures++;
        if (FailuresBeforeFallback > 0 && ConsecutiveFailures >= FailuresBeforeFallback)
        {
            FallbackActive = true;
        }
    }

    public void Reset()
    {
        issuedAny = false;
        seeded = false;
        lastIssueTime = 0f;
        ConsecutiveFailures = 0;
        FallbackActive = false;
    }
}
