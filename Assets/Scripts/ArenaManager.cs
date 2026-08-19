using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

// Builds one of several FPS arenas from primitive cubes at runtime and bakes a
// fresh NavMesh over it — the scene is force-binary serialized, so level
// geometry can't be authored and reviewed as text. Any static "Arena" root the
// scene still carries is removed first.
//
// The execution order below runs this before EnemyBehavior/EnemyAgent, so the
// NavMesh exists by the time they try to spawn the enemy on it.
[DefaultExecutionOrder(-10000)]
public class ArenaManager : MonoBehaviour
{
    public static ArenaManager Current { get; private set; }

    [Header("Setup")]
    [Tooltip("Name of any pre-existing arena root in the scene to remove before generating. Leave as 'Arena' to clear the static scene arena.")]
    [SerializeField] string existingArenaRootName = "Arena";
    [Tooltip("Force a specific arena (0..4). Leave at -1 to pick randomly at startup.")]
    [SerializeField] internal int forcedArenaIndex = -1;
    [Tooltip("Run seed for reproducible evaluation: drives arena selection, spawn sampling and wander waypoints (see RunRng). 0 = random seed each run. Overridden by '-runSeed <int>' on the command line.")]
    [SerializeField] internal int runSeed = 0;
    [Tooltip("Teleport the player to a random NavMesh point on start (seeded — see RunRng), so rounds don't always open from the same spot. Also keeps the player inside whatever arena was generated.")]
    [SerializeField] internal bool repositionPlayerOnStart = true;

    [Header("Perimeter walls")]
    [SerializeField] float wallHeight = 5f;
    [SerializeField] float wallThickness = 1f;

    GameObject arenaRoot;
    readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();
    // Every LOS-blocking box this arena was built from, so cover queries don't
    // have to raycast-search the world.
    readonly List<Collider> coverColliders = new List<Collider>();

    // Half the floor size, i.e. center to the inner wall face.
    public float HalfExtentX { get; private set; }
    public float HalfExtentZ { get; private set; }
    public Vector3 PlayerSpawn { get; private set; }
    public int ActiveArenaIndex { get; private set; }
    public string ActiveArenaName { get; private set; }

    public const int ArenaCount = 5;

    // Fallback bootstrap: if no ArenaManager was placed in the scene, spawn
    // one so arenas still generate out of the box. AfterSceneLoad only fires
    // for the FIRST scene of a session, but the manager dies with every
    // SceneManager.LoadScene — hence the sceneLoaded hook, without which
    // rounds 2+ get no manager, no re-roll and the stale static "Arena" root.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrap()
    {
        // -= before += so a second play session (domain reload disabled)
        // doesn't stack subscriptions.
        SceneManager.sceneLoaded -= EnsureExistsOnSceneLoad;
        SceneManager.sceneLoaded += EnsureExistsOnSceneLoad;
        EnsureExists();
    }

    static void EnsureExistsOnSceneLoad(Scene scene, LoadSceneMode mode)
    {
        // Fires after the scene's Awakes but before Starts, so the arena and
        // its NavMesh exist before EnemyBehavior.Start spawns the enemy.
        EnsureExists();
    }

    // Test seam: PlayMode tests build their own minimal scenes and must not
    // have an arena auto-spawned into them by the sceneLoaded hook.
    internal static bool suppressAutoBootstrap;

    static void EnsureExists()
    {
        if (suppressAutoBootstrap) return;
        if (Current != null) return;
        if (FindAnyObjectByType<ArenaManager>() != null) return;
        new GameObject("ArenaManager (auto)").AddComponent<ArenaManager>();
    }

    void Awake()
    {
        Current = this;
        // Seed the run before any randomness is consumed. No-op on scene
        // reloads within the same run, so the deterministic sequences keep
        // advancing round to round instead of restarting.
        RunRng.EnsureInitialized(runSeed);
        RemoveExistingArena();
        // Clear any NavMesh data baked into the scene so only our fresh bake is live.
        NavMesh.RemoveAllNavMeshData();
        BuildRandomArena();
        BakeNavMesh();
    }

    void Start()
    {
        // Random, not the fixed south-wall spawn: every round should open
        // from a fresh position in every mode, human play included. Runs at
        // execution order -10000, i.e. before EnemyBehavior.Start, so the
        // enemy's min-spawn-separation check measures against this position.
        if (repositionPlayerOnStart) RepositionPlayerAtRandomPoint();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    // ---------------------------------------------------------------- bounds

    // The floor sits at y = 0, so anything sampled noticeably above it is a
    // platform/rampart/stair/crate top. Spawns stay at or below this height:
    // actors belong on the open floor, not perched out of sight on cover.
    const float GroundLevelMaxY = 0.6f;

    // Gap a spawn keeps from any cover box, i.e. the body radius (the Enemy
    // capsule is 0.5) plus a margin. Being on the NavMesh is not enough on its
    // own: the bake insets the mesh around anything tall enough to block, but
    // geometry the agent can step over — stair steps — isn't carved at all, so
    // a sampled point can sit right against a box or on top of a step and the
    // capsule ends up clipping through it (issue #74).
    const float SpawnCoverClearance = 0.9f;

    // A random floor-level point on the baked NavMesh, comfortably inside the
    // walls and clear of cover. Once a NavMesh is baked this always returns an
    // on-mesh point, never the off-mesh origin.
    public Vector3 RandomGroundPoint() => RandomGroundPoint(RunRng.Stream.Spawn);

    // Same point sampling off a caller-chosen stream: search waypoints draw a
    // policy-dependent number of times, which on the spawn stream would shift
    // every later spawn and break the per-domain isolation RunRng is for.
    public Vector3 RandomGroundPoint(RunRng.Stream stream)
    {
        float marginX = Mathf.Max(2f, HalfExtentX - 2f);
        float marginZ = Mathf.Max(2f, HalfExtentZ - 2f);
        Vector3 crowdedFallback = Vector3.zero;
        bool haveCrowdedFallback = false;
        Vector3 elevatedFallback = Vector3.zero;
        bool haveElevatedFallback = false;
        for (int i = 0; i < 64; i++)
        {
            float x = RunRng.Range(stream, -marginX, marginX);
            float z = RunRng.Range(stream, -marginZ, marginZ);
            // Small search radius so a floor query snaps to the floor directly
            // below it instead of jumping up onto a nearby raised platform.
            if (NavMesh.SamplePosition(new Vector3(x, 0.5f, z), out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                if (hit.position.y > GroundLevelMaxY)
                {
                    if (!haveElevatedFallback) { elevatedFallback = hit.position; haveElevatedFallback = true; }
                    continue;
                }
                if (IsClearOfCover(hit.position)) return hit.position;
                if (!haveCrowdedFallback) { crowdedFallback = hit.position; haveCrowdedFallback = true; }
            }
        }
        // Nothing clear turned up — a spot against a box still beats an
        // elevated one, and both beat an off-mesh origin that a Warp would
        // silently reject.
        if (haveCrowdedFallback) return crowdedFallback;
        if (haveElevatedFallback) return elevatedFallback;
        if (NavMesh.SamplePosition(new Vector3(PlayerSpawn.x, 0.5f, PlayerSpawn.z), out NavMeshHit spawnHit, 12f, NavMesh.AllAreas))
            return spawnHit.position;
        Debug.LogWarning("[ArenaManager] RandomGroundPoint found no NavMesh — is the arena baked?");
        return Vector3.zero;
    }

    // How far apart two actors can realistically be in this arena.
    public float SpawnSeparationCap => Mathf.Min(HalfExtentX, HalfExtentZ) * 1.4f;

    // ----------------------------------------------------------------- cover

    const float CoverStandOff = 1.2f;    // how far behind the cover face to stand

    // Whether a body standing here would be clear of every cover box the arena
    // was built from. Uses the world AABB, which is exact for the axis-aligned
    // boxes the builder emits (the stairs are rotated in 90° steps).
    public bool IsClearOfCover(Vector3 point)
    {
        const float sqrClearance = SpawnCoverClearance * SpawnCoverClearance;
        foreach (Collider cover in coverColliders)
        {
            if (cover == null) continue;
            // SqrDistance is 0 inside the box, so "on top of a stair step"
            // fails this too.
            if (cover.bounds.SqrDistance(point) < sqrClearance) return false;
        }
        return true;
    }

    // Nearest walkable point hidden from breakLosFrom and reachable from `from`.
    // False when this layout offers none — the caller must fall back to another
    // action. Two things must match the real actors, so the caller supplies them:
    // `sightObstacleMask` is the threat's own (EnemyBehavior.SightObstacleMask),
    // or a point comes back "hidden" behind geometry the threat sees through; and
    // `eyeHeight` is the asker's own height above the floor — the head cover has
    // to hide, so a 1 m constant would call a point hidden behind a 1.5 m wall
    // that a 2 m head clears. Both parameters stand in for the threat too: the
    // same eyeHeight raises the threat's eye and the asker's, so a human player's
    // real eye level isn't modelled — the asker's is used for both, the same
    // approximation as the mask. `asker` is the agent running the query: its own
    // colliders are ignored, or its capsule can block the LOS probe and get an
    // exposed point accepted (the default `~0` mask sees it, and arena geometry
    // shares its layer).
    public bool NearestCoverPoint(Vector3 from, Vector3 breakLosFrom, LayerMask sightObstacleMask,
        Transform asker, float eyeHeight, out Vector3 coverPoint)
    {
        coverPoint = default;
        // Snap the origin on-mesh, otherwise the path checks below all fail.
        if (NavMesh.SamplePosition(from + Vector3.up, out NavMeshHit fromHit, 4f, NavMesh.AllAreas))
        {
            from = fromHit.position;
        }

        Vector3 threatEye = breakLosFrom + Vector3.up * eyeHeight;
        NavMeshPath path = new NavMeshPath();
        float bestDist = float.PositiveInfinity;
        bool found = false;

        foreach (Collider cover in coverColliders)
        {
            if (cover == null) continue;
            Bounds bounds = cover.bounds;
            Vector3 center = new Vector3(bounds.center.x, 0f, bounds.center.z);

            // The far side of the cover from the threat, one stand-off past the
            // object's horizontal footprint.
            Vector3 away = center - new Vector3(breakLosFrom.x, 0f, breakLosFrom.z);
            if (away.sqrMagnitude < 1e-4f) continue; // threat is on top of it
            away.Normalize();
            float footprint = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            Vector3 candidate = center + away * (footprint + CoverStandOff);

            // Cheapest rejections first — the raycast and path query below cost.
            if (Vector3.Distance(from, candidate) >= bestDist) continue;
            if (!NavMesh.SamplePosition(candidate + Vector3.up, out NavMeshHit navHit, 3f, NavMesh.AllAreas)) continue;
            candidate = navHit.position;
            float dist = Vector3.Distance(from, candidate);
            if (dist >= bestDist) continue;

            // Something has to interrupt the threat's eye-line — normally this
            // very object, so low geometry like stair steps self-rejects.
            Vector3 candidateEye = candidate + Vector3.up * eyeHeight;
            if (!EyeLine.Blocked(threatEye, candidateEye, sightObstacleMask, asker)) continue;

            if (!NavMesh.CalculatePath(from, candidate, NavMesh.AllAreas, path)) continue;
            if (path.status != NavMeshPathStatus.PathComplete) continue;

            bestDist = dist;
            coverPoint = candidate;
            found = true;
        }
        return found;
    }

    // ------------------------------------------------------------- lifecycle

    void RemoveExistingArena()
    {
        if (string.IsNullOrEmpty(existingArenaRootName)) return;
        GameObject existing = GameObject.Find(existingArenaRootName);
        if (existing != null && existing != gameObject)
        {
            // Deactivate immediately so its colliders / NavMeshSurface stop
            // affecting the world before the (deferred) Destroy runs.
            existing.SetActive(false);
            Destroy(existing);
        }
    }

    // Called from Start every round (all modes) and by EnemyAgent on episode
    // begin during training, so spawns spread across the arena instead of
    // clustering at a fixed spot or wherever the last episode ended.
    public void RepositionPlayerAtRandomPoint()
    {
        MovePlayerTo(RandomGroundPoint());
    }

    void MovePlayerTo(Vector3 point)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        // Snap to the nearest walkable spot so the player never spawns inside a
        // wall/divider, whatever arena was generated.
        Vector3 ground = point;
        bool onNavMesh = NavMesh.SamplePosition(new Vector3(point.x, 1f, point.z), out NavMeshHit hit, 10f, NavMesh.AllAreas);
        if (onNavMesh) ground = hit.position;

        // An agent-driven player (CombatantRig) is steered by a NavMeshAgent,
        // which owns the transform from its own internal position — a raw
        // positional write gets overridden and the body snaps back.
        NavMeshAgent nav = player.GetComponent<NavMeshAgent>();
        if (nav != null && nav.enabled && onNavMesh)
        {
            WarpPlayerAgent(nav, ground);
            return;
        }

        Vector3 target = onNavMesh ? ground + Vector3.up * 1.1f : point;
        CharacterController cc = player.GetComponent<CharacterController>();
        bool wasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false; // CharacterController fights direct position writes.
        player.transform.position = target;
        if (cc != null) cc.enabled = wasEnabled;
    }

    // Same pattern as EnemyBehavior.EnsureOnNavMesh: Warp is the normal path,
    // but it no-ops when the native agent lost its mesh (arena rebuilt under
    // it), and toggling the component forces a clean re-creation.
    static void WarpPlayerAgent(NavMeshAgent nav, Vector3 target)
    {
        if (nav.isOnNavMesh) nav.ResetPath(); // drop last round's destination
        if (nav.Warp(target) && nav.isOnNavMesh) return;

        nav.enabled = false;
        nav.transform.position = target;
        nav.enabled = true;
        if (!nav.isOnNavMesh)
        {
            Debug.LogWarning($"[ArenaManager] Could not place the player agent on the NavMesh at {target}.", nav);
        }
    }

    void BakeNavMesh()
    {
        NavMeshSurface surface = arenaRoot.GetComponent<NavMeshSurface>();
        if (surface == null) surface = arenaRoot.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.layerMask = ~0;
        surface.BuildNavMesh();
    }

    // ---------------------------------------------------------- arena select

    void BuildRandomArena()
    {
        int idx = (forcedArenaIndex >= 0 && forcedArenaIndex < ArenaCount)
            ? forcedArenaIndex
            : RunRng.Range(RunRng.Stream.Arena, 0, ArenaCount);
        ActiveArenaIndex = idx;
        arenaRoot = new GameObject("Arena (Generated)");

        switch (idx)
        {
            case 0: BuildCourtyard(); break;
            case 1: BuildThePit(); break;
            case 2: BuildTwinTowers(); break;
            case 3: BuildMaze(); break;
            case 4: BuildRamparts(); break;
        }

        Debug.Log($"[ArenaManager] Built arena #{idx} \"{ActiveArenaName}\" ({HalfExtentX * 2f:0}x{HalfExtentZ * 2f:0}).");
    }

    // ------------------------------------------------------- arena layouts

    // Arena 0 — open courtyard with a central building and scattered crates.
    void BuildCourtyard()
    {
        ActiveArenaName = "Courtyard";
        Material floor = MakeMat(new Color(0.76f, 0.70f, 0.50f));  // sand
        Material wall = MakeMat(new Color(0.70f, 0.35f, 0.25f));   // terracotta
        Material cover = MakeMat(new Color(0.22f, 0.42f, 0.70f)); // blue
        Material crate = MakeMat(new Color(0.88f, 0.80f, 0.30f)); // yellow
        BuildFloorAndWalls(20f, 20f, floor, wall);

        House(new Vector3(0f, 0f, 2f), 11f, 9f, 4f, 3f, cover);

        foreach (Vector3 c in new[]
        {
            new Vector3(-12f, 0f, 10f), new Vector3(12f, 0f, 10f),
            new Vector3(-12f, 0f, -10f), new Vector3(12f, 0f, -10f),
        })
        {
            Box("Crate", c + Vector3.up * 1f, new Vector3(2f, 2f, 2f), crate);
            Box("Crate", c + new Vector3(1.6f, 1f, 0.4f), new Vector3(1.6f, 2f, 1.6f), crate);
        }

        Box("LowWall", new Vector3(-7f, 0.75f, -8f), new Vector3(6f, 1.5f, 0.8f), cover);
        Box("LowWall", new Vector3(7f, 0.75f, -8f), new Vector3(6f, 1.5f, 0.8f), cover);
    }

    // Arena 1 — small, tight pit with two raised corner platforms reached by stairs.
    void BuildThePit()
    {
        ActiveArenaName = "The Pit";
        Material floor = MakeMat(new Color(0.40f, 0.40f, 0.43f));  // grey
        Material wall = MakeMat(new Color(0.85f, 0.45f, 0.15f));   // orange
        Material cover = MakeMat(new Color(0.20f, 0.60f, 0.32f)); // green
        Material crate = MakeMat(new Color(0.90f, 0.85f, 0.80f)); // bone
        BuildFloorAndWalls(14f, 14f, floor, wall);

        Platform(new Vector3(9f, 0f, 9f), 7f, 7f, 2.5f, cover);
        Stairs(new Vector3(9f, 0f, 4.5f), Vector3.forward, 10, 0.25f, 0.55f, 4f, cover);

        Platform(new Vector3(-9f, 0f, -9f), 7f, 7f, 2.5f, cover);
        Stairs(new Vector3(-9f, 0f, -4.5f), Vector3.back, 10, 0.25f, 0.55f, 4f, cover);

        // Central staggered cover so the lanes aren't a straight shot.
        Box("Pillar", new Vector3(0f, 1.5f, 0f), new Vector3(2f, 3f, 2f), crate);
        Box("Crate", new Vector3(-4f, 1f, 4f), new Vector3(2f, 2f, 2f), crate);
        Box("Crate", new Vector3(4f, 1f, -4f), new Vector3(2f, 2f, 2f), crate);
    }

    // Arena 2 — big arena dominated by two tall tower buildings.
    void BuildTwinTowers()
    {
        ActiveArenaName = "Twin Towers";
        Material floor = MakeMat(new Color(0.18f, 0.18f, 0.22f)); // near-black slate
        Material wall = MakeMat(new Color(0.80f, 0.80f, 0.85f));  // light grey
        Material tower = MakeMat(new Color(0.75f, 0.22f, 0.22f)); // red
        Material crate = MakeMat(new Color(0.90f, 0.78f, 0.25f)); // amber
        Material barrier = MakeMat(new Color(0.60f, 0.60f, 0.65f)); // steel
        BuildFloorAndWalls(30f, 30f, floor, wall);

        House(new Vector3(-15f, 0f, 8f), 13f, 13f, 8f, 4f, tower);
        House(new Vector3(15f, 0f, -8f), 13f, 13f, 8f, 4f, tower);

        // Long mid-field sightline blockers, or this arena is one big open shot.
        Box("Barrier", new Vector3(0f, 1.5f, 14f), new Vector3(16f, 3f, 1f), barrier);
        Box("Barrier", new Vector3(0f, 1.5f, -14f), new Vector3(16f, 3f, 1f), barrier);

        Box("Crate", new Vector3(-6f, 1f, -6f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(6f, 1f, 6f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(20f, 1f, 18f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(-20f, 1f, -18f), new Vector3(2.5f, 2f, 2.5f), crate);
    }

    // Arena 3 — flat maze of wall segments and pillars.
    void BuildMaze()
    {
        ActiveArenaName = "Maze";
        Material floor = MakeMat(new Color(0.15f, 0.45f, 0.45f)); // teal
        Material wall = MakeMat(new Color(0.90f, 0.90f, 0.90f));  // white
        Material pillar = MakeMat(new Color(0.90f, 0.80f, 0.20f)); // yellow
        BuildFloorAndWalls(22f, 22f, floor, wall);

        Material maze = MakeMat(new Color(0.85f, 0.85f, 0.88f));
        float h = 3f, t = 1f;
        // A loose grid of offset wall segments — leaves a clear lane near the
        // south spawn and never fully seals any cell.
        Box("Maze", new Vector3(-10f, h / 2, 8f), new Vector3(14f, h, t), maze);
        Box("Maze", new Vector3(8f, h / 2, 12f), new Vector3(t, h, 16f), maze);
        Box("Maze", new Vector3(-2f, h / 2, 2f), new Vector3(t, h, 16f), maze);
        Box("Maze", new Vector3(6f, h / 2, -2f), new Vector3(14f, h, t), maze);
        Box("Maze", new Vector3(-12f, h / 2, -4f), new Vector3(t, h, 14f), maze);
        Box("Maze", new Vector3(-4f, h / 2, -10f), new Vector3(16f, h, t), maze);
        Box("Maze", new Vector3(13f, h / 2, -6f), new Vector3(12f, h, t), maze);

        foreach (Vector3 p in new[]
        {
            new Vector3(-2f, 0f, 10f), new Vector3(8f, 0f, 4f),
            new Vector3(-12f, 0f, 3f), new Vector3(6f, 0f, -10f),
        })
        {
            Box("Pillar", p + Vector3.up * 1.5f, new Vector3(1.2f, 3f, 1.2f), pillar);
        }
    }

    // Arena 4 — rectangular arena with raised ramparts down the long sides.
    void BuildRamparts()
    {
        ActiveArenaName = "Ramparts";
        Material floor = MakeMat(new Color(0.30f, 0.50f, 0.25f)); // grass green
        Material wall = MakeMat(new Color(0.45f, 0.32f, 0.20f));  // brown
        Material rampart = MakeMat(new Color(0.20f, 0.70f, 0.75f)); // cyan
        Material crate = MakeMat(new Color(0.88f, 0.78f, 0.55f)); // tan
        BuildFloorAndWalls(28f, 18f, floor, wall);

        Platform(new Vector3(-25f, 0f, 0f), 5f, 30f, 2.5f, rampart);
        Platform(new Vector3(25f, 0f, 0f), 5f, 30f, 2.5f, rampart);
        Stairs(new Vector3(-21f, 0f, 0f), Vector3.left, 9, 0.28f, 0.55f, 5f, rampart);
        Stairs(new Vector3(21f, 0f, 0f), Vector3.right, 9, 0.28f, 0.55f, 5f, rampart);

        // Central divider with two gaps to funnel movement.
        Box("Divider", new Vector3(0f, 1.75f, 9f), new Vector3(1f, 3.5f, 14f), wall);
        Box("Divider", new Vector3(0f, 1.75f, -9f), new Vector3(1f, 3.5f, 14f), wall);

        Box("Crate", new Vector3(-10f, 1f, 6f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(10f, 1f, -6f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(8f, 1f, 8f), new Vector3(2.5f, 2f, 2.5f), crate);
        Box("Crate", new Vector3(-8f, 1f, -8f), new Vector3(2.5f, 2f, 2.5f), crate);
    }

    // -------------------------------------------------------- build helpers

    void BuildFloorAndWalls(float halfX, float halfZ, Material floorMat, Material wallMat)
    {
        HalfExtentX = halfX;
        HalfExtentZ = halfZ;
        Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(halfX * 2f, 1f, halfZ * 2f), floorMat);

        float h = wallHeight, t = wallThickness;
        Box("Wall_North", new Vector3(0f, h / 2f, halfZ + t / 2f), new Vector3(halfX * 2f + t * 2f, h, t), wallMat);
        Box("Wall_South", new Vector3(0f, h / 2f, -halfZ - t / 2f), new Vector3(halfX * 2f + t * 2f, h, t), wallMat);
        Box("Wall_East", new Vector3(halfX + t / 2f, h / 2f, 0f), new Vector3(t, h, halfZ * 2f), wallMat);
        Box("Wall_West", new Vector3(-halfX - t / 2f, h / 2f, 0f), new Vector3(t, h, halfZ * 2f), wallMat);

        // Player spawns just inside the south wall, facing the arena.
        PlayerSpawn = new Vector3(0f, 1.2f, -halfZ + 3f);
    }

    // A 4-walled building with a door gap centered on the south face.
    void House(Vector3 center, float width, float depth, float height, float doorWidth, Material mat)
    {
        float t = 0.6f;
        float hx = width / 2f, hz = depth / 2f;
        Box("House_N", center + new Vector3(0f, height / 2f, hz), new Vector3(width, height, t), mat);
        Box("House_E", center + new Vector3(hx, height / 2f, 0f), new Vector3(t, height, depth), mat);
        Box("House_W", center + new Vector3(-hx, height / 2f, 0f), new Vector3(t, height, depth), mat);

        // South face split around a doorway.
        float sideWidth = (width - doorWidth) / 2f;
        float sideOffset = (doorWidth + sideWidth) / 2f;
        Box("House_S", center + new Vector3(-sideOffset, height / 2f, -hz), new Vector3(sideWidth, height, t), mat);
        Box("House_S", center + new Vector3(sideOffset, height / 2f, -hz), new Vector3(sideWidth, height, t), mat);
    }

    // A solid raised platform; top surface sits at topY.
    void Platform(Vector3 center, float width, float depth, float topY, Material mat)
    {
        Box("Platform", new Vector3(center.x, topY / 2f, center.z), new Vector3(width, topY, depth), mat);
    }

    // A solid stepped staircase running along dir. Each step is full-height to
    // the floor so the side profile reads as stairs and provides cover.
    // Returns the world position at the top of the last step.
    Vector3 Stairs(Vector3 frontBase, Vector3 dir, int steps, float rise, float run, float width, Material mat)
    {
        dir = dir.normalized;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        for (int i = 0; i < steps; i++)
        {
            float topY = rise * (i + 1);
            Vector3 center = frontBase + dir * (run * (i + 0.5f)) + Vector3.up * (topY / 2f);
            Box($"Step_{i}", center, new Vector3(width, topY, run), mat, rot);
        }
        return frontBase + dir * (run * steps) + Vector3.up * (rise * steps);
    }

    GameObject Box(string name, Vector3 center, Vector3 size, Material mat)
    {
        return Box(name, center, size, mat, Quaternion.identity);
    }

    GameObject Box(string name, Vector3 center, Vector3 size, Material mat, Quaternion rotation)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(arenaRoot.transform, false);
        go.transform.SetPositionAndRotation(center, rotation);
        go.transform.localScale = size;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;

        // Cover candidate for NearestCoverPoint. Perimeter walls are excluded
        // because their far side is outside the arena; stairs and other low
        // boxes stay in and self-reject on the eye-height LOS check.
        if (name != "Floor" && !name.StartsWith("Wall_"))
        {
            coverColliders.Add(go.GetComponent<Collider>());
        }
        return go;
    }

    // Cached URP/Lit material for a solid color (falls back to Standard).
    Material MakeMat(Color c)
    {
        if (matCache.TryGetValue(c, out Material cached)) return cached;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        matCache[c] = mat;
        return mat;
    }
}
