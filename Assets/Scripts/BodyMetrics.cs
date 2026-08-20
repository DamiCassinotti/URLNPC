// The two combatant bodies don't share an origin convention. Enemy.prefab's
// transform sits at its capsule centre and is lifted onto the mesh by a 1 m
// NavMeshAgent base offset; CombatantRig's player body has its origin at the
// feet with no offset. Anything measured off transform.position has to take the
// offset out first, or the same expression lands a metre apart on the two sides
// — which is how the agent-driven player ended up firing from ankle height and
// probing the enemy's eye-line a metre above its head (#103).
public static class BodyMetrics
{
    // Where a body's shots leave it: mid-height, so the muzzle sits inside the
    // torso rather than at whatever the transform origin happens to be.
    public static float MuzzleOffset(float height, float baseOffset)
    {
        return height * 0.5f - baseOffset;
    }

    // Where a body looks from, and the head cover has to hide.
    public static float EyeOffset(float height, float baseOffset)
    {
        return height - baseOffset;
    }
}
