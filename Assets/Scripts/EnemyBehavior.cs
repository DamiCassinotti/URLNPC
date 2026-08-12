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
    [Tooltip("How far ahead of itself Retreat and the strafes place their NavMesh destination each decision step. (Advance walks the whole way to the last-seen position.)")]
    [SerializeField] float moveStepDistance = 6f;
    [Tooltip("Seconds between cover searches. NearestCoverPoint raycasts and path-checks every cover box in the arena, so MoveToCover walks to the point it already picked in between.")]
    [SerializeField] float coverQueryInterval = 0.25f;
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

    Vector3 coverPoint;
    bool hasCoverPoint;
    float nextCoverQueryTime;

    // Final snap for a destination sitting just off the mesh; the long
    // overshoots are trimmed by SetStepDestination before they get here.
    const float DestinationSampleRadius = 2f;
    // Below this a trimmed step isn't worth issuing — see SetStepDestination.
    const float MinStepDistance = 0.5f;

    // The only source of target info for the NPC brain (sensory contract,
    // issue #9). Auto-added in Awake because the Enemy prefab is binary
    // serialized and can't gain new components via a text edit.
    public PerceptionMemory Perception { get; private set; }

    // "Recently shot, roughly from there" — the other half of the brain's
    // inputs, and auto-added for the same binary-prefab reason.
    public DamageMemory Damage { get; private set; }

    // What this combatant's sight ray can be blocked by. Cover queries must run
    // against the same mask (ArenaManager.NearestCoverPoint).
    public LayerMask SightObstacleMask => sightObstacleMask;

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = GetComponent<EnemyWeapon>();
        enemyHealth = GetComponent<Health>();
        Perception = GetComponent<PerceptionMemory>();
        if (Perception == null) Perception = gameObject.AddComponent<PerceptionMemory>();
        Damage = GetComponent<DamageMemory>();
        if (Damage == null) Damage = gameObject.AddComponent<DamageMemory>();
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
        // Aim at the last-seen position, never the live one. While the target
        // is visible they are the same; behind cover the shot goes where the
        // NPC believes the player is, and eats the wastedShotPenalty if wrong.
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

    // The movement branch's only entry point: one primitive per decision step.
    public void Move(MovementAction action)
    {
        if (!navMeshAgent.isOnNavMesh) return;
        Perception.Refresh();
        switch (action)
        {
            case MovementAction.Hold: Hold(); break;
            case MovementAction.Advance: Advance(); break;
            case MovementAction.Retreat: Retreat(); break;
            case MovementAction.StrafeLeft: Strafe(-1f); break;
            case MovementAction.StrafeRight: Strafe(1f); break;
            case MovementAction.MoveToCover: MoveToCover(); break;
            case MovementAction.Wander: Wander(); break;
        }
    }

    // Stand still, watching where the target was last seen — the NPC keeps its
    // bearing instead of drifting off the one it had when it stopped.
    void Hold()
    {
        navMeshAgent.ResetPath();
        navMeshAgent.velocity = Vector3.zero;
        FaceBearing(PerceivedBearing());
    }

    // Close on the perceived position. Last-seen, not true position: with the
    // player behind a wall the enemy heads for the corner it lost sight at
    // instead of wallhack-tracking.
    void Advance()
    {
        if (WanderIfBlind()) return;
        navMeshAgent.updateRotation = true;
        // The whole way to the remembered spot, so the solver routes around
        // whatever is in between. A straight-line step instead would pick
        // waypoints on the far side of a building and work the doorway.
        SetDestinationOnNavMesh(Perception.LastSeenPosition);
    }

    void Retreat()
    {
        if (WanderIfBlind()) return;
        navMeshAgent.updateRotation = true; // turn and run; watching is Hold's job
        SetStepDestination(-PerceivedBearing());
    }

    // Sidestep while keeping the perceived position in front: a strafe that let
    // the NavMeshAgent turn the body would swing the target out of the sight
    // cone, which is the opposite of what circling cover is for.
    void Strafe(float sign)
    {
        if (WanderIfBlind()) return;
        Vector3 bearing = PerceivedBearing();
        SetStepDestination(Vector3.Cross(Vector3.up, bearing) * sign);
        FaceBearing(bearing);
    }

    // The bearing-relative primitives have no bearing before the first sighting.
    // Running off own facing wedges the NPC nose-first into the first wall it
    // reaches — pinned facing, so it never turns away, stops moving against the
    // wall and so never sees anything to break out with. Cover ground to find
    // the target instead. (Hold has no such trap: standing still is well defined
    // with no target, so it keeps the own-facing fallback.)
    bool WanderIfBlind()
    {
        if (Perception.HasEverSeen) return false;
        Wander();
        return true;
    }

    // Break the perceived threat's line of sight. The arena knows where its own
    // cover is; when this layout offers none, opening distance is the fallback.
    void MoveToCover()
    {
        // Nothing to hide from: no query to rate-limit, so leave the timer be —
        // arming it here would skip the query for a full interval after the
        // first sighting and fall back to Retreat in the open with cover about.
        if (ArenaManager.Current == null || !Perception.HasEverSeen)
        {
            hasCoverPoint = false;
        }
        else if (Time.time >= nextCoverQueryTime)
        {
            // The query wants the mask of whatever blocks the *threat's* view;
            // a human player has no sight model to read, so the NPC's own
            // stands in. The same thing while both keep the default. Eye height
            // is the agent's full height — the head above its grounded base that
            // cover has to hide. Reading height, not baseOffset + 1, keeps it
            // right whether the body is centred on a lifted transform (the Enemy
            // prefab) or stands feet-on-mesh (CombatantRig's agent player).
            nextCoverQueryTime = Time.time + coverQueryInterval;
            hasCoverPoint = ArenaManager.Current.NearestCoverPoint(
                transform.position, Perception.LastSeenPosition, sightObstacleMask,
                transform, navMeshAgent.height, out coverPoint);
        }

        if (!hasCoverPoint)
        {
            Retreat();
            return;
        }
        navMeshAgent.updateRotation = true;
        SetDestinationOnNavMesh(coverPoint);
    }

    void Wander()
    {
        navMeshAgent.updateRotation = true;
        if (navMeshAgent.remainingDistance < 0.5f || !navMeshAgent.hasPath)
        {
            SetDestinationOnNavMesh(GetNextDestination());
        }
    }

    // Where the NPC believes the target is, as a flat unit vector. Only Hold
    // reads this before the first sighting (it faces its own way and stands
    // still); the moving primitives Wander instead of running off own facing.
    Vector3 PerceivedBearing()
    {
        if (Perception.HasEverSeen)
        {
            Vector3 toTarget = Perception.LastSeenPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 1e-4f) return toTarget.normalized;
        }
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
    }

    void FaceBearing(Vector3 bearing)
    {
        // The agent would rewrite the rotation from its own velocity next tick.
        navMeshAgent.updateRotation = false;
        transform.rotation = Quaternion.LookRotation(bearing, Vector3.up);
    }

    // A step aims at open ground rather than a known goal, so one that crosses
    // a wall is trimmed back to the last point on the mesh along it. Untrimmed
    // it maps to whatever mesh sits nearest the overshoot, which can be on the
    // far side of the wall — turning a sidestep into a walk around the block.
    void SetStepDestination(Vector3 direction)
    {
        // Snap the origin on-mesh first: the Enemy prefab lifts the body a metre
        // by its NavMeshAgent base offset, so transform.position sits above the
        // surface and NavMesh.Raycast off it maps unpredictably. Same guard as
        // ArenaManager.NearestCoverPoint and SetDestinationOnNavMesh.
        Vector3 origin = transform.position;
        if (NavMesh.SamplePosition(origin, out NavMeshHit onMesh, DestinationSampleRadius, NavMesh.AllAreas))
        {
            origin = onMesh.position;
        }
        Vector3 point = origin + direction * moveStepDistance;
        if (NavMesh.Raycast(origin, point, out NavMeshHit edge, NavMesh.AllAreas))
        {
            // Already on the boundary: the trimmed step is the agent's own
            // position, and issuing that as a destination pins it there.
            // Nowhere to go this way, so stop — carrying on would run out the
            // destination an earlier primitive set, and a cornered Retreat
            // would walk the last Advance straight at what it is backing from.
            if (edge.distance < MinStepDistance)
            {
                navMeshAgent.ResetPath();
                return;
            }
            point = edge.position;
        }
        SetDestinationOnNavMesh(point);
    }

    void SetDestinationOnNavMesh(Vector3 point)
    {
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, DestinationSampleRadius, NavMesh.AllAreas))
        {
            point = hit.position;
        }
        navMeshAgent.SetDestination(point);
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
        // Sample the generated arena's NavMesh so spawns stay inside whatever
        // layout was selected this round; the bounds below are a bare fallback.
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
        hasCoverPoint = false;
        nextCoverQueryTime = 0f;
        if (Perception != null) Perception.Forget();
        if (Damage != null) Damage.Forget();
        if (navMeshAgent != null)
        {
            // Hold/the strafes park it on manual rotation; the next episode
            // starts on the agent's own steering again.
            navMeshAgent.updateRotation = true;
            if (navMeshAgent.isOnNavMesh) navMeshAgent.ResetPath();
        }
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

    // ENVIRONMENT-SIDE ONLY. Reward computation may read true state; never feed
    // this to observations or actions — the brain goes through PerceptionMemory.
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
