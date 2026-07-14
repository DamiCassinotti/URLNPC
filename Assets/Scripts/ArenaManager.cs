using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Procedurally builds one of several FPS arenas at runtime and bakes a fresh
/// NavMesh over it. On <see cref="Awake"/> it removes any static arena baked
/// into the scene (the "Arena" root) so generated geometry is the only thing
/// present, picks a random layout, builds it from primitive cubes with
/// contrasting URP materials, and bakes the NavMesh so the enemy NavMeshAgent
/// has somewhere to walk.
///
/// Runs very early (<see cref="DefaultExecutionOrder"/> -10000) so the NavMesh
/// exists before <see cref="EnemyBehavior"/> / <see cref="EnemyAgent"/> try to
/// spawn the enemy on it. <see cref="EnemyBehavior"/> queries
/// <see cref="Current"/> for valid spawn points instead of hard-coded bounds.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class ArenaManager : MonoBehaviour
{
    public static ArenaManager Current { get; private set; }

    [Header("Setup")]
    [Tooltip("Name of any pre-existing arena root in the scene to remove before generating. Leave as 'Arena' to clear the static scene arena.")]
    [SerializeField] string existingArenaRootName = "Arena";
    [Tooltip("Force a specific arena (0..4). Leave at -1 to pick randomly at startup.")]
    [SerializeField] int forcedArenaIndex = -1;
    [Tooltip("Run seed for reproducible evaluation: drives arena selection, spawn sampling and wander waypoints (see RunRng). 0 = random seed each run. Overridden by '-runSeed <int>' on the command line.")]
    [SerializeField] int runSeed = 25910267;
    [Tooltip("Teleport the player to a random NavMesh point on start (seeded — see RunRng), so rounds don't always open from the same spot. Also keeps the player inside whatever arena was generated.")]
    [SerializeField] bool repositionPlayerOnStart = true;

    [Header("Perimeter walls")]
    [SerializeField] float wallHeight = 5f;
    [SerializeField] float wallThickness = 1f;

    GameObject arenaRoot;
    readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();

    /// <summary>Half the floor size along X (distance from center to the inner wall face).</summary>
    public float HalfExtentX { get; private set; }
    /// <summary>Half the floor size along Z.</summary>
    public float HalfExtentZ { get; private set; }
    /// <summary>Where the player should be placed for the current arena.</summary>
    public Vector3 PlayerSpawn { get; private set; }
    /// <summary>Index of the arena that was built this session.</summary>
    public int ActiveArenaIndex { get; private set; }
    /// <summary>Human-readable name of the active arena.</summary>
    public string ActiveArenaName { get; private set; }

    public const int ArenaCount = 5;

    // Fallback bootstrap: if no ArenaManager was placed in the scene, spawn
    // one so arenas still generate out of the box. AfterSceneLoad only fires
    // for the FIRST scene of a session, but the manager and its generated
    // arena die with every SceneManager.LoadScene — without the sceneLoaded
    // hook, rounds 2+ had no manager at all: no re-roll, and the old static
    // "Arena" root baked into the scene was never removed, so every round
    // after the first showed the same hand-authored arena.
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

    static void EnsureExists()
    {
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

        // Active NavMeshAgents must sit out the data swap: AddNavMeshData
        // immediately re-creates every active agent, and one still standing
        // at its authored scene position — outside a small arena like The
        // Pit — fails with "Failed to create agent because it is not close
        // enough to the NavMesh" and is left in a broken state that survives
        // later Warp calls (the "missing enemy" bug).
        var suspendedAgents = new List<NavMeshAgent>();
        foreach (NavMeshAgent agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (!agent.enabled) continue;
            agent.enabled = false;
            suspendedAgents.Add(agent);
        }

        // Clear any NavMesh data baked into the scene so only our fresh bake is live.
        NavMesh.RemoveAllNavMeshData();
        BuildRandomArena();
        BakeNavMesh();

        foreach (NavMeshAgent agent in suspendedAgents)
        {
            if (agent == null) continue;
            // Snap onto the new mesh BEFORE re-enabling so the native agent
            // is created cleanly. Proper spawn placement still belongs to the
            // owner (EnemyBehavior warps to its own random point in Start).
            if (NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, 100f, NavMesh.AllAreas))
            {
                agent.transform.position = hit.position;
            }
            agent.enabled = true;
        }
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

    /// <summary>
    /// A random point that sits on the baked NavMesh, comfortably inside the
    /// arena walls. Returns <see cref="Vector3.zero"/> if nothing is found
    /// (should not happen once a NavMesh is baked).
    /// </summary>
    public Vector3 RandomGroundPoint()
    {
        float marginX = Mathf.Max(2f, HalfExtentX - 2f);
        float marginZ = Mathf.Max(2f, HalfExtentZ - 2f);
        for (int i = 0; i < 48; i++)
        {
            float x = RunRng.Range(RunRng.Stream.Spawn, -marginX, marginX);
            float z = RunRng.Range(RunRng.Stream.Spawn, -marginZ, marginZ);
            if (NavMesh.SamplePosition(new Vector3(x, 1f, z), out NavMeshHit hit, 6f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }
        return Vector3.zero;
    }

    /// <summary>How far apart two actors can realistically be in this arena.</summary>
    public float SpawnSeparationCap => Mathf.Min(HalfExtentX, HalfExtentZ) * 1.4f;

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

    /// <summary>
    /// Teleport the player to a random walkable point. Called from
    /// <see cref="Start"/> every round (all modes) and by
    /// <see cref="EnemyAgent"/> on episode begin during training, so player
    /// spawns are distributed across the arena instead of clustering at a
    /// fixed spot or wherever the last episode ended.
    /// </summary>
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
        Vector3 target = point;
        if (NavMesh.SamplePosition(new Vector3(point.x, 1f, point.z), out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            target = hit.position + Vector3.up * 1.1f;
        }

        CharacterController cc = player.GetComponent<CharacterController>();
        bool wasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false; // CharacterController fights direct position writes.
        player.transform.position = target;
        if (cc != null) cc.enabled = wasEnabled;
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

        // Central house with a door facing south (toward the player spawn).
        House(new Vector3(0f, 0f, 2f), 11f, 9f, 4f, 3f, cover);

        // Corner crate stacks for cover.
        foreach (Vector3 c in new[]
        {
            new Vector3(-12f, 0f, 10f), new Vector3(12f, 0f, 10f),
            new Vector3(-12f, 0f, -10f), new Vector3(12f, 0f, -10f),
        })
        {
            Box("Crate", c + Vector3.up * 1f, new Vector3(2f, 2f, 2f), crate);
            Box("Crate", c + new Vector3(1.6f, 1f, 0.4f), new Vector3(1.6f, 2f, 1.6f), crate);
        }

        // Two low flanking walls.
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

        // Raised platforms in NE and SW corners, with stairs leading up.
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

        // Two towers (tall houses) with doorways facing the center.
        House(new Vector3(-15f, 0f, 8f), 13f, 13f, 8f, 4f, tower);
        House(new Vector3(15f, 0f, -8f), 13f, 13f, 8f, 4f, tower);

        // Long mid-field sightline blockers.
        Box("Barrier", new Vector3(0f, 1.5f, 14f), new Vector3(16f, 3f, 1f), barrier);
        Box("Barrier", new Vector3(0f, 1.5f, -14f), new Vector3(16f, 3f, 1f), barrier);

        // Crate clusters around the towers for short cover.
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

        // Decorative pillars at junctions, also useful cover.
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

        // Raised walkways (ramparts) along the east and west edges.
        Platform(new Vector3(-25f, 0f, 0f), 5f, 30f, 2.5f, rampart);
        Platform(new Vector3(25f, 0f, 0f), 5f, 30f, 2.5f, rampart);
        // Stairs up to each rampart from mid-field.
        Stairs(new Vector3(-21f, 0f, 0f), Vector3.left, 9, 0.28f, 0.55f, 5f, rampart);
        Stairs(new Vector3(21f, 0f, 0f), Vector3.right, 9, 0.28f, 0.55f, 5f, rampart);

        // Central divider with two gaps to funnel movement.
        Box("Divider", new Vector3(0f, 1.75f, 9f), new Vector3(1f, 3.5f, 14f), wall);
        Box("Divider", new Vector3(0f, 1.75f, -9f), new Vector3(1f, 3.5f, 14f), wall);

        // Crates for ground-level cover.
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
