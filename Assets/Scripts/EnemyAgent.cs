using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

[RequireComponent(typeof(EnemyBehavior))]
[RequireComponent(typeof(Health))]
public class EnemyAgent : Agent
{
    [Header("Rewards")]
    // Small enough that a full ~1200-step round of stalling pays less than a
    // kill: at 0.001 it summed to ~+1.2 and the policy converged to running the
    // clock out (#79).
    [SerializeField] float aliveRewardPerStep = 0.0002f;
    [SerializeField] float hitTargetReward = 0.5f;
    [SerializeField] float gotHitPenalty = 0.5f;
    [SerializeField] float killTargetReward = 1.0f;
    [SerializeField] float diedPenalty = 1.0f;
    [SerializeField] float wastedShotPenalty = 0.05f;
    [Tooltip("Negative terminal reward when the round clock runs out (draw). Discourages stalling without forcing a winner.")]
    [SerializeField] float timeoutPenalty = 0.6f;

    [Header("Positioning shaping")]
    [Tooltip("Fallback per-step penalty for being closer to the player than tooCloseDistance, used only where tooClosePenaltyByMode has no entry.")]
    [SerializeField] float tooClosePenaltyPerStep = 0.001f;
    [SerializeField] float tooCloseDistance = 6f;

    // One column per NpcMode, in NpcModes.All order: Hunt, HoldCover, Retreat,
    // Patrol. Rows shorter than that fall back to the global reward above (or to
    // 0 for the rows that have no global counterpart), so a row edited down in
    // the Inspector can't throw mid-episode.
    [Header("Per-mode reward columns (Hunt, HoldCover, Retreat, Patrol)")]
    [Tooltip("Reward for hitting the target, per commanded mode. Falls back to hitTargetReward.")]
    [SerializeField] float[] hitTargetRewardByMode = { 0.5f, 0.5f, 0.1f, 0.1f };
    [Tooltip("Penalty (positive magnitude) for being hit, per commanded mode. Falls back to gotHitPenalty.")]
    [SerializeField] float[] gotHitPenaltyByMode = { 0.3f, 0.6f, 0.6f, 0.5f };
    [Tooltip("Reward per metre closed on the target since the last step. Negative pays for opening distance instead.")]
    [SerializeField] float[] closingRewardPerMeterByMode = { 0.01f, 0f, -0.01f, 0f };
    [Tooltip("Per-step reward while the target's eye-line to the enemy is broken.")]
    [SerializeField] float[] coverRewardPerStepByMode = { 0f, 0.005f, 0.002f, 0f };
    [Tooltip("One-off reward the first time each patch of the arena is entered in an episode.")]
    [SerializeField] float[] newAreaRewardByMode = { 0f, 0f, 0f, 0.002f };
    [Tooltip("Penalty (positive magnitude) per step inside tooCloseDistance. Zero for Hunt, which has to be free to close. Falls back to tooClosePenaltyPerStep.")]
    [SerializeField] float[] tooClosePenaltyByMode = { 0f, 0.004f, 0.008f, 0.002f };
    [Tooltip("Side in metres of the square patches the new-area reward counts.")]
    [SerializeField] float newAreaCellSize = 6f;

    [Header("Episode reset")]
    [Tooltip("During training (communicator on), teleport the player to a random NavMesh point on episode begin so spawn positions don't cluster wherever the player last died. Human play gets its random spawn from ArenaManager.Start on each round's scene reload instead — repositioning here would also fire on mid-round MaxStep resets and yank a live player across the arena.")]
    [SerializeField] bool repositionPlayerOnEpisodeBegin = true;

    EnemyBehavior behavior;
    Health selfHealth;
    Health targetHealth;
    ModeChannel modeChannel;
    ModeDirector director;
    GameManager gameManager;
    RewardComputer rewards;
    EpisodeProgress progress;
    ModeComplianceTracker compliance;

    // Built once: the stat name is per mode and the flush runs every episode.
    static readonly string[] complianceStatNames = BuildComplianceStatNames();

    readonly float[] observations = new float[NpcBrainSpec.ObservationSize];
    NpcObservationInput inputs;

    // Test seam: the vector CollectObservations last wrote, so PlayMode can
    // check the real memories reach the frozen layout.
    internal float[] LastObservations => observations;

    bool episodeEnding;

    // Awake, not Initialize: Agent.OnEnable builds the actuators off the action
    // spec before Initialize runs, so a branch count fixed there arrives a step
    // too late.
    protected override void Awake()
    {
        base.Awake();
        EnforceBrainSpec();
        SubscribeToModeChannel();
    }

    // The mode timeline the behavior analysis segments on, logged from here
    // rather than a scan in TelemetryLogger: the channel is added at runtime
    // (EnemyBehavior.Awake, and CombatantRig for the agent-driven player), so a
    // scene-load-time scan can miss it. Awake and not Initialize because
    // component order decides whether EnemyBehavior.Awake has run by then —
    // finding-or-adding the channel the same way it does leaves exactly one on
    // the body whichever of the two goes first, and it exists before the first
    // FixedUpdate a writer could command a mode on.
    void SubscribeToModeChannel()
    {
        modeChannel = GetComponent<ModeChannel>();
        if (modeChannel == null) modeChannel = gameObject.AddComponent<ModeChannel>();
        modeChannel.ModeChanged += HandleModeChanged;
    }

    // The frozen interface has to hold for the instance that actually plays, and
    // the binary FPS scene carries prefab-instance overrides pinning the old
    // 3-float / one-branch shape — a text edit to Enemy.prefab never reaches
    // them. Same problem, and the same fix, as the stale MaxStep below.
    void EnforceBrainSpec()
    {
        var parameters = GetComponent<BehaviorParameters>();
        if (parameters == null) return;
        BrainParameters brain = parameters.BrainParameters;

        if (brain.VectorObservationSize != NpcBrainSpec.ObservationSize
            || brain.NumStackedVectorObservations != 1)
        {
            Debug.LogWarning(
                $"[EnemyAgent] Overriding serialized observation shape " +
                $"{brain.VectorObservationSize}x{brain.NumStackedVectorObservations} -> " +
                $"{NpcBrainSpec.ObservationSize}x1 (#43).", this);
            brain.VectorObservationSize = NpcBrainSpec.ObservationSize;
            brain.NumStackedVectorObservations = 1;
        }

        ActionSpec expected = NpcBrainSpec.Actions;
        int[] branches = brain.ActionSpec.BranchSizes;
        bool matches = brain.ActionSpec.NumContinuousActions == 0
            && branches != null
            && branches.Length == expected.BranchSizes.Length;
        for (int i = 0; matches && i < branches.Length; i++)
        {
            matches = branches[i] == expected.BranchSizes[i];
        }
        if (!matches)
        {
            Debug.LogWarning(
                $"[EnemyAgent] Overriding serialized action branches " +
                $"[{string.Join(",", branches ?? System.Array.Empty<int>())}] -> " +
                $"[{string.Join(",", expected.BranchSizes)}] (#43).", this);
            brain.ActionSpec = expected;
        }
    }

    public override void Initialize()
    {
        // The round clock is the single owner of time-based episode
        // termination. A nonzero MaxStep silently resets the episode before the
        // clock fires — the binary FPS scene carries a stale 5000 (~100 s,
        // shorter than the round), which made timeouts restart the round in
        // place and never count a draw.
        if (MaxStep != 0)
        {
            Debug.LogWarning($"[EnemyAgent] Overriding serialized MaxStep {MaxStep} -> 0; the round clock owns episode timeout.");
            MaxStep = 0;
        }

        behavior = GetComponent<EnemyBehavior>();
        selfHealth = GetComponent<Health>();
        // The scripted mode writer EnemyBehavior.Awake attaches to every AI
        // body; OnEpisodeBegin redraws its mode so the new episode doesn't
        // inherit the last one's dwell. Resolved here rather than off the
        // behavior so it holds even in fixtures that disable EnemyBehavior.
        director = GetComponent<ModeDirector>();

        // Snapshot taken once per play session, so these tunables only take
        // effect between runs. Built before the subscriptions below: the
        // handlers read the mode columns out of it.
        rewards = new RewardComputer
        {
            aliveRewardPerStep = aliveRewardPerStep,
            wastedShotPenalty = wastedShotPenalty,
            tooCloseDistance = tooCloseDistance,
        };
        foreach (NpcMode mode in NpcModes.All)
        {
            rewards.modes[mode] = new ModeRewardColumn
            {
                hitTarget = Column(hitTargetRewardByMode, mode, hitTargetReward),
                gotHit = Column(gotHitPenaltyByMode, mode, gotHitPenalty),
                closingPerMeter = Column(closingRewardPerMeterByMode, mode, 0f),
                coverPerStep = Column(coverRewardPerStepByMode, mode, 0f),
                newArea = Column(newAreaRewardByMode, mode, 0f),
                tooClosePerStep = Column(tooClosePenaltyByMode, mode, tooClosePenaltyPerStep),
            };
        }
        progress = new EpisodeProgress { areaCellSize = newAreaCellSize };
        compliance = new ModeComplianceTracker();

        selfHealth.OnDamaged += HandleSelfDamaged;
        selfHealth.OnDied += HandleSelfDied;
    }

    static string[] BuildComplianceStatNames()
    {
        var names = new string[NpcModes.All.Length];
        foreach (NpcMode mode in NpcModes.All) names[(int)mode] = $"Compliance/{mode}";
        return names;
    }

    static float Column(float[] row, NpcMode mode, float fallback)
    {
        return row != null && (int)mode < row.Length ? row[(int)mode] : fallback;
    }

    // What the mode columns are read against. The channel is auto-added by
    // EnemyBehavior.Awake, so this only falls back before Initialize has run.
    NpcMode CommandedMode =>
        behavior != null && behavior.Mode != null ? behavior.Mode.CurrentMode : NpcMode.Hunt;

    void Update()
    {
        // OnActionReceived also runs on the steps between decisions, where
        // CollectObservations doesn't; keep the snapshot its reward reads fresh.
        ReadInputs();
        EnsureTargetSubscription();
    }

    // Sensory contract (issue #9): target info reaches the policy only through
    // PerceptionMemory, never off the target transform.
    void ReadInputs()
    {
        PerceptionMemory perception = behavior.Perception;
        DamageMemory damage = behavior.Damage;
        ModeChannel mode = behavior.Mode;
        float maxHp = selfHealth.maxHealth <= 0f ? 1f : selfHealth.maxHealth;

        inputs = new NpcObservationInput
        {
            canAttack = behavior.ReadCanAttack(),
            cooldownRemaining01 = behavior.ReadCooldownRemaining01(),

            targetVisible = perception != null && perception.CurrentlyVisible,
            hasEverSeen = perception != null && perception.HasEverSeen,
            lastSeenPosition = perception != null ? perception.LastSeenPosition : Vector3.zero,
            timeSinceSeen = perception != null ? perception.TimeSinceSeen : Mathf.Infinity,

            selfPosition = transform.position,
            selfForward = transform.forward,
            sightRange = behavior.SightRange,

            normalizedHealth = selfHealth.health / maxHp,
            normalizedSpeed = behavior.ReadNormalizedSpeed(),

            recentlyDamaged = damage != null && damage.RecentlyDamaged,
            hitDirection = damage != null ? damage.LastHitDirection : HitDirection.None,
            mode = mode != null ? mode.CurrentMode : NpcMode.Hunt,
        };
    }

    void EnsureTargetSubscription()
    {
        if (behavior.target == null) return;
        // Walk both directions so this works regardless of whether Health is
        // attached to the tagged root, a parent, or a child mesh GameObject.
        Health th = behavior.target.GetComponent<Health>()
                  ?? behavior.target.GetComponentInParent<Health>()
                  ?? behavior.target.GetComponentInChildren<Health>();
        if (th == targetHealth) return;
        if (targetHealth != null)
        {
            targetHealth.OnDamaged -= HandleTargetDamaged;
            targetHealth.OnDied -= HandleTargetDied;
        }
        targetHealth = th;
        if (targetHealth != null)
        {
            targetHealth.OnDamaged += HandleTargetDamaged;
            targetHealth.OnDied += HandleTargetDied;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Decisions run on the fixed step, where PerceptionMemory's per-frame
        // Update is stale — badly so at the trainer's time scale. Refresh for
        // the same reason Move/Attack do, so the observation and the action it
        // produces are taken from one view of the world.
        if (behavior.Perception != null) behavior.Perception.Refresh();
        ReadInputs();
        NpcObservations.Fill(observations, inputs);
        sensor.AddObservation(observations);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        // Advance on the memory, not just on sight (issue #72): losing sight
        // has to mean walking to where they went, and Advance falls through to
        // a search of its own once it gets there and finds nobody.
        discrete[0] = (int)(inputs.hasEverSeen ? MovementAction.Advance : MovementAction.Wander);
        discrete[1] = inputs.targetVisible && inputs.canAttack ? NpcBrainSpec.Fire : NpcBrainSpec.DontFire;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeEnding) return;

        // True-state reads are fine here: reward computation is environment
        // code, not a policy input (sensory contract, issue #9).
        NpcMode mode = CommandedMode;
        float distanceToTarget = behavior.DistanceToTarget();
        // Taken before the move so consecutive steps measure the same thing,
        // and unconditionally so the baseline never skips a step.
        float closingDelta = progress.Closing(distanceToTarget);
        bool enteredNewArea = progress.EnterArea(transform.position.x, transform.position.z);
        // A raycast, so it is only probed for the modes that read it: the
        // columns that pay for cover, and HoldCover's compliance rule.
        bool needsCover = rewards.RewardsCover(mode) || ModeComplianceTracker.ReadsCover(mode);
        bool hidden = needsCover && behavior.IsHiddenFromTarget();

        behavior.Move((MovementAction)actions.DiscreteActions[0]);
        // Fire after moving: the primitives set the facing, and Attack's own
        // LookAt at the perceived position has to be the last word on the aim.
        bool fired = actions.DiscreteActions[1] == NpcBrainSpec.Fire;
        if (fired) behavior.Attack();

        AddReward(rewards.StepReward(new StepRewardInput
        {
            mode = mode,
            fired = fired,
            didShoot = behavior.DidShoot,
            targetInSight = inputs.targetVisible,
            distanceToTarget = distanceToTarget,
            closingDelta = closingDelta,
            hiddenFromTarget = hidden,
            enteredNewArea = enteredNewArea,
        }));

        compliance.Record(new ComplianceSample
        {
            mode = mode,
            closingDelta = closingDelta,
            shotFired = fired && behavior.DidShoot,
            inCover = hidden,
            enteredNewArea = enteredNewArea,
        });
    }

    public override void OnEpisodeBegin()
    {
        episodeEnding = false;
        // Both halves are per-episode: the respawn below is a teleport, not
        // ground closed, and the arena is unexplored again.
        if (progress != null) progress.Reset();
        // Normally already emptied by the flush on the way out; this catches an
        // episode the trainer ends from its side, whose tally would otherwise
        // be counted into the next one.
        if (compliance != null) compliance.Reset();
        if (selfHealth != null) selfHealth.ResetHealth();
        if (targetHealth != null) targetHealth.ResetHealth();
        // Reposition the player BEFORE the enemy respawns so the enemy's
        // min-spawn-separation check (EnemyBehavior.InitAtRandomPosition)
        // measures against the player's fresh position, not last episode's.
        if (ShouldRepositionPlayer())
        {
            ArenaManager.Current.RepositionPlayerAtRandomPoint();
        }
        if (behavior != null) behavior.ResetState();
        // Redraw the commanded mode for the fresh episode: without this the new
        // episode keeps the mode (and the running dwell) the last one ended on.
        // ResetState stands the director down outside training on its own.
        if (director != null) director.ResetState();
        // Training episodes reset without a scene reload, so the clock has to
        // be rearmed here to give each one a full budget.
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null) gameManager.ResetRoundClock();
        // Makes every episode attributable to its run seed in TensorBoard.
        if (Academy.IsInitialized)
        {
            Academy.Instance.StatsRecorder.Add("Run/Seed", RunRng.Seed);
        }
    }

    // Called by GameManager when the round clock runs out. A timeout is a draw:
    // both sides take a small penalty so stalling doesn't pay off, and the
    // episode ends without a winner.
    public void OnRoundTimeout()
    {
        if (episodeEnding) return;
        EndEpisodeWith(-timeoutPenalty);
    }

    // Every terminal path goes through here so the episode's compliance tally
    // is always reported, and reported before the round-end machinery moves
    // the telemetry log on to the next episode.
    void EndEpisodeWith(float terminalReward)
    {
        episodeEnding = true;
        AddReward(terminalReward);
        FlushCompliance();
        EndEpisode();
    }

    // Per-episode mode compliance (#45): to TensorBoard next to reward and
    // entropy, and to the JSONL for the offline behavior analysis.
    void FlushCompliance()
    {
        if (compliance == null || compliance.TotalSteps == 0) return;

        if (Academy.IsInitialized)
        {
            StatsRecorder stats = Academy.Instance.StatsRecorder;
            foreach (NpcMode mode in NpcModes.All)
            {
                // A mode this episode never ran has no rate to average in.
                if (compliance.Steps(mode) > 0) stats.Add(complianceStatNames[(int)mode], compliance.Rate(mode));
            }
        }
        if (TelemetryLogger.Instance != null)
        {
            TelemetryLogger.Instance.LogEvent("mode_compliance",
                JsonLine.Field("entity", tag),
                compliance.ComplianceJson());
        }
        compliance.Reset();
    }

    void HandleModeChanged(NpcMode previous, NpcMode current)
    {
        if (TelemetryLogger.Instance == null) return;
        TelemetryLogger.Instance.LogEvent("mode_change",
            JsonLine.Field("entity", tag),
            JsonLine.Field("from", previous.ToString()),
            JsonLine.Field("to", current.ToString()));
    }

    // Only during automated training: in human play the round restarts via a
    // scene reload and ArenaManager.Start places the player, so teleporting
    // them here would just yank a live player around mid-round.
    bool ShouldRepositionPlayer()
    {
        return repositionPlayerOnEpisodeBegin
            && ArenaManager.Current != null
            && Academy.IsInitialized
            && Academy.Instance.IsCommunicatorOn;
    }

    void HandleSelfDamaged(DamageInfo info)
    {
        AddReward(-rewards.modes[CommandedMode].gotHit);
    }

    void HandleTargetDamaged(DamageInfo info)
    {
        AddReward(rewards.modes[CommandedMode].hitTarget);
    }

    void HandleSelfDied()
    {
        if (episodeEnding) return;
        EndEpisodeWith(-diedPenalty);
    }

    void HandleTargetDied()
    {
        if (episodeEnding) return;
        EndEpisodeWith(killTargetReward);
    }

    void OnDestroy()
    {
        if (modeChannel != null) modeChannel.ModeChanged -= HandleModeChanged;
        if (selfHealth != null)
        {
            selfHealth.OnDamaged -= HandleSelfDamaged;
            selfHealth.OnDied -= HandleSelfDied;
        }
        if (targetHealth != null)
        {
            targetHealth.OnDamaged -= HandleTargetDamaged;
            targetHealth.OnDied -= HandleTargetDied;
        }
    }
}
