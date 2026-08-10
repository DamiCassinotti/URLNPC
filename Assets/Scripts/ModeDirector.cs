using UnityEngine;

// Writes the commanded mode during training, standing in for the LLM selector
// that takes the channel over at inference. Draws from RunRng on its own stream,
// on the fixed step the agent's decisions run on — so a seeded run replays the
// same mode timeline instead of quantising the switches to whatever frame rate
// the editor or a headless build happens to hit.
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

    void FixedUpdate()
    {
        Tick(Time.fixedTime);
    }

    // Internal seam: EditMode tests drive the schedule without play mode, the
    // same way GameManager feeds RoundClock its delta.
    internal void Tick(float now)
    {
        Write(now, restart: false);
    }

    // Episode resets: draw a fresh mode straight away rather than arming the
    // next tick. The agent collects its first observation on the Academy step,
    // which would otherwise read the mode the previous episode ended on.
    public void ResetState()
    {
        ResetState(Time.fixedTime);
    }

    internal void ResetState(float now)
    {
        schedule.Reset();
        Write(now, restart: true);
    }

    void Write(float now, bool restart)
    {
        // Awake does not run in EditMode.
        if (channel == null) channel = GetComponent<ModeChannel>();
        if (channel == null) return;

        if (useForcedMode)
        {
            Commit(forcedMode, restart);
            return;
        }

        RebuildPoolIfMaskChanged();
        schedule.MinDwellSeconds = minDwellSeconds;
        if (schedule.TryAdvance(now, pickIndex)) Commit(schedule.Current, restart);
    }

    // A reset forces the write through: the drawn mode can equal the one the
    // channel already holds, and SetMode would drop it as a no-op, leaving
    // TimeInMode counting the previous episode's dwell.
    void Commit(NpcMode mode, bool restart)
    {
        if (restart) channel.ResetState(mode);
        else channel.SetMode(mode);
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
