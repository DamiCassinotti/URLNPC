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

    /// <summary>
    /// The only source of target info for the NPC brain (sensory contract,
    /// issue #9). Auto-added at runtime because the Enemy prefab is binary
    /// serialized and can't gain new components via a text edit.
    /// </summary>
    public PerceptionMemory Perception { get; private set; }

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = GetComponent<EnemyWeapon>();
        enemyHealth = GetComponent<Health>();
        Perception = GetComponent<PerceptionMemory>();
        if (Perception == null) Perception = gameObject.AddComponent<PerceptionMemory>();
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
        Perception.Refresh();
        // Aim at the last-seen position, never the live one — while the
        // target is visible they are the same thing; behind cover the shot
        // goes where the NPC *believes* the player is (and eats the
        // wastedShotPenalty if it's wrong).
        if (canAttack && Perception.HasEverSeen)
        {
            Vector3 aim = Perception.LastSeenPosition;
            transform.LookAt(new Vector3(aim.x, transform.position.y, aim.z));
            weapon.Shoot();
            DidShoot = true;
            canAttack = false;
            StartCoroutine(AttackCooldown());
        }
    }

    public void Chase()
    {
        if (!navMeshAgent.isOnNavMesh) return;
        Perception.Refresh();
        // Navigate to where the target was last seen, not where they truly
        // are — with the player behind a wall the enemy heads to the corner
        // it lost sight at instead of wallhack-tracking. Never seen anyone?
        // Then there is nothing to chase.
        if (Perception.HasEverSeen)
        {
            navMeshAgent.SetDestination(Perception.LastSeenPosition);
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
        // Tight arenas can't satisfy a large min separation — cap it to what
        // the current arena can actually fit so we don't burn every attempt.
        float effectiveMinDistance = minSpawnDistanceFromPlayer;
        if (ArenaManager.Current != null)
        {
            effectiveMinDistance = Mathf.Min(minSpawnDistanceFromPlayer, ArenaManager.Current.SpawnSeparationCap);
        }

        Vector3 newPosition = GetRandomPositionInMap();
        int attempts = 0;
        while (attempts < 32)
        {
            bool farEnough = playerForSpawn == null
                || Vector3.Distance(newPosition, playerForSpawn.position) >= effectiveMinDistance;
            bool reachable = navMeshAgent.CalculatePath(newPosition, new NavMeshPath());
            if (farEnough && reachable) break;
            newPosition = GetRandomPositionInMap();
            attempts++;
        }
        navMeshAgent.Warp(newPosition);
    }

    Vector3 GetRandomPositionInMap()
    {
        // Prefer a point sampled from the procedurally generated arena's NavMesh
        // so spawns stay inside whatever arena was selected this round.
        if (ArenaManager.Current != null) return ArenaManager.Current.RandomGroundPoint();

        float newX = Random.Range(-60f, 60f);
        float newZ = Random.Range(-60f, 60f);
        return new Vector3(newX, 0f, newZ);
    }

    public void ResetState()
    {
        StopAllCoroutines();
        canAttack = true;
        DidShoot = false;
        if (Perception != null) Perception.Forget();
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

    // True distance to the live target position. ENVIRONMENT-SIDE ONLY: used
    // by EnemyAgent for reward computation (tooClose penalty), which is
    // allowed to read true state. Never feed this to observations or actions
    // — the brain goes through PerceptionMemory.
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
