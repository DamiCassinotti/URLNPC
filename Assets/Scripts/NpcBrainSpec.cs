using Unity.MLAgents.Actuators;

// The frozen brain interface (#43): the observation width and the two discrete
// action branches. The Enemy prefab declares the same shape in its serialized
// BehaviorParameters and CombatantRig composes it at runtime for the agent-driven
// player — this is the single copy both are held to, and changing any of it
// invalidates every trained model.
public static class NpcBrainSpec
{
    public const int ObservationSize = 18;

    // Branch 0 picks a MovementAction, branch 1 decides whether to pull the
    // trigger. Splitting them lets the policy move and shoot in the same step.
    public const int MovementBranchSize = MovementActions.Count;
    public const int FireBranchSize = 2;

    public const int DontFire = 0;
    public const int Fire = 1;

    public static ActionSpec Actions => ActionSpec.MakeDiscrete(MovementBranchSize, FireBranchSize);
}
