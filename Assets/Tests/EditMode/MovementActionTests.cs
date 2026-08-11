using NUnit.Framework;

// The movement branch's action indices. These are baked into every trained
// model, so reordering the enum silently remaps a policy's behavior.
public class MovementActionTests
{
    [Test]
    public void ActionIndicesAreFrozen()
    {
        Assert.That((int)MovementAction.Hold, Is.EqualTo(0));
        Assert.That((int)MovementAction.Advance, Is.EqualTo(1));
        Assert.That((int)MovementAction.Retreat, Is.EqualTo(2));
        Assert.That((int)MovementAction.StrafeLeft, Is.EqualTo(3));
        Assert.That((int)MovementAction.StrafeRight, Is.EqualTo(4));
        Assert.That((int)MovementAction.MoveToCover, Is.EqualTo(5));
        Assert.That((int)MovementAction.Wander, Is.EqualTo(6));
    }

    [Test]
    public void CountMatchesTheEnum()
    {
        Assert.That(System.Enum.GetValues(typeof(MovementAction)).Length, Is.EqualTo(MovementActions.Count));
    }
}
