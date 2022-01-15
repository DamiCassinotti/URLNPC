using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

public class EnemyBehavior : Agent
{

    [Header("PARAMS")]
    [SerializeField] Transform target;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] float attackCooldown = 0.5f;
    [SerializeField] float sightRange = 20f;

    [Header("ENV PARAMS")]
    EnemyWeapon weapon;
    NavMeshAgent navMeshAgent;
    Health enemyHealth;
    bool canAttack = true;

    void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = gameObject.transform.GetComponent<EnemyWeapon>();
        enemyHealth = gameObject.transform.GetComponent<Health>();
        InitAtRandomPosition();
    }

    public void Attack()
    {
        if (canAttack)
        {
            weapon.Shoot();
            canAttack = false;
            StartCoroutine(AttackCooldown());
        }
    }

    public void Chase()
    {
        navMeshAgent.SetDestination(target.position);
    }

    public void Patrol()
    {
        navMeshAgent.SetDestination(GetNextDestination());
    }

    Vector3 GetNextDestination()
    {
        float randomZ = UnityEngine.Random.Range(-walkPointRange, walkPointRange);
        float randomX = UnityEngine.Random.Range(-walkPointRange, walkPointRange);
        return new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);
    }

    IEnumerator AttackCooldown()
    {
        yield return new WaitForSeconds(attackCooldown);
        this.canAttack = true;
    }

    public void InitAtRandomPosition()
    {
        Vector3 newPosition = GetRandomPositionInMap();
        while (!navMeshAgent.CalculatePath(newPosition, new NavMeshPath()))
        {
            newPosition = GetRandomPositionInMap();
        }
        gameObject.transform.position = newPosition;
    }

    Vector3 GetRandomPositionInMap()
    {
        float newX = UnityEngine.Random.Range(-60, 60);
        float newZ = UnityEngine.Random.Range(-60, 60);
        return new Vector3(newX, 0, newZ);
    }

    public bool IsTargetInSight()
    {
        Vector3 startPosition = gameObject.transform.position + gameObject.transform.forward * sightRange;
        RaycastHit[] hits = Physics.SphereCastAll(startPosition, sightRange, gameObject.transform.forward);
        bool targetInSight = Array.Exists(hits, element => element.transform.tag == "Player");
        //if (targetInSight) {
        //    Debug.Log("I cann see you");
        //}
        return targetInSight;
    }

    public float ReadHealth()
    {
        return enemyHealth.health;
    }

    public bool ReadCanAttack()
    {
        return canAttack;
    }

}
