// The scripted retreating control (eval.sh --subject flee, #105): a body that
// really does back off and get something solid between itself and the target.
// Retreat's compliance rule had only low-scoring controls, so a low rate could
// not be told apart from a rule nothing can satisfy. Pure logic, like
// RandomActionPolicy — EnemyAgent.Heuristic is the adapter.
public static class ScriptedRetreatPolicy
{
    public static void Choose(bool hasEverSeen, bool targetVisible, out int movement, out int fire)
    {
        // Never fires: trading shots is the one thing a retreat isn't doing,
        // and it would score on Hunt's rule as well.
        fire = NpcBrainSpec.DontFire;
        // Before the first sighting there is no bearing to back away along, and
        // the movement primitives fall through to Wander anyway.
        if (!hasEverSeen)
        {
            movement = (int)MovementAction.Wander;
            return;
        }
        // In sight: put ground between them. Out of sight but known about:
        // break the eye-line and stay behind it.
        movement = (int)(targetVisible ? MovementAction.Retreat : MovementAction.MoveToCover);
    }
}
