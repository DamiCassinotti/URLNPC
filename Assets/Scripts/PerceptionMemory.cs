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
/// </summary>
public class PerceptionMemory : MonoBehaviour
{
    /// <summary>True while the target passes the line-of-sight check this frame.</summary>
    public bool CurrentlyVisible { get; private set; }

    /// <summary>
    /// Where the target was last seen. Only meaningful when
    /// <see cref="HasEverSeen"/> is true. While visible this tracks the live
    /// position; the moment sight breaks it freezes.
    /// </summary>
    public Vector3 LastSeenPosition { get; private set; }

    /// <summary>Seconds since the target was last visible. Infinity if never seen.</summary>
    public float TimeSinceSeen => HasEverSeen ? Time.time - lastSeenTime : Mathf.Infinity;

    /// <summary>False until the first successful sighting (and after <see cref="Forget"/>).</summary>
    public bool HasEverSeen { get; private set; }

    EnemyBehavior behavior;
    float lastSeenTime;

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
        CurrentlyVisible = behavior != null && behavior.IsTargetInSight();
        if (CurrentlyVisible && behavior.target != null)
        {
            // The one place live player position legitimately enters NPC
            // state: the sensor reading while the target is visible.
            LastSeenPosition = behavior.target.position;
            lastSeenTime = Time.time;
            HasEverSeen = true;
        }
    }

    /// <summary>Wipe the memory (episode resets — last episode's sighting must not leak).</summary>
    public void Forget()
    {
        CurrentlyVisible = false;
        HasEverSeen = false;
        LastSeenPosition = Vector3.zero;
    }
}
