using UnityEngine;

// The raw values GameStateSnapshotBuilder gathers off the components; the
// bucketing below is what turns them into the snapshot. Target info comes off
// PerceptionMemory only (sensory contract, issue #9) — there is deliberately no
// field for the player's HP or true position. The weapon cooldown is left out
// too: at 0.5 s against a multi-second decision period it is stale before the
// answer lands. The rule: the LLM tier only receives state whose timescale is
// at least its own decision period.
public struct GameStateInput
{
    public float health;
    public float maxHealth;

    public bool targetVisible;
    public bool hasEverSeen;
    public float distanceToLastSeen;
    public float timeSinceSeen;
    public float seenHorizonSeconds;

    public bool recentlyDamaged;
    public HitDirection hitDirection;

    public float roundTimeRemaining;
    public int playerWins;
    public int npcWins;
    public int draws;

    public int arenaIndex;
    public string arenaName;
    public float coverDensity;

    public float observedSeconds;
    public float meanEngagementDistance;
    public float shotsPer10Seconds;
    public float observedMeanSpeed;

    public NpcMode mode;
    public float timeInMode;
}

// Coarse range to the perceived target position. None is the never-seen fixed
// point: no sighting inside the perception horizon, nothing to report.
public enum DistanceBucket
{
    None = 0,
    Near = 1,
    Mid = 2,
    Far = 3,
}

// What the LLM mode selector is given when asked for a decision (issue #123).
// Values are bucketed and rounded HERE, not in the prompt formatter: the prompt
// labels have to be written against the same values the model sees.
public class GameStateSnapshot
{
    // Bucket edges. Near is knife range — the tooCloseDistance radius (6 m)
    // plus a couple of steps; Mid is the normal engagement band around
    // huntEngagementDistance (20 m); Far runs from there out to the 45 m
    // sight range.
    public const float NearMaxMetres = 10f;
    public const float MidMaxMetres = 25f;

    // Multiples of 5. Lossless in practice: shots deal 15, so HP off 100 only
    // ever lands on one.
    public int hpPercent;

    public bool targetVisible;
    // None when there is no sighting inside the horizon.
    public DistanceBucket targetDistance;
    // 0 while visible, the sighting's age while remembered, -1 when
    // targetDistance is None.
    public int secondsSinceSeen;

    public bool recentlyDamaged;
    // None when not recently damaged, or when the hit had no known shooter —
    // DamageMemory keeps the two from ever disagreeing.
    public HitDirection damagedFrom;

    public int roundSecondsRemaining;
    public int playerWins;
    public int npcWins;
    public int draws;

    public int arenaIndex;
    public string arenaName;
    // Fraction of the floor under cover footprint, two decimals.
    public float coverDensity;

    // How much watching backs the three stats below; 0 means they carry no
    // information. One decimal.
    public float observedSeconds;
    public int meanEngagementDistanceMetres;
    public float shotsHeardPer10Seconds;
    // Metres per second, one decimal.
    public float observedMeanSpeed;

    public NpcMode mode;
    public int secondsInMode;

    public static GameStateSnapshot Build(in GameStateInput input)
    {
        float maxHp = input.maxHealth > 0f ? input.maxHealth : 1f;
        // A remembered sighting counts only inside the horizon: PerceptionMemory
        // lapses on its own refresh, but a snapshot built before that refresh
        // has run must read never-seen all the same.
        bool known = input.targetVisible
            || (input.hasEverSeen && input.timeSinceSeen <= input.seenHorizonSeconds);

        return new GameStateSnapshot
        {
            hpPercent = RoundTo5(Mathf.Clamp01(input.health / maxHp) * 100f),

            targetVisible = input.targetVisible,
            targetDistance = known ? BucketOf(input.distanceToLastSeen) : DistanceBucket.None,
            secondsSinceSeen = !known ? -1
                : input.targetVisible ? 0
                : Mathf.RoundToInt(input.timeSinceSeen),

            recentlyDamaged = input.recentlyDamaged,
            damagedFrom = input.hitDirection,

            // Ceil, matching the HUD clock: 0 is reserved for "time is up".
            roundSecondsRemaining = Mathf.Max(0, Mathf.CeilToInt(input.roundTimeRemaining)),
            playerWins = input.playerWins,
            npcWins = input.npcWins,
            draws = input.draws,

            arenaIndex = input.arenaIndex,
            arenaName = input.arenaName,
            coverDensity = Round2(Mathf.Clamp01(input.coverDensity)),

            observedSeconds = Round1(input.observedSeconds),
            meanEngagementDistanceMetres = Mathf.RoundToInt(input.meanEngagementDistance),
            shotsHeardPer10Seconds = Round1(input.shotsPer10Seconds),
            observedMeanSpeed = Round1(input.observedMeanSpeed),

            mode = input.mode,
            secondsInMode = Mathf.Max(0, Mathf.RoundToInt(input.timeInMode)),
        };
    }

    static DistanceBucket BucketOf(float metres)
    {
        if (metres <= NearMaxMetres) return DistanceBucket.Near;
        return metres <= MidMaxMetres ? DistanceBucket.Mid : DistanceBucket.Far;
    }

    static int RoundTo5(float value) => Mathf.RoundToInt(value / 5f) * 5;

    static float Round1(float value) => Mathf.Round(value * 10f) / 10f;

    static float Round2(float value) => Mathf.Round(value * 100f) / 100f;
}
