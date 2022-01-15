using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

public class EnemyAgent : Agent
{

    [Header("ENV PARAMS")]
    EnemyBehavior behavior;
    EnvironmentParameters defaultParameters;

    [Header("Heuristic")]
    [SerializeField] float chaseRange = 30f;
    [SerializeField] float attackRange = 15f;
    float distanceToTarget = Mathf.Infinity;

    [Header("OBSERVATIONS")]
    bool canAttack = true;
    bool targetInSight = false;
    float health;

    public override void Initialize()
    {
        defaultParameters = Academy.Instance.EnvironmentParameters;
        behavior = gameObject.transform.GetComponent<EnemyBehavior>();
    }

    void Update()
    {
        this.targetInSight = behavior.IsTargetInSight();
        this.health = behavior.ReadHealth();
        this.canAttack = behavior.ReadCanAttack();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(canAttack);
        sensor.AddObservation(targetInSight);
        sensor.AddObservation(health);
    }

    public override void Heuristic(float[] actionsOut)
    {
        if (!targetInSight)
        {
            actionsOut[0] = 0;
        } else if (targetInSight && !canAttack)
        {
            actionsOut[0] = 1;
        } else if (targetInSight && canAttack)
        {
            actionsOut[0] = 2;
        }
    }

    public override void OnActionReceived(float[] vectorAction)
    {
        var action = Mathf.FloorToInt(vectorAction[0]);
        switch (action)
        {
            case 0:
            behavior.Patrol();
            SetReward(-0.1f);
            break;
            case 1:
            behavior.Chase();
            SetReward(0.1f);
            break;
            case 2:
            behavior.Attack();
            SetReward(0.5f);
            break;
        }
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
    }
}
