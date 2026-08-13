using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

// Makes the player side interchangeable (issue #10): the same body can be
// driven by a Human (the StarterAssets controller authored in the scene) or by
// an Agent. Exactly one driver is active per run.
//
// The scene is force-binary serialized, so the agent driver's components can't
// be authored there — the rig composes them at runtime on the existing player
// body. Human mode touches nothing.
//
// Health, the "Player" tag and GameManager.ProcessDeath are untouched by the
// switch, so win/loss handling is identical for either driver.
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

    // Set from code (a future menu, say) before the scene loads; survives reloads.
    public static DriverKind? DriverOverride;

    public DriverKind ActiveDriver { get; private set; }

    // The human-driver components the agent path silences, as strings because
    // they can't be referenced at compile time (see EnableAgentDriver).
    // Internal so the tests assert against these names, not a second copy.
    internal static readonly string[] HumanDriverTypeNames =
    {
        "FirstPersonController",
        "StarterAssetsInputs",
    };

    // AfterSceneLoad runs after the scene's Awakes but before Starts, which is
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
        // StarterAssets lives in Assembly-CSharp, which auto-references this
        // assembly (URLNPC) — so URLNPC can't reference it back and the two
        // controller types can't be named at compile time. Match by type name.
        foreach (string typeName in HumanDriverTypeNames) DisableByTypeName(typeName);
        Disable<PlayerInput>();
        Disable<PlayerWeapon>();
        // CharacterController fights NavMeshAgent for the transform.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // ArenaManager baked the mesh in Awake at execution order -10000, so
        // it already exists by the time this agent needs it.
        NavMeshAgent nav = gameObject.AddComponent<NavMeshAgent>();
        nav.speed = agentMoveSpeed;
        nav.angularSpeed = 720f;
        nav.acceleration = 16f;
        if (cc != null)
        {
            nav.radius = cc.radius;
            // Height also feeds MoveToCover's eye height, so the agent player
            // judges cover against its real head instead of a shorter default
            // one — leaving it unset reintroduces the #56 low-cover bug.
            nav.height = cc.height;
        }

        // The weapon must exist before EnemyBehavior — its Awake caches it.
        gameObject.AddComponent<EnemyWeapon>();
        EnemyBehavior behavior = gameObject.AddComponent<EnemyBehavior>();
        behavior.targetTag = "NPC"; // this combatant hunts the enemy
        GameObject npc = GameObject.FindWithTag("NPC");
        if (npc != null) behavior.target = npc.transform;

        // Check Health before adding PlayerAgent: its RequireComponent would
        // silently auto-add a fresh one and mask a mis-set-up body.
        if (GetComponent<Health>() == null)
        {
            Debug.LogError("[CombatantRig] Player body has no Health on its root — the agent driver can't take damage or die properly.");
        }
        // Configured before PlayerAgent is added: the Agent initializes against it.
        BehaviorParameters bp = gameObject.AddComponent<BehaviorParameters>();
        bp.BehaviorName = agentBehaviorName;
        bp.TeamId = agentTeamId;
        // One shared copy of the brain interface, so this can't drift from what
        // the Enemy prefab declares (#43).
        bp.BrainParameters.VectorObservationSize = NpcBrainSpec.ObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = NpcBrainSpec.Actions;

        // The scripted mode writer, matching the Enemy prefab: without it this
        // side trains the shared mode-conditioned policy stuck on initialMode.
        // Added before PlayerAgent so the agent's Initialize caches it and can
        // redraw the mode on episode begin. RequireComponent supplies the
        // ModeChannel; EnemyBehavior already added one, so this finds it.
        gameObject.AddComponent<ModeDirector>();

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
