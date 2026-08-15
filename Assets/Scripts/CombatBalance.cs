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
}
