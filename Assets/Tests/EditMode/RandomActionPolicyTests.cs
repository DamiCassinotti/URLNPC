using System.Collections.Generic;
using NUnit.Framework;

// The random control (#97): the floor an evaluated model has to clear. It only
// has to stay inside the frozen action space and draw once per branch — a draw
// off the end of a branch is an out-of-range action, and a shared range across
// the two would skew the movement mix towards the first two primitives.
public class RandomActionPolicyTests
{
    [Test]
    public void EachBranchIsDrawnOverItsOwnRange()
    {
        var bounds = new List<(int min, int max)>();

        RandomActionPolicy.Draw((min, max) =>
        {
            bounds.Add((min, max));
            return min;
        }, out _, out _);

        Assert.That(bounds, Is.EqualTo(new[]
        {
            (0, NpcBrainSpec.MovementBranchSize),
            (0, NpcBrainSpec.FireBranchSize),
        }), "one draw per branch, movement first — the order EnemyAgent writes them in");
    }

    // The draw is half-open, so both extremes of every branch are legal
    // actions; neither may fall outside it.
    [TestCase(true)]
    [TestCase(false)]
    public void BothExtremesOfEveryBranchAreInRange(bool takeTop)
    {
        RandomActionPolicy.Draw((min, max) => takeTop ? max - 1 : min,
            out int movement, out int fire);

        Assert.That(movement, Is.InRange(0, NpcBrainSpec.MovementBranchSize - 1));
        Assert.That(fire, Is.InRange(0, NpcBrainSpec.FireBranchSize - 1));
        Assert.That(System.Enum.IsDefined(typeof(MovementAction), (MovementAction)movement), Is.True,
            "branch 0 is a MovementAction index, so every draw has to name a real primitive");
    }
}
