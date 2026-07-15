using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
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

    bool canAttack = true;
    bool targetInSight = false;
    float normalizedHealth = 1f;

    bool episodeEnding;

    public override void Initialize()
    {
        behavior = GetComponent<EnemyBehavior>();
        selfHealth = GetComponent<Health>();
        selfHealth.OnDamaged += HandleSelfDamaged;
        selfHealth.OnDied += HandleSelfDied;
    }

    void Update()
    {
        targetInSight = behavior.IsTargetInSight();
        canAttack = behavior.ReadCanAttack();
        float maxHp = selfHealth.maxHealth <= 0f ? 1f : selfHealth.maxHealth;
        normalizedHealth = Mathf.Clamp01(selfHealth.health / maxHp);
        EnsureTargetSubscription();
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
        sensor.AddObservation(canAttack);
        sensor.AddObservation(targetInSight);
        sensor.AddObservation(normalizedHealth);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        if (!targetInSight) discrete[0] = 0;
        else if (!canAttack) discrete[0] = 1;
        else discrete[0] = 2;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeEnding) return;

        AddReward(aliveRewardPerStep);

        if (tooClosePenaltyPerStep > 0f && behavior.DistanceToTarget() < tooCloseDistance)
        {
            AddReward(-tooClosePenaltyPerStep);
        }

        int action = actions.DiscreteActions[0];
        switch (action)
        {
            case 0:
                behavior.Patrol();
                break;
            case 1:
                behavior.Chase();
                break;
            case 2:
                behavior.Attack();
                if (behavior.DidShoot && !targetInSight)
                {
                    AddReward(-wastedShotPenalty);
                }
                break;
        }
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
        // Make every episode attributable to its run seed in TensorBoard.
        // (The authoritative full-precision record is RunRng's startup log.)
        if (Academy.IsInitialized)
        {
            Academy.Instance.StatsRecorder.Add("Run/Seed", RunRng.Seed);
        }
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

    void HandleSelfDamaged(float amount)
    {
        AddReward(-gotHitPenalty);
    }

    void HandleTargetDamaged(float amount)
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
