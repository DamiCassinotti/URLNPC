using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBehavior : MonoBehaviour
{

    [SerializeField] Transform target;
    [SerializeField] float chaseRange = 30f;
    [SerializeField] float attackRange = 15f;
    [SerializeField] float walkPointRange = 10f;

    NavMeshAgent navMeshAgent;
    float distanceToTarget = Mathf.Infinity;

    void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        distanceToTarget = Vector3.Distance(target.position, transform.position);
        if (distanceToTarget <= attackRange)
        {
            Chase();
            Attack();
        } else if (distanceToTarget <= chaseRange) {
            Chase();
        } else {
            Patrol();
        }
    }

    void Attack()
    {
        EnemyWeapon weapon = gameObject.transform.GetComponent<EnemyWeapon>();
        weapon.Shoot();
    }

    void Chase()
    {
        navMeshAgent.SetDestination(target.position);
    }

    void Patrol()
    {
        navMeshAgent.SetDestination(GetNextDestination());
    }

    Vector3 GetNextDestination()
    {
        float randomZ = Random.Range(-walkPointRange, walkPointRange);
        float randomX = Random.Range(-walkPointRange, walkPointRange);
        return new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, chaseRange);
    }

}
