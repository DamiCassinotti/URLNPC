// The movement primitives the policy's movement branch selects from
// (EnemyBehavior.Move). Declaration order is the action index order; changing
// it invalidates every trained model.
public enum MovementAction
{
    Hold = 0,
    Advance = 1,
    Retreat = 2,
    StrafeLeft = 3,
    StrafeRight = 4,
    MoveToCover = 5,
    Wander = 6,
}

public static class MovementActions
{
    public const int Count = 7;
}
