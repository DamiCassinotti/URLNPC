using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

/// <summary>
/// Makes the player side interchangeable (issue #10): the same body —
/// capsule + <see cref="Health"/> + weapon + camera anchor, tag "Player" —
/// can be driven by a Human (StarterAssets first-person controller +
/// <see cref="PlayerWeapon"/>) or by an Agent (ML-Agents
/// <see cref="PlayerAgent"/> + NavMeshAgent + <see cref="EnemyWeapon"/>-style
/// firing). Exactly one driver is active per run.
///
/// The scene is force-binary serialized, so the Agent driver's components
/// can't be authored in the scene file — this rig composes them at runtime
/// on the existing player body, the same trick <c>ArenaManager</c> and
/// <c>GameManager</c> already use. The Human driver is whatever the scene
/// already carries; in Human mode the rig leaves it alone.
///
/// Driver selection, highest priority first:
///  1. command line: <c>-playerDriver agent</c> / <c>-playerDriver human</c>
///  2. <see cref="DriverOverride"/> (set from code, e.g. a menu/GameManager,
///     before the scene loads)
///  3. the <c>driver</c> Inspector field (default Human)
///
/// <see cref="Health"/>, the "Player" tag and <c>GameManager.ProcessDeath</c>
/// are untouched by the switch, so win/loss handling works identically for
/// either driver.
/// </summary>
[DefaultExecutionOrder(-100)] // decide the driver before any driver's Start runs
public class CombatantRig : MonoBehaviour
{
    public enum DriverKind { Human, Agent }

    [Tooltip("Which driver controls the player body this run. Overridden by '-playerDriver <human|agent>' on the command line, or DriverOverride from code.")]
    [SerializeField] DriverKind driver = DriverKind.Human;

    [Header("Agent driver")]
    [Tooltip("Behavior name for the agent-driven player. Keep it equal to the enemy's (URLNPC) to share one policy for self-play; use a different name to train a separate player policy.")]
    [SerializeField] string agentBehaviorName = "URLNPC";
    [Tooltip("ML-Agents team id for the player side. The enemy defaults to 0; a distinct id lets the trainer treat the matchup as adversarial self-play.")]
    [SerializeField] int agentTeamId = 1;
    [SerializeField] float agentMoveSpeed = 4f;

    /// <summary>Code-level driver selection (e.g. from a future menu or GameManager). Set before the scene loads; survives scene reloads.</summary>
    public static DriverKind? DriverOverride;

    /// <summary>The driver that actually won the selection this run.</summary>
    public DriverKind ActiveDriver { get; private set; }

    /// <summary>
    /// The human-driver components the agent path silences, named as strings
    /// because they cannot be referenced at compile time (see
    /// <see cref="EnableAgentDriver"/>). Exposed so the test suite can assert
    /// against the names actually used here rather than a second copy of them.
    /// </summary>
    internal static readonly string[] HumanDriverTypeNames =
    {
        "FirstPersonController",
        "StarterAssetsInputs",
    };

    // The scene is binary, so the rig can't be added to the player in the
    // editor by editing text — attach it at runtime if it isn't there.
    // AfterSceneLoad runs after scene Awakes but before Starts, which is
    // early enough to silence the human driver before it initializes.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrap()
    {
        if (FindAnyObjectByType<CombatantRig>() != null) return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) player.AddComponent<CombatantRig>();
    }

    void Awake()
    {
        ActiveDriver = ResolveDriver();
        if (ActiveDriver == DriverKind.Agent) EnableAgentDriver();
        // Human: the body as authored in the scene IS the human driver —
        // nothing to compose, nothing to disable.
        Debug.Log($"[CombatantRig] Player driver: {ActiveDriver}.");
    }

    DriverKind ResolveDriver()
    {
        return DriverSelector.Resolve(System.Environment.GetCommandLineArgs(), DriverOverride, driver);
    }

    void EnableAgentDriver()
    {
        // --- 1. Silence every input-driven component on the body. ----------
        // StarterAssets lives in Assembly-CSharp, which auto-references this
        // assembly (URLNPC) — so URLNPC can't reference it back and the two
        // controller types can't be named at compile time. Match by type name.
        foreach (string typeName in HumanDriverTypeNames) DisableByTypeName(typeName);
        Disable<PlayerInput>();
        Disable<PlayerWeapon>();
        // CharacterController fights NavMeshAgent for the transform.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // --- 2. NavMesh locomotion (ArenaManager baked the mesh in Awake,
        // execution order -10000, so it already exists). -------------------
        NavMeshAgent nav = gameObject.AddComponent<NavMeshAgent>();
        nav.speed = agentMoveSpeed;
        nav.angularSpeed = 720f;
        nav.acceleration = 16f;
        if (cc != null)
        {
            nav.radius = cc.radius;
            nav.height = cc.height;
        }

        // --- 3. Combat stack: reuse the enemy scripts on the player body.
        // Weapon must exist before EnemyBehavior — its Awake caches it. -----
        gameObject.AddComponent<EnemyWeapon>();
        EnemyBehavior behavior = gameObject.AddComponent<EnemyBehavior>();
        behavior.targetTag = "NPC"; // this combatant hunts the enemy
        GameObject npc = GameObject.FindWithTag("NPC");
        if (npc != null) behavior.target = npc.transform;

        // --- 4. ML-Agents plumbing. BehaviorParameters must exist and be
        // configured BEFORE the Agent component initializes against it. -----
        // (Check Health first: PlayerAgent's RequireComponent would silently
        // auto-add a fresh one and mask a mis-set-up body.)
        if (GetComponent<Health>() == null)
        {
            Debug.LogError("[CombatantRig] Player body has no Health on its root — the agent driver can't take damage or die properly.");
        }
        BehaviorParameters bp = gameObject.AddComponent<BehaviorParameters>();
        bp.BehaviorName = agentBehaviorName;
        bp.TeamId = agentTeamId;
        bp.BrainParameters.VectorObservationSize = 3;   // canAttack, targetInSight, normalizedHealth
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(3); // Patrol / Chase / Attack

        // MaxStep is deliberately left at 0: EnemyAgent.Initialize forces it
        // there so the GameManager round clock is the single owner of
        // time-based episode termination on both sides of the fight.
        gameObject.AddComponent<PlayerAgent>();
        DecisionRequester requester = gameObject.AddComponent<DecisionRequester>();
        requester.DecisionPeriod = 5; // same as the Enemy prefab
    }

    // Searches children too: the weapon hangs off the camera rig, not the root.
    void Disable<T>() where T : Behaviour
    {
        T component = GetComponentInChildren<T>(true);
        if (component != null) component.enabled = false;
    }

    // Same, for types this assembly can't reference (see EnableAgentDriver).
    void DisableByTypeName(string typeName)
    {
        foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == typeName) component.enabled = false;
        }
    }
}
