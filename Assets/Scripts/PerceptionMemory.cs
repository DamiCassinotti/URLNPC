using UnityEngine;

// The NPC's only window onto the player (sensory contract, issue #9): it never
// knows the player's HP, and knows their position only while it can see them —
// out of sight it works from the last-seen position, and once that sighting is
// older than memorySeconds it knows nothing again.
//
// Every policy input and every action that aims or navigates at the player must
// go through here. Environment code (spawning, reward computation, the sight
// check itself) may still read true state; only the NPC brain is restricted.
//
// The engine adapter half; the remember/freeze rules are in PerceptionState.
public class PerceptionMemory : MonoBehaviour
{
    [Tooltip("How long a sighting survives out of sight before the NPC is blind again (issue #72).")]
    [SerializeField] internal float memorySeconds = NpcObservations.SeenHorizonSeconds;

    readonly PerceptionState state = new PerceptionState();

    public bool CurrentlyVisible => state.CurrentlyVisible;

    // Tracks the live position while visible, then freezes. Only meaningful
    // once HasEverSeen is true.
    public Vector3 LastSeenPosition => state.LastSeenPosition;

    // Infinity if never seen.
    public float TimeSinceSeen => state.TimeSinceSeen(Time.time);

    public bool HasEverSeen => state.HasEverSeen;

    // Is the last sighting no older than the given window? The fire gate reads
    // this so the NPC can suppress a corner someone just ducked behind without
    // shooting at a memory for the rest of the round.
    public bool SeenWithin(float seconds) => state.SeenWithin(seconds, Time.time);

    EnemyBehavior behavior;

    void Awake()
    {
        behavior = GetComponent<EnemyBehavior>();
    }

    void Update()
    {
        Refresh();
    }

    // Also called explicitly from EnemyBehavior.Move/Attack so ML-Agents
    // decision steps, which fire from FixedUpdate, never act on a frame-stale
    // snapshot.
    public void Refresh()
    {
        // Pushed every refresh rather than cached in Awake, so an Inspector (or
        // fixture) edit to the horizon takes effect without a restart.
        state.memorySeconds = memorySeconds;
        bool visible = behavior != null && behavior.IsTargetInSight();
        // The one place live player position legitimately enters NPC state:
        // the sensor reading while the target is visible. (IsTargetInSight is
        // false for a null target, so the position read is safe.)
        state.Observe(visible, visible ? behavior.target.position : Vector3.zero, Time.time);
    }

    // Episode resets: last episode's sighting must not leak into the next.
    public void Forget()
    {
        state.Forget();
    }
}
