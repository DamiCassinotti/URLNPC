using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBehavior : MonoBehaviour
{
    [Header("PARAMS")]
    [SerializeField] public Transform target;
    [Tooltip("Tag of the opponent this combatant hunts. \"Player\" on the enemy NPC; CombatantRig sets it to \"NPC\" when this script drives the agent-side player body.")]
    [SerializeField] public string targetTag = "Player";
    [Tooltip("Range of the local waypoint jitter Wander falls back on when there is no ArenaManager to sample arena-wide points from.")]
    [SerializeField] float walkPointRange = 10f;
    [Tooltip("How many arena points a search leg is picked between: the farthest one in ground not swept yet wins.")]
    [SerializeField] int searchCandidateCount = 4;
    [Tooltip("Side of the patches the search counts as already swept — roughly the width a walk-through clears, not the sight range.")]
    [SerializeField] float searchCellSize = 8f;
    [Tooltip("What a candidate in already-swept ground is worth against one that isn't. Not 0: late in an episode every patch is swept and the sweep still has to go somewhere.")]
    [SerializeField] float searchSweptWeight = 0.35f;
    [Tooltip("Close enough to call a search leg walked.")]
    [SerializeField] float searchArrivalRadius = 2f;
    [Tooltip("Give up on a search leg that hasn't got any closer for this long — a cross-arena destination can come back as a partial path that strands the agent.")]
    [SerializeField] float searchLegTimeoutSeconds = 4f;
    [Tooltip("Slack on \"got closer\", so per-step jitter doesn't keep a stalled search leg alive.")]
    [SerializeField] float searchProgressEpsilon = 0.5f;
    [Tooltip("How far ahead of itself Retreat and the strafes place their NavMesh destination each decision step. (Advance walks the whole way to the last-seen position.)")]
    [SerializeField] float moveStepDistance = 6f;
    [Tooltip("Seconds between cover searches. NearestCoverPoint raycasts and path-checks every cover box in the arena, so MoveToCover walks to the point it already picked in between.")]
    [SerializeField] float coverQueryInterval = 0.25f;
    [Tooltip("Ignored at runtime — CombatBalance.AttackCooldown is forced on in Awake; the FPS scene's Enemy instance pins this at 1 s.")]
    [SerializeField] float attackCooldown = CombatBalance.AttackCooldown;
    [Tooltip("Ignored at runtime — CombatBalance.SightRange is forced on in Awake.")]
    [SerializeField] float sightRange = CombatBalance.SightRange;
    [SerializeField] float sightFovDegrees = 120f;
    [SerializeField] LayerMask sightObstacleMask = ~0;
    [Tooltip("Minimum distance between the enemy's random spawn and the player.")]
    [SerializeField] float minSpawnDistanceFromPlayer = 25f;
    [Tooltip("How close Advance has to get to the last-seen position to call it reached; from there it searches instead of standing on the empty spot.")]
    [SerializeField] float lastSeenArrivalRadius = 2f;

    NavMeshAgent navMeshAgent;
    EnemyWeapon weapon;
    Health enemyHealth;
    // Earliest Time.time the next shot is allowed. A timestamp rather than a
    // coroutine flag so the cooldown observation reads off the same value that
    // gates Attack().
    float attackReadyTime;

    Vector3 coverPoint;
    bool hasCoverPoint;
    float nextCoverQueryTime;

    // Advance reached the last-seen position without finding anyone there.
    bool searching;

    // The target's own agent, resolved lazily: target is a public field the rig
    // writes after Awake, and it can point at either kind of body.
    Transform targetAgentOwner;
    NavMeshAgent targetAgent;

    // Where Wander looks next, and the ground it has already swept this episode.
    readonly SearchPlanner search = new SearchPlanner();
    readonly List<Vector3> searchCandidates = new List<Vector3>();

    // Final snap for a destination sitting just off the mesh; the long
    // overshoots are trimmed by SetStepDestination before they get here.
    const float DestinationSampleRadius = 2f;
    // Below this a trimmed step isn't worth issuing — see SetStepDestination.
    const float MinStepDistance = 0.5f;
    // How far the agent's destination may sit from the search leg's waypoint
    // before Wander calls it someone else's and re-issues its own.
    const float WaypointTolerance = 0.5f;

    // The only source of target info for the NPC brain (sensory contract,
    // issue #9). Auto-added in Awake because the Enemy prefab is binary
    // serialized and can't gain new components via a text edit.
    public PerceptionMemory Perception { get; private set; }

    // "Recently shot, roughly from there" — the other half of the brain's
    // inputs, and auto-added for the same binary-prefab reason.
    public DamageMemory Damage { get; private set; }

    // The commanded mode the policy is conditioned on, auto-added for the same
    // reason. Until a writer commands one it just reports its initial mode.
    public ModeChannel Mode { get; private set; }

    // What this combatant's sight ray can be blocked by. Cover queries must run
    // against the same mask (ArenaManager.NearestCoverPoint).
    public LayerMask SightObstacleMask => sightObstacleMask;

    // How far this combatant can see, i.e. the scale the perceived-distance
    // observation is normalized against.
    public float SightRange => sightRange;

    // Seconds between shots, the scale ReadCooldownRemaining01 normalizes by.
    public float AttackCooldown => attackCooldown;

    void Awake()
    {
        // The FPS scene's Enemy is a prefab instance pinning attackCooldown at
        // 1 s as an override, which no edit to Enemy.prefab reaches — same trap
        // as the stale MaxStep. Code owns the fire rate (CombatBalance).
        attackCooldown = CombatBalance.AttackCooldown;
        // Same trap: the prefab instance in the scene may pin the old 20 m.
        sightRange = CombatBalance.SightRange;
        search.cellSize = searchCellSize;
        search.sweptWeight = searchSweptWeight;
        search.arrivalRadius = searchArrivalRadius;
        search.legTimeoutSeconds = searchLegTimeoutSeconds;
        search.progressEpsilon = searchProgressEpsilon;
        navMeshAgent = GetComponent<NavMeshAgent>();
        weapon = GetComponent<EnemyWeapon>();
        enemyHealth = GetComponent<Health>();
        Perception = GetComponent<PerceptionMemory>();
        if (Perception == null) Perception = gameObject.AddComponent<PerceptionMemory>();
        Damage = GetComponent<DamageMemory>();
        if (Damage == null) Damage = gameObject.AddComponent<DamageMemory>();
        Mode = GetComponent<ModeChannel>();
        if (Mode == null) Mode = gameObject.AddComponent<ModeChannel>();
        // The scripted writer that commands Mode during training, attached the
        // same way so every AI body has one and #65 can't recur on a new
        // combatant. Inert outside training (ModeDirector.trainingOnly), and the
        // find-or-add uses the prefab's serialized instance when there is one.
        // After the channel: its RequireComponent(ModeChannel) would else add a
        // second one. Read back by EnemyAgent via its own GetComponent.
        if (GetComponent<ModeDirector>() == null) gameObject.AddComponent<ModeDirector>();
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
        // Gated on a *fresh* sighting rather than HasEverSeen (issue #72): the
        // old gate let it stand still emptying the magazine at a corner the
        // player had left long ago.
        if (ReadCanAttack() && Perception.SeenWithin(CombatBalance.FiringGraceSeconds))
        {
            Vector3 aim = Perception.LastSeenPosition;
            transform.LookAt(new Vector3(aim.x, transform.position.y, aim.z));
            weapon.Shoot();
            DidShoot = true;
            attackReadyTime = Time.time + attackCooldown;
        }
    }

    // The movement branch's only entry point: one primitive per decision step.
    public void Move(MovementAction action)
    {
        if (!navMeshAgent.isOnNavMesh) return;
        Perception.Refresh();
        // Any sighting ends a search, not just one Advance happens to be
        // running for: the primitive the policy picks in between is its own
        // choice and must not leave the latch stuck.
        if (Perception.CurrentlyVisible) searching = false;
        // Ground covered under any primitive is ground the search has looked at,
        // so a later Wander doesn't send the NPC back through it.
        search.MarkSwept(transform.position);
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
        if (SearchInsteadOfCamping())
        {
            Wander();
            return;
        }
        navMeshAgent.updateRotation = true;
        // The whole way to the remembered spot, so the solver routes around
        // whatever is in between. A straight-line step instead would pick
        // waypoints on the far side of a building and work the doorway.
        SetDestinationOnNavMesh(Perception.LastSeenPosition);
    }

    // Advance walked the whole way to the last-seen position and the target
    // isn't there (issue #72). Standing on an empty spot is not pursuit, so
    // sweep for them instead. Latched, because a wander step away from that
    // spot would otherwise put it back in range of the destination and bounce
    // the NPC between the two; Move clears it on the next sighting.
    bool SearchInsteadOfCamping()
    {
        if (!searching && !Perception.CurrentlyVisible)
        {
            Vector3 toLastSeen = Perception.LastSeenPosition - transform.position;
            toLastSeen.y = 0f;
            searching = toLastSeen.sqrMagnitude <= lastSeenArrivalRadius * lastSeenArrivalRadius;
        }
        return searching;
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
            // The query raises the threat's eye off the position it is handed,
            // so hand it the ground under the remembered one — on an
            // agent-driven target that origin is already lifted (#103).
            Vector3 threat = Perception.LastSeenPosition + Vector3.down * TargetBaseOffset;
            hasCoverPoint = ArenaManager.Current.NearestCoverPoint(
                transform.position, threat, sightObstacleMask,
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

    // The search half of the behavior (issue #93): with nobody in sight this is
    // what every moving primitive falls through to, so how much ground it covers
    // is most of whether the round ends in a kill or a draw. A leg crosses the
    // arena towards ground not swept yet rather than hopping to a point a few
    // metres away, and it is abandoned when it stops making headway.
    void Wander()
    {
        navMeshAgent.updateRotation = true;
        if (!search.HasWaypoint)
        {
            PickSearchLeg();
            return;
        }
        // The policy picks a primitive per decision step, and the others take
        // the wheel while a leg is half walked — Hold clears the path outright,
        // Retreat and the strafes point the agent somewhere else. Put the leg
        // back rather than stand still until it times out.
        if (!DestinationIsSearchLeg())
        {
            navMeshAgent.SetDestination(search.Waypoint);
            return;
        }
        // Nothing to judge the leg on until the solver has answered.
        if (navMeshAgent.pathPending) return;
        if (search.NeedsWaypoint(RemainingToWaypoint(), Time.time)) PickSearchLeg();
    }

    void PickSearchLeg()
    {
        CollectSearchCandidates();
        // The candidates are already on-mesh, so the leg is issued raw rather
        // than through SetDestinationOnNavMesh: a re-snap could land the
        // agent's destination further from the waypoint than the tolerance
        // below, and then every step would re-issue the same leg.
        if (search.TryChoose(transform.position, searchCandidates, Time.time, out Vector3 waypoint))
        {
            navMeshAgent.SetDestination(waypoint);
        }
    }

    // Is the agent still walking the leg the search set, or has another
    // primitive pointed it somewhere else since?
    bool DestinationIsSearchLeg()
    {
        if (!navMeshAgent.hasPath && !navMeshAgent.pathPending) return false;
        return (navMeshAgent.destination - search.Waypoint).sqrMagnitude
               <= WaypointTolerance * WaypointTolerance;
    }

    // How much walking the leg has left. The path length, not the straight
    // line: a leg routed the long way around a building holds its straight-line
    // distance flat for seconds, which the stall check would read as stranded.
    // The straight line is the fallback for when the solver has no length to
    // give, which is the stranded case the timeout is actually for.
    float RemainingToWaypoint()
    {
        float remaining = navMeshAgent.remainingDistance;
        if (!float.IsInfinity(remaining)) return remaining;
        Vector3 toWaypoint = search.Waypoint - transform.position;
        toWaypoint.y = 0f;
        return toWaypoint.magnitude;
    }

    // On-mesh points for the planner to pick a leg between. Arena-wide when the
    // builder is up; the local jitter is the bare fallback for a scene with a
    // NavMesh and no ArenaManager, and an unsampleable jitter point is still
    // offered raw — a leg that has to be timed out beats standing still.
    void CollectSearchCandidates()
    {
        searchCandidates.Clear();
        ArenaManager arena = ArenaManager.Current;
        int wanted = Mathf.Max(1, searchCandidateCount);
        for (int i = 0; i < wanted; i++)
        {
            if (arena != null)
            {
                searchCandidates.Add(arena.RandomGroundPoint(RunRng.Stream.Wander));
                continue;
            }
            Vector3 local = GetNextDestination();
            searchCandidates.Add(
                NavMesh.SamplePosition(local, out NavMeshHit hit, DestinationSampleRadius, NavMesh.AllAreas)
                    ? hit.position
                    : local);
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
        attackReadyTime = 0f;
        DidShoot = false;
        hasCoverPoint = false;
        nextCoverQueryTime = 0f;
        searching = false;
        search.Reset();
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
        // Muzzle height on both ends, not a flat metre off two transforms that
        // don't share an origin (#103). Muzzle rather than eye because this is
        // the shot's own line: Attack turns the body at the target and fires
        // horizontally from there, so probing higher would report a sighting
        // over a wall the bullet goes straight into.
        Vector3 origin = transform.position + Vector3.up * BodyMetrics.MuzzleOffset(SelfHeight, SelfBaseOffset);
        Vector3 aimPoint = target.position + Vector3.up * BodyMetrics.MuzzleOffset(SelfHeight, TargetBaseOffset);
        Vector3 toTarget = aimPoint - origin;
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

    // ENVIRONMENT-SIDE ONLY, same rule as DistanceToTarget: is the target's
    // eye-line to this body broken? IsTargetInSight can't answer that — it is
    // FOV-limited, so it would call "in cover" every step the NPC looked away.
    // Probe and eye height match MoveToCover's cover query, so the point the
    // policy is sent to is a point the reward column pays for.
    public bool IsHiddenFromTarget()
    {
        if (target == null) return false;
        // The head above the grounded base, on both ends: each transform is
        // already lifted by its own agent's base offset (#103) — reading the
        // threat's off this body's made the probe asymmetric, and the two sides
        // of a self-play round disagreed about what cover is.
        Vector3 selfEye = transform.position + Vector3.up * BodyMetrics.EyeOffset(SelfHeight, SelfBaseOffset);
        Vector3 threatEye = target.position + Vector3.up * BodyMetrics.EyeOffset(SelfHeight, TargetBaseOffset);
        return EyeLine.Blocked(threatEye, selfEye, sightObstacleMask, transform, target);
    }

    // This body's agent dimensions, falling back to the 2 m capsule both
    // combatants are. The target's height is not read separately: a human body
    // has no agent to read one off, so the asker's stands in for both — the
    // same approximation MoveToCover's cover query makes with the sight mask.
    float SelfHeight => navMeshAgent != null ? navMeshAgent.height : 2f;
    float SelfBaseOffset => navMeshAgent != null ? navMeshAgent.baseOffset : 0f;

    // How far the target's transform origin sits above the ground it stands on.
    // Zero for a human body, whose origin is its feet.
    float TargetBaseOffset
    {
        get
        {
            if (target == null) return 0f;
            if (targetAgentOwner != target)
            {
                targetAgentOwner = target;
                targetAgent = target.GetComponent<NavMeshAgent>();
            }
            return targetAgent != null ? targetAgent.baseOffset : 0f;
        }
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
        return Time.time >= attackReadyTime;
    }

    // 1 the instant after a shot, 0 once the weapon is ready again.
    public float ReadCooldownRemaining01()
    {
        if (attackCooldown <= 0f) return 0f;
        return Mathf.Clamp01((attackReadyTime - Time.time) / attackCooldown);
    }

    // Current speed against the agent's own top speed, so the policy can tell
    // "sprinting" from "stuck against a wall".
    public float ReadNormalizedSpeed()
    {
        // Reading velocity off an agent that isn't attached to a mesh logs an
        // engine error — before the first spawn, and all through the NavMesh-less
        // test fixtures, it simply isn't moving.
        if (navMeshAgent == null || !navMeshAgent.isActiveAndEnabled
            || !navMeshAgent.isOnNavMesh || navMeshAgent.speed <= 0f)
        {
            return 0f;
        }
        return Mathf.Clamp01(navMeshAgent.velocity.magnitude / navMeshAgent.speed);
    }
}
