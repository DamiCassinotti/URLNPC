using UnityEngine;

/// <summary>
/// The NPC's only window onto the player. Enforces the sensory contract
/// (issue #9): the NPC never knows the player's HP, and knows the player's
/// position only while it can actually see them — outside line of sight it
/// works from memory (the last-seen position).
///
/// Every policy input (RL observations, future LLM snapshot) and every
/// action that aims or navigates at the player must go through this
/// component. Environment code — spawning, reward computation, the sight
/// check itself — may still read true state; only the NPC *brain* is
/// restricted.
///
/// This component is the engine adapter: the remember/freeze rules live in
/// <see cref="PerceptionState"/>, which gets fed the sight-check result and
/// the current time.
/// </summary>
public class PerceptionMemory : MonoBehaviour
{
    readonly PerceptionState state = new PerceptionState();

    /// <summary>True while the target passes the line-of-sight check this frame.</summary>
    public bool CurrentlyVisible => state.CurrentlyVisible;

    /// <summary>
    /// Where the target was last seen. Only meaningful when
    /// <see cref="HasEverSeen"/> is true. While visible this tracks the live
    /// position; the moment sight breaks it freezes.
    /// </summary>
    public Vector3 LastSeenPosition => state.LastSeenPosition;

    /// <summary>Seconds since the target was last visible. Infinity if never seen.</summary>
    public float TimeSinceSeen => state.TimeSinceSeen(Time.time);

    /// <summary>False until the first successful sighting (and after <see cref="Forget"/>).</summary>
    public bool HasEverSeen => state.HasEverSeen;

    EnemyBehavior behavior;

    void Awake()
    {
        behavior = GetComponent<EnemyBehavior>();
    }

    void Update()
    {
        Refresh();
    }

    /// <summary>
    /// Re-run the sight check and update memory. Also called explicitly at
    /// the top of <see cref="EnemyBehavior.Chase"/>/<see cref="EnemyBehavior.Attack"/>
    /// so ML-Agents decision steps (which fire from FixedUpdate) never act on
    /// a frame-stale snapshot.
    /// </summary>
    public void Refresh()
    {
        bool visible = behavior != null && behavior.IsTargetInSight();
        // The one place live player position legitimately enters NPC state:
        // the sensor reading while the target is visible. (IsTargetInSight is
        // false for a null target, so the position read is safe.)
        state.Observe(visible, visible ? behavior.target.position : Vector3.zero, Time.time);
    }

    /// <summary>Wipe the memory (episode resets — last episode's sighting must not leak).</summary>
    public void Forget()
    {
        state.Forget();
    }
}
