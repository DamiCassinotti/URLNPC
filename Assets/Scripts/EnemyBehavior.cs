using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBehavior : MonoBehaviour
{
    [Header("PARAMS")]
    [SerializeField] public Transform target;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] float attackCooldown = 0.5f;
    [SerializeField] float sightRange = 20f;
    [SerializeField] float sightFovDegrees = 120f;
    [SerializeField] LayerMask sightObstacleMask = ~0;
    [Tooltip("Minimum distance between the enemy's random spawn and the player.")]
    [SerializeField] float minSpawnDistanceFromPlayer = 25f;

    NavMeshAgent navMeshAgent;
    EnemyWeapon weapon;
    Health enemyHealth;
    bool canAttack = true;

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = GetComponent<EnemyWeapon>();
        enemyHealth = GetComponent<Health>();
    }

    void Start()
    {
        InitAtRandomPosition();
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    public bool DidShoot { get; private set; }

    public void Attack()
    {
        DidShoot = false;
        if (canAttack && target != null)
        {
            transform.LookAt(new Vector3(target.position.x, transform.position.y, target.position.z));
            weapon.Shoot();
            DidShoot = true;
            canAttack = false;
            StartCoroutine(AttackCooldown());
        }
    }

    public void Chase()
    {
        if (target != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.SetDestination(target.position);
        }
    }

    public void Patrol()
    {
        if (!navMeshAgent.isOnNavMesh) return;
        if (navMeshAgent.remainingDistance < 0.5f || !navMeshAgent.hasPath)
        {
            navMeshAgent.SetDestination(GetNextDestination());
        }
    }

    Vector3 GetNextDestination()
    {
        float randomZ = Random.Range(-walkPointRange, walkPointRange);
        float randomX = Random.Range(-walkPointRange, walkPointRange);
        return new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);
    }

    IEnumerator AttackCooldown()
    {
        yield return new WaitForSeconds(attackCooldown);
        canAttack = true;
    }

    public void InitAtRandomPosition()
    {
        if (navMeshAgent == null) navMeshAgent = GetComponent<NavMeshAgent>();
        Transform playerForSpawn = target;
        if (playerForSpawn == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) playerForSpawn = p.transform;
        }
        Vector3 newPosition = GetRandomPositionInMap();
        int attempts = 0;
        while (attempts < 32)
        {
            bool farEnough = playerForSpawn == null
                || Vector3.Distance(newPosition, playerForSpawn.position) >= minSpawnDistanceFromPlayer;
            bool reachable = navMeshAgent.CalculatePath(newPosition, new NavMeshPath());
            if (farEnough && reachable) break;
            newPosition = GetRandomPositionInMap();
            attempts++;
        }
        navMeshAgent.Warp(newPosition);
    }

    Vector3 GetRandomPositionInMap()
    {
        float newX = Random.Range(-60f, 60f);
        float newZ = Random.Range(-60f, 60f);
        return new Vector3(newX, 0f, newZ);
    }

    public void ResetState()
    {
        StopAllCoroutines();
        canAttack = true;
        DidShoot = false;
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh) navMeshAgent.ResetPath();
        InitAtRandomPosition();
    }

    public bool IsTargetInSight()
    {
        if (target == null) return false;
        Vector3 origin = transform.position + Vector3.up;
        Vector3 toTarget = (target.position + Vector3.up) - origin;
        float distance = toTarget.magnitude;
        if (distance > sightRange) return false;
        float angle = Vector3.Angle(transform.forward, toTarget.normalized);
        if (angle > sightFovDegrees * 0.5f) return false;
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, distance, sightObstacleMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform.CompareTag("Player");
        }
        return true;
    }

    public float DistanceToTarget()
    {
        if (target == null) return Mathf.Infinity;
        return Vector3.Distance(transform.position, target.position);
    }

    public float ReadHealth()
    {
        return enemyHealth != null ? enemyHealth.health : 0f;
    }

    public bool ReadCanAttack()
    {
        return canAttack;
    }
}
