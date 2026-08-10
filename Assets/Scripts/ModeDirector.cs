using UnityEngine;

// Writes the commanded mode during training, standing in for the LLM selector
// that takes the channel over at inference. Draws from RunRng on its own stream
// so a seeded run replays the same mode timeline.
//
// The engine adapter half; the sampling rules are in ModeSchedule.
[RequireComponent(typeof(ModeChannel))]
public class ModeDirector : MonoBehaviour
{
    [Tooltip("Modes this run may command. The slice run trains Hunt/Retreat only.")]
    [SerializeField] internal NpcModeMask enabledModes = NpcModeMask.Hunt | NpcModeMask.Retreat;

    [Tooltip("Seconds a drawn mode is held before the next draw. Kept near the LLM selector's 3-5 s decision period so the policy trains on the switch rate it will see at inference.")]
    [SerializeField] internal float minDwellSeconds = 5f;

    [Header("Manual inspection")]
    [Tooltip("Hold one mode for the whole run instead of sampling.")]
    [SerializeField] internal bool useForcedMode;
    [SerializeField] internal NpcMode forcedMode = NpcMode.Hunt;

    readonly ModeSchedule schedule = new ModeSchedule();

    // Cached: a method group would allocate a delegate on every frame's draw.
    readonly System.Func<int, int> pickIndex = count => RunRng.Range(RunRng.Stream.Mode, 0, count);

    ModeChannel channel;
    NpcModeMask builtMask;
    bool poolBuilt;
    bool warnedEmptyMask;

    void Awake()
    {
        channel = GetComponent<ModeChannel>();
    }

    void Update()
    {
        Tick(Time.time);
    }

    // Internal seam: EditMode tests drive the schedule without play mode, the
    // same way GameManager feeds RoundClock its delta.
    internal void Tick(float now)
    {
        // Awake does not run in EditMode.
        if (channel == null) channel = GetComponent<ModeChannel>();
        if (channel == null) return;

        if (useForcedMode)
        {
            channel.SetMode(forcedMode);
            return;
        }

        RebuildPoolIfMaskChanged();
        schedule.MinDwellSeconds = minDwellSeconds;
        if (schedule.TryAdvance(now, pickIndex)) channel.SetMode(schedule.Current);
    }

    // Episode resets: the next tick draws a fresh mode with a full dwell.
    public void ResetState()
    {
        schedule.Reset();
    }

    void RebuildPoolIfMaskChanged()
    {
        if (poolBuilt && builtMask == enabledModes) return;
        builtMask = enabledModes;
        poolBuilt = true;
        schedule.Pool = enabledModes.Enabled();
        if (schedule.Pool.Length == 0 && !warnedEmptyMask)
        {
            warnedEmptyMask = true;
            Debug.LogWarning("[ModeDirector] enabledModes is empty — the channel keeps whatever mode it already holds.", this);
        }
    }
}
