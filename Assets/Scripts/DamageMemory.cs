using UnityEngine;

// "I was just shot, roughly from there" — the signal that lets the NPC react to
// fire it can't see the source of. Sits alongside PerceptionMemory: this one is
// about the combatant's own body, so it reads Health's damage events rather
// than the sight cone.
//
// The engine adapter half; the decay and bucketing rules are in DamageState.
public class DamageMemory : MonoBehaviour
{
    [Tooltip("How long after a hit RecentlyDamaged stays true and the direction keeps reporting.")]
    [SerializeField] float memorySeconds = 2f;

    readonly DamageState state = new DamageState();
    Health health;

    public bool RecentlyDamaged => state.RecentlyDamaged(Time.time);

    // None while nothing is remembered, or when the hit had no known shooter.
    public HitDirection LastHitDirection => state.LastHitDirection(Time.time);

    // Infinity if never hit.
    public float TimeSinceDamaged => state.TimeSinceDamaged(Time.time);

    void Awake()
    {
        state.memorySeconds = memorySeconds;
        health = GetComponent<Health>();
        if (health != null) health.OnDamaged += HandleDamaged;
    }

    void OnDestroy()
    {
        if (health != null) health.OnDamaged -= HandleDamaged;
    }

    void HandleDamaged(DamageInfo info)
    {
        // The bucket is taken at the moment of the hit: the body turns while the
        // memory decays, and "shot from behind" has to stay behind-at-the-time.
        HitDirection from = info.HasSource
            ? DamageState.Bucket(info.SourcePosition - transform.position, transform.forward)
            : HitDirection.None;
        state.Record(from, Time.time);
    }

    // Episode resets: last episode's hits must not leak into the next.
    public void Forget()
    {
        state.Forget();
    }
}
