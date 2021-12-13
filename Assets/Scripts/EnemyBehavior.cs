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
    [SerializeField] float attackCooldown = 1f;

    EnemyWeapon weapon;
    NavMeshAgent navMeshAgent;
    float distanceToTarget = Mathf.Infinity;
    bool canAttack = true;

    void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = gameObject.transform.GetComponent<EnemyWeapon>();
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
        if (canAttack)
        {
            weapon.Shoot();
            canAttack = false;
            StartCoroutine(AttackCooldown());
        }
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

    IEnumerator AttackCooldown()
    {
        yield return new WaitForSeconds(attackCooldown);
        this.canAttack = true;
    }

}
