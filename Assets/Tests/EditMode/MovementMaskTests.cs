using NUnit.Framework;

// The per-mode movement mask (#113). The table is what the policy is trained
// inside, so a silent edit remaps every trained model's behavior.
public class MovementMaskTests
{
    [Test]
    public void HuntBlocksBackingOffAndHiding()
    {
        Assert.That(MovementMask.IsAllowed(NpcMode.Hunt, MovementAction.Retreat), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Hunt, MovementAction.MoveToCover), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Hunt, MovementAction.Advance), Is.True);
        Assert.That(MovementMask.IsAllowed(NpcMode.Hunt, MovementAction.Hold), Is.True);
    }

    [Test]
    public void HoldCoverBlocksClosingAndRoaming()
    {
        Assert.That(MovementMask.IsAllowed(NpcMode.HoldCover, MovementAction.Advance), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.HoldCover, MovementAction.Retreat), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.HoldCover, MovementAction.Wander), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.HoldCover, MovementAction.MoveToCover), Is.True);
        Assert.That(MovementMask.IsAllowed(NpcMode.HoldCover, MovementAction.Hold), Is.True);
    }

    [Test]
    public void RetreatBlocksClosingAndStandingStill()
    {
        Assert.That(MovementMask.IsAllowed(NpcMode.Retreat, MovementAction.Advance), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Retreat, MovementAction.Hold), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Retreat, MovementAction.Retreat), Is.True);
        Assert.That(MovementMask.IsAllowed(NpcMode.Retreat, MovementAction.MoveToCover), Is.True);
    }

    [Test]
    public void PatrolBlocksEngagingAndHiding()
    {
        Assert.That(MovementMask.IsAllowed(NpcMode.Patrol, MovementAction.Advance), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Patrol, MovementAction.Retreat), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Patrol, MovementAction.MoveToCover), Is.False);
        Assert.That(MovementMask.IsAllowed(NpcMode.Patrol, MovementAction.Wander), Is.True);
    }

    // A fully masked branch throws inside ML-Agents, and a mode with one primitive
    // left has no choice to learn.
    [Test]
    public void EveryModeKeepsSeveralPrimitives()
    {
        foreach (NpcMode mode in NpcModes.All)
        {
            int allowed = 0;
            foreach (MovementAction action in System.Enum.GetValues(typeof(MovementAction)))
            {
                if (MovementMask.IsAllowed(mode, action)) allowed++;
            }
            Assert.That(allowed, Is.GreaterThan(1), $"{mode} has {allowed} primitive(s) left");
        }
    }

    [Test]
    public void EveryPrimitiveIsReachableUnderSomeMode()
    {
        foreach (MovementAction action in System.Enum.GetValues(typeof(MovementAction)))
        {
            bool reachable = false;
            foreach (NpcMode mode in NpcModes.All) reachable |= MovementMask.IsAllowed(mode, action);
            Assert.That(reachable, Is.True, $"{action} is masked under every mode");
        }
    }

    [Test]
    public void BlockedActionsAreWithinTheMovementBranch()
    {
        foreach (NpcMode mode in NpcModes.All)
        {
            foreach (MovementAction action in MovementMask.BlockedFor(mode))
            {
                Assert.That((int)action, Is.InRange(0, NpcBrainSpec.MovementBranchSize - 1));
            }
        }
    }

    // A stale serialized mode in the binary FPS scene must not index off the end.
    [Test]
    public void UndefinedModeMasksNothing()
    {
        Assert.That(MovementMask.BlockedFor((NpcMode)99), Is.Empty);
        Assert.That(MovementMask.IsAllowed((NpcMode)99, MovementAction.Advance), Is.True);
    }
}
