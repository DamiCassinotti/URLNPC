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
    [Tooltip("Small negative terminal reward when the round clock runs out (draw). Discourages stalling without forcing a winner.")]
    [SerializeField] float timeoutPenalty = 0.2f;

    [Header("Positioning shaping")]
    [Tooltip("Per-step penalty while the enemy is closer to the player than tooCloseDistance. Discourages melee-rush.")]
    [SerializeField] float tooClosePenaltyPerStep = 0.005f;
    [SerializeField] float tooCloseDistance = 6f;

    EnemyBehavior behavior;
    Health selfHealth;
    Health targetHealth;
    GameManager gameManager;

    bool canAttack = true;
    bool targetInSight = false;
    float normalizedHealth = 1f;

    bool episodeEnding;

    public override void Initialize()
    {
        // The GameManager round clock is the single owner of time-based
        // episode termination (timeout penalty + draw). A nonzero
        // Agent.MaxStep would silently reset the episode before the clock
        // fires — the binary FPS scene carries a stale MaxStep of 5000
        // (~100 s, shorter than the 120 s round), which made timeouts
        // restart the round in place without ever counting a draw.
        if (MaxStep != 0)
        {
            Debug.LogWarning($"[EnemyAgent] Overriding serialized MaxStep {MaxStep} -> 0; the round clock owns episode timeout.");
            MaxStep = 0;
        }

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
        if (behavior != null) behavior.ResetState();
        // Every episode gets a full round clock — during training episodes
        // reset without a scene reload, so the clock must be rearmed here.
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null) gameManager.ResetRoundClock();
    }

    /// <summary>
    /// Called by <see cref="GameManager"/> when the round clock runs out.
    /// Timeout is a draw: both sides take a small penalty (stalling should
    /// not pay off) and the episode ends without a winner.
    /// </summary>
    public void OnRoundTimeout()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        AddReward(-timeoutPenalty);
        EndEpisode();
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
