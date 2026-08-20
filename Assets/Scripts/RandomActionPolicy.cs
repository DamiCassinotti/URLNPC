// The random control policy (eval.sh --subject random, #97): uniform draws over
// the frozen action space, ignoring the world entirely. Pure logic with the draw
// injected — EnemyAgent.Heuristic is the adapter and passes RunRng's own stream.
public static class RandomActionPolicy
{
    // draw(minInclusive, maxExclusive), like RunRng.Range and Random.Range.
    // One draw per branch every step whatever the world looks like, so the
    // stream stays in step across runs at the same seed.
    public static void Draw(System.Func<int, int, int> draw, out int movement, out int fire)
    {
        movement = draw(0, NpcBrainSpec.MovementBranchSize);
        fire = draw(0, NpcBrainSpec.FireBranchSize);
    }
}
