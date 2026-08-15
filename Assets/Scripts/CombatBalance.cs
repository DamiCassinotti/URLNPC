// The numbers that decide a fight, owned by code because the binary FPS scene
// can't be trusted with them (issue #71). The Enemy is a prefab instance whose
// serialized overrides pin attackCooldown at 1 s and the weapon damage at 12,
// and no edit to Enemy.prefab reaches a prefab-instance override; the player's
// weapon is authored straight into the scene, where nothing reaches it at all.
// Weapon/EnemyWeapon/EnemyBehavior force these onto whatever the scene
// deserialized, the way EnemyAgent forces MaxStep.
//
// Both sides read the same values, so a self-play run fights symmetric weapons:
// CombatantRig composes the agent-side player from the script defaults, which
// used to leave it with 50 damage and a 0.5 s cooldown against the scene
// enemy's 12 and 1 s.
public static class CombatBalance
{
    public const float MaxHealth = 100f;

    // Shots a full-health combatant survives: 100 / 15 kills on the 7th.
    public const float ShotDamage = 15f;

    // Seconds between NPC shots. Two per second at 100% uptime, so a clean
    // burst kills in ~3.5 s.
    public const float AttackCooldown = 0.5f;

    // Max angular error per NPC shot. At the old 5 degrees the cone was wider
    // than a player capsule past ~10 m, so most shots missed on purpose.
    public const float AimSpreadDegrees = 1.5f;

    // How far a combatant can see (issue #73). The arenas run from 28x28 up to
    // 60x60 m and the enemy spawns at least 25 m from the player — at the old
    // 20 m it started every round blind and only woke up at knife range. 45 m
    // covers the widest spawn separation any layout can produce
    // (ArenaManager.SpawnSeparationCap peaks at 42 m on Twin Towers), so LOS
    // and the 120 degree FOV are what gate a sighting, not the range cutoff.
    // Also the scale the perceived-distance observation normalizes by, so
    // changing it invalidates trained models.
    public const float SightRange = 45f;

    // How long after losing sight the NPC may still shoot at the last-seen
    // position (issue #72). Two shots' worth of suppressing the corner someone
    // just ducked behind; past that it has to go and look instead of standing
    // and firing at a memory. A shot inside the window is still a shot out of
    // sight, so it keeps paying the wasted-shot penalty.
    public const float FiringGraceSeconds = 1f;
}
