using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBehavior : MonoBehaviour
{
    [Header("PARAMS")]
    [SerializeField] public Transform target;
    [Tooltip("Tag of the opponent this combatant hunts. \"Player\" on the enemy NPC; CombatantRig sets it to \"NPC\" when this script drives the agent-side player body.")]
    [SerializeField] public string targetTag = "Player";
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
            GameObject opponent = GameObject.FindWithTag(targetTag);
            if (opponent != null) target = opponent.transform;
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
        float randomZ = RunRng.Range(RunRng.Stream.Wander, -walkPointRange, walkPointRange);
        float randomX = RunRng.Range(RunRng.Stream.Wander, -walkPointRange, walkPointRange);
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
            GameObject p = GameObject.FindWithTag(targetTag);
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

        // On reload rounds the arena (and its NavMesh) is rebuilt AFTER this
        // agent's OnEnable, so the agent may not be standing on the fresh
        // mesh yet — and CalculatePath errors on a detached agent. Warp
        // attaches it; only then is the reachability probe legal.
        if (!EnsureOnNavMesh(newPosition))
        {
            // No usable NavMesh at all: place the transform directly so the
            // enemy still exists somewhere sane, and let the next reset retry.
            Debug.LogWarning($"[EnemyBehavior] Could not place the enemy on a NavMesh — parking it at {newPosition}.", this);
            transform.position = newPosition;
            return;
        }

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

        // Final guard: snap to the nearest NavMesh point so Warp can never fail
        // and silently strand the enemy at its (off-arena) authored position.
        if (NavMesh.SamplePosition(newPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            newPosition = hit.position;
        }
        if (!navMeshAgent.Warp(newPosition))
        {
            Debug.LogWarning($"[EnemyBehavior] Warp to {newPosition} failed — enemy may be off the NavMesh.");
        }
    }

    // Attach the agent to the NavMesh at (or near) the given point. Warp is
    // the normal path; if the native agent is stuck in a failed-creation
    // state (e.g. it was active while the arena's NavMesh data was swapped),
    // Warp can no-op — toggling the component forces a clean re-creation.
    bool EnsureOnNavMesh(Vector3 point)
    {
        if (navMeshAgent.isOnNavMesh) return true;
        if (navMeshAgent.Warp(point) && navMeshAgent.isOnNavMesh) return true;

        navMeshAgent.enabled = false;
        transform.position = point;
        navMeshAgent.enabled = true;
        return navMeshAgent.isOnNavMesh;
    }

    Vector3 GetRandomPositionInMap()
    {
        // Prefer a point sampled from the procedurally generated arena's NavMesh
        // so spawns stay inside whatever arena was selected this round.
        if (ArenaManager.Current != null) return ArenaManager.Current.RandomGroundPoint();

        float newX = RunRng.Range(RunRng.Stream.Spawn, -60f, 60f);
        float newZ = RunRng.Range(RunRng.Stream.Spawn, -60f, 60f);
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
            // Accept any collider in the target's hierarchy: the tagged root
            // (CharacterController) and untagged child colliders like the
            // "Capsule" visual mesh are both "seeing the target".
            return hit.transform.IsChildOf(target) || hit.transform.CompareTag(targetTag);
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
