using UnityEngine;

// The commanded-mode slot on a combatant: whatever is in here is what the
// policy is told to do this step. Exactly one writer at a time — the scripted
// ModeDirector during training, the LLM selector at inference — and any number
// of readers (the mode one-hot observation, the per-mode reward column,
// telemetry).
public class ModeChannel : MonoBehaviour
{
    [Tooltip("Mode the channel reports until a writer sets one.")]
    [SerializeField] internal NpcMode initialMode = NpcMode.Hunt;

    public NpcMode CurrentMode { get; private set; }

    // Seconds since the current mode interval started. Re-commanding the same
    // mode mid-episode does not restart it; an episode reset does.
    public float TimeInMode => Time.time - modeSetTime;

    // (previous, current), raised once per mode interval — so a listener that
    // segments the timeline from this event alone can never disagree with
    // TimeInMode. An episode reset that redraws the mode already held is a new
    // interval and reports previous == current.
    public event System.Action<NpcMode, NpcMode> ModeChanged;

    float modeSetTime;

    void Awake()
    {
        CurrentMode = initialMode;
        modeSetTime = Time.time;
    }

    public void SetMode(NpcMode mode)
    {
        if (mode == CurrentMode) return;
        Write(mode);
    }

    // Episode resets: force the mode and restart TimeInMode even when the drawn
    // mode matches the one the last episode ended on, which SetMode would treat
    // as a no-op and leave reporting the previous episode's dwell.
    public void ResetState(NpcMode mode)
    {
        Write(mode);
    }

    void Write(NpcMode mode)
    {
        NpcMode previous = CurrentMode;
        CurrentMode = mode;
        modeSetTime = Time.time;
        ModeChanged?.Invoke(previous, mode);
    }
}
