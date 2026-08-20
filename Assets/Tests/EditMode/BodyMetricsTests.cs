using NUnit.Framework;

// The origin conventions the two combatant bodies actually ship with (#103):
// the Enemy prefab is a 2 m capsule centred on a transform lifted 1 m onto the
// mesh, CombatantRig's player body is a 2 m capsule with its origin at the feet.
// Both offsets have to land on the same world height.
public class BodyMetricsTests
{
    const float Height = 2f;
    const float LiftedOrigin = 1f;
    const float FeetOrigin = 0f;

    [Test]
    public void MuzzleOffset_PutsBothBodiesAtTheSameWorldHeight()
    {
        float lifted = LiftedOrigin + BodyMetrics.MuzzleOffset(Height, LiftedOrigin);
        float feet = FeetOrigin + BodyMetrics.MuzzleOffset(Height, FeetOrigin);

        Assert.That(lifted, Is.EqualTo(feet).Within(1e-4f));
        Assert.That(lifted, Is.EqualTo(Height * 0.5f).Within(1e-4f), "mid-body above the ground");
    }

    [Test]
    public void MuzzleOffset_IsZeroOnTheEnemyPrefab()
    {
        // The NPC's muzzle must not move: its combat numbers were trained with
        // the transform origin doubling as the muzzle.
        Assert.That(BodyMetrics.MuzzleOffset(Height, LiftedOrigin), Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void EyeOffset_PutsBothBodiesAtTheSameWorldHeight()
    {
        float lifted = LiftedOrigin + BodyMetrics.EyeOffset(Height, LiftedOrigin);
        float feet = FeetOrigin + BodyMetrics.EyeOffset(Height, FeetOrigin);

        Assert.That(lifted, Is.EqualTo(feet).Within(1e-4f));
        Assert.That(lifted, Is.EqualTo(Height).Within(1e-4f), "the head above the ground");
    }

    [Test]
    public void NoOffset_LeavesTheHeightsAsGiven()
    {
        Assert.That(BodyMetrics.MuzzleOffset(1.8f, 0f), Is.EqualTo(0.9f).Within(1e-4f));
        Assert.That(BodyMetrics.EyeOffset(1.8f, 0f), Is.EqualTo(1.8f).Within(1e-4f));
    }
}
