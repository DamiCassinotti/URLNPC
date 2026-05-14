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
        Health th = behavior.target.GetComponent<Health>();
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
