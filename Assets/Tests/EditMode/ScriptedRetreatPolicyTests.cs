using NUnit.Framework;

// The scripted retreating control (#105): Retreat's and HoldCover's positive
// control, so a low rate on those rules can be read as the policy rather than
// as a rule nothing satisfies.
public class ScriptedRetreatPolicyTests
{
    [Test]
    public void ItNeverFires()
    {
        foreach (bool seen in new[] { true, false })
        {
            foreach (bool visible in new[] { true, false })
            {
                ScriptedRetreatPolicy.Choose(seen, visible, out _, out int fire);
                Assert.That(fire, Is.EqualTo(NpcBrainSpec.DontFire), $"seen {seen}, visible {visible}");
            }
        }
    }

    [Test]
    public void InSightItBacksOff_OutOfSightItTakesCover()
    {
        ScriptedRetreatPolicy.Choose(true, true, out int visible, out _);
        ScriptedRetreatPolicy.Choose(true, false, out int lost, out _);

        Assert.That((MovementAction)visible, Is.EqualTo(MovementAction.Retreat));
        Assert.That((MovementAction)lost, Is.EqualTo(MovementAction.MoveToCover));
    }

    [Test]
    public void BeforeTheFirstSightingItSearches()
    {
        // There is no bearing to back away along yet, and Retreat falls through
        // to Wander anyway.
        ScriptedRetreatPolicy.Choose(false, false, out int movement, out _);
        Assert.That((MovementAction)movement, Is.EqualTo(MovementAction.Wander));
    }

    [Test]
    public void EveryChoiceIsInsideTheFrozenActionSpace()
    {
        foreach (bool seen in new[] { true, false })
        {
            foreach (bool visible in new[] { true, false })
            {
                ScriptedRetreatPolicy.Choose(seen, visible, out int movement, out int fire);
                Assert.That(movement, Is.InRange(0, NpcBrainSpec.MovementBranchSize - 1));
                Assert.That(fire, Is.InRange(0, NpcBrainSpec.FireBranchSize - 1));
            }
        }
    }
}
