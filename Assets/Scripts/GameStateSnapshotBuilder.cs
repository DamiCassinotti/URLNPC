using UnityEngine;

// Assembles the LLM tier's GameStateSnapshot (issue #123) from the same
// restricted sources the policy reads — PerceptionMemory, DamageMemory, own
// Health, ModeChannel — plus the round context (GameManager's clock, the
// CounterData score, ArenaManager's descriptor). Auto-added by
// EnemyBehavior.Awake alongside the memories, so every AI body accumulates its
// observed-player stats from round start; the accumulation rules are in
// ObservedTargetStats, the bucketing in GameStateSnapshot.Build.
public class GameStateSnapshotBuilder : MonoBehaviour
{
    readonly ObservedTargetStats observed = new ObservedTargetStats();

    EnemyBehavior behavior;
    Health selfHealth;
    GameManager gameManager;

    // The scouting report the snapshot's observed-player stats are read off.
    public ObservedTargetStats Observed => observed;

    void Awake()
    {
        behavior = GetComponent<EnemyBehavior>();
        selfHealth = GetComponent<Health>();
    }

    void OnEnable()
    {
        Weapon.ShotFired += HandleShotFired;
    }

    void OnDisable()
    {
        Weapon.ShotFired -= HandleShotFired;
    }

    void Update()
    {
        PerceptionMemory perception = behavior != null ? behavior.Perception : null;
        if (perception == null) return;
        // No forced Refresh — that costs a raycast, and a frame-stale
        // CurrentlyVisible only blurs a time-weighted mean.
        observed.Sample(perception.CurrentlyVisible, perception.LastSeenPosition,
            FlatDistanceTo(perception.LastSeenPosition), Time.deltaTime);
    }

    void HandleShotFired(Weapon weapon, Health victim, float damage)
    {
        // The opponent's shots only, attributed by root tag the way
        // TelemetryLogger does; ObservedTargetStats drops the ones fired while
        // the target is unseen.
        if (behavior == null) return;
        if (!weapon.transform.root.CompareTag(behavior.targetTag)) return;
        observed.RecordShot();
    }

    // Flat, because the two body origins sit at different heights (#103) and a
    // bucketed range has no use for the metre that adds.
    float FlatDistanceTo(Vector3 point)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        return to.magnitude;
    }

    // Episode resets, called from EnemyBehavior.ResetState like the memories.
    public void ResetState()
    {
        observed.Reset();
    }

    public GameStateSnapshot BuildSnapshot()
    {
        PerceptionMemory perception = behavior != null ? behavior.Perception : null;
        DamageMemory damage = behavior != null ? behavior.Damage : null;
        ModeChannel mode = behavior != null ? behavior.Mode : null;
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        ArenaManager arena = ArenaManager.Current;

        return GameStateSnapshot.Build(new GameStateInput
        {
            health = selfHealth != null ? selfHealth.health : 0f,
            maxHealth = selfHealth != null ? selfHealth.maxHealth : 0f,

            targetVisible = perception != null && perception.CurrentlyVisible,
            hasEverSeen = perception != null && perception.HasEverSeen,
            distanceToLastSeen = perception != null ? FlatDistanceTo(perception.LastSeenPosition) : 0f,
            timeSinceSeen = perception != null ? perception.TimeSinceSeen : Mathf.Infinity,
            seenHorizonSeconds = perception != null ? perception.MemorySeconds : NpcObservations.SeenHorizonSeconds,

            recentlyDamaged = damage != null && damage.RecentlyDamaged,
            hitDirection = damage != null ? damage.LastHitDirection : HitDirection.None,

            roundTimeRemaining = gameManager != null ? gameManager.RemainingRoundTime : 0f,
            playerWins = CounterData.readUserPoints(),
            npcWins = CounterData.readNpcPoints(),
            draws = CounterData.readDraws(),

            arenaIndex = arena != null ? arena.ActiveArenaIndex : -1,
            arenaName = arena != null ? arena.ActiveArenaName : "",
            coverDensity = arena != null ? arena.CoverDensity : 0f,

            observedSeconds = observed.VisibleSeconds,
            meanEngagementDistance = observed.MeanEngagementDistance,
            shotsPer10Seconds = observed.ShotsPer10Seconds,
            observedMeanSpeed = observed.MeanSpeed,

            mode = mode != null ? mode.CurrentMode : NpcMode.Hunt,
            timeInMode = mode != null ? mode.TimeInMode : 0f,
        });
    }
}
