using UnityEngine;
using Unity.MLAgents;

// Writes the commanded mode during training, standing in for the LLM selector
// that takes the channel over at inference. Draws from RunRng on its own stream,
// on the fixed step the agent's decisions run on — so a seeded run replays the
// same mode timeline instead of quantising the switches to whatever frame rate
// the editor or a headless build happens to hit.
//
// The engine adapter half; the sampling rules are in ModeSchedule.
// Ahead of Agent (-50), DecisionRequester (-10) and the Academy's own stepper
// (default order): same-order FixedUpdates run in an undefined order, so
// without this the mode could land after the step it is meant to condition.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(ModeChannel))]
public class ModeDirector : MonoBehaviour
{
    [Tooltip("Modes this run may command outside training. Ignored while the trainer is attached: training always commands all four, so the prefab, the scene's instance override and CombatantRig's composed director can't drift apart.")]
    [SerializeField] internal NpcModeMask enabledModes = NpcModes.AllMask;

    [Tooltip("Seconds a drawn mode is held before the next draw. Kept near the LLM selector's 3-5 s decision period so the policy trains on the switch rate it will see at inference.")]
    [SerializeField] internal float minDwellSeconds = 5f;

    [Tooltip("Only write the channel while the trainer is attached, so the director can't fight the LLM selector for it at inference. Untick to drive an inference run from the scripted schedule instead — the random-mode evaluation baseline, or manual inspection with a forced mode.")]
    [SerializeField] internal bool trainingOnly = true;

    [Header("Manual inspection")]
    [Tooltip("Hold one mode for the whole run instead of sampling. Needs trainingOnly unticked outside training.")]
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
        if (!IsWriter) return;
        Tick(Time.fixedTime);
    }

    // The channel takes one writer at a time: this director during training,
    // the LLM selector at inference. Nothing arbitrates between them, so the
    // scripted side stands down unless it was told to run either way.
    internal bool IsWriter => !trainingOnly || IsTraining;

    static bool IsTraining => Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;

    // The pool actually drawn from: code-owned during training, the serialized
    // field outside it. See NpcModes.ResolveEnabled.
    internal NpcModeMask ActiveModes => NpcModes.ResolveEnabled(enabledModes, IsTraining);

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
        if (!IsWriter) return;
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
        // An empty pool has nothing to draw, but a reset still owes the channel
        // an interval boundary — otherwise the new episode inherits the old
        // one's dwell with no ModeChanged to mark it.
        else if (restart) channel.ResetState(channel.CurrentMode);
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
        NpcModeMask mask = ActiveModes;
        if (poolBuilt && builtMask == mask) return;
        builtMask = mask;
        poolBuilt = true;
        schedule.Pool = mask.Enabled();
        if (schedule.Pool.Length == 0 && !warnedEmptyMask)
        {
            warnedEmptyMask = true;
            Debug.LogWarning("[ModeDirector] enabledModes is empty — the channel keeps whatever mode it already holds.", this);
        }
    }
}
