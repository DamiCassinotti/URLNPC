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

    // Seconds since the mode last actually changed. Re-commanding the same mode
    // does not restart it.
    public float TimeInMode => Time.time - modeSetTime;

    // (previous, current). Only fires on a real change.
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
        NpcMode previous = CurrentMode;
        CurrentMode = mode;
        modeSetTime = Time.time;
        ModeChanged?.Invoke(previous, mode);
    }
}
