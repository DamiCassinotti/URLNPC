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
    [SerializeField] float aliveRewardPerStep = 0.001f;
    [SerializeField] float hitTargetReward = 0.5f;
    [SerializeField] float gotHitPenalty = 0.5f;
    [SerializeField] float killTargetReward = 1.0f;
    [SerializeField] float diedPenalty = 1.0f;
    [SerializeField] float wastedShotPenalty = 0.05f;
    [Tooltip("Small negative terminal reward when the round clock runs out (draw). Discourages stalling without forcing a winner.")]
    [SerializeField] float timeoutPenalty = 0.2f;

    [Header("Positioning shaping")]
    [Tooltip("Per-step penalty while the enemy is closer to the player than tooCloseDistance. Discourages melee-rush.")]
    [SerializeField] float tooClosePenaltyPerStep = 0.005f;
    [SerializeField] float tooCloseDistance = 6f;

    [Header("Episode reset")]
    [Tooltip("During training (communicator on), teleport the player to a random NavMesh point on episode begin so spawn positions don't cluster wherever the player last died. Human play gets its random spawn from ArenaManager.Start on each round's scene reload instead — repositioning here would also fire on mid-round MaxStep resets and yank a live player across the arena.")]
    [SerializeField] bool repositionPlayerOnEpisodeBegin = true;

    EnemyBehavior behavior;
    Health selfHealth;
    Health targetHealth;
    GameManager gameManager;
    RewardComputer stepRewards;

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
        selfHealth.OnDamaged += HandleSelfDamaged;
        selfHealth.OnDied += HandleSelfDied;

        // Snapshot taken once per play session, so these tunables only take
        // effect between runs. (The event rewards below still read live.)
        stepRewards = new RewardComputer
        {
            aliveRewardPerStep = aliveRewardPerStep,
            wastedShotPenalty = wastedShotPenalty,
            tooClosePenaltyPerStep = tooClosePenaltyPerStep,
            tooCloseDistance = tooCloseDistance,
        };
    }

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
        discrete[0] = (int)(inputs.targetVisible ? MovementAction.Advance : MovementAction.Wander);
        discrete[1] = inputs.targetVisible && inputs.canAttack ? NpcBrainSpec.Fire : NpcBrainSpec.DontFire;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeEnding) return;

        // A true-state read is fine here: reward computation is environment
        // code, not a policy input (sensory contract, issue #9).
        float distanceToTarget = behavior.DistanceToTarget();

        behavior.Move((MovementAction)actions.DiscreteActions[0]);
        // Fire after moving: the primitives set the facing, and Attack's own
        // LookAt at the perceived position has to be the last word on the aim.
        bool fired = actions.DiscreteActions[1] == NpcBrainSpec.Fire;
        if (fired) behavior.Attack();

        AddReward(stepRewards.StepReward(fired, behavior.DidShoot, inputs.targetVisible, distanceToTarget));
    }

    public override void OnEpisodeBegin()
    {
        episodeEnding = false;
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
        episodeEnding = true;
        AddReward(-timeoutPenalty);
        EndEpisode();
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
        AddReward(-gotHitPenalty);
    }

    void HandleTargetDamaged(DamageInfo info)
    {
        AddReward(hitTargetReward);
    }

    void HandleSelfDied()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        AddReward(-diedPenalty);
        EndEpisode();
    }

    void HandleTargetDied()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        AddReward(killTargetReward);
        EndEpisode();
    }

    void OnDestroy()
    {
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
