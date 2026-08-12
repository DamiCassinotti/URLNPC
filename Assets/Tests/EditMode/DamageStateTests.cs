using NUnit.Framework;
using UnityEngine;

// The decay/bucketing rules behind DamageMemory. Time is injected, so the whole
// lapse boundary is testable without waiting on a frame.
public class DamageStateTests
{
    DamageState state;

    [SetUp]
    public void SetUp()
    {
        state = new DamageState { memorySeconds = 2f };
    }

    [Test]
    public void FreshState_RemembersNothing()
    {
        Assert.That(state.RecentlyDamaged(0f), Is.False);
        Assert.That(state.LastHitDirection(0f), Is.EqualTo(HitDirection.None));
        Assert.That(state.TimeSinceDamaged(0f), Is.EqualTo(Mathf.Infinity));
    }

    [Test]
    public void Record_RemembersUntilTheMemoryLapses()
    {
        state.Record(HitDirection.Left, 10f);

        Assert.That(state.RecentlyDamaged(10f), Is.True);
        Assert.That(state.LastHitDirection(11.9f), Is.EqualTo(HitDirection.Left));
        Assert.That(state.TimeSinceDamaged(11f), Is.EqualTo(1f));

        Assert.That(state.RecentlyDamaged(12f), Is.False, "memorySeconds is exclusive at the boundary");
        Assert.That(state.LastHitDirection(12f), Is.EqualTo(HitDirection.None),
            "a lapsed memory must not keep reporting a direction");
        Assert.That(state.TimeSinceDamaged(12f), Is.EqualTo(2f), "time since the hit keeps running past the lapse");
    }

    [Test]
    public void SecondHit_ReplacesDirectionAndRestartsTheTimer()
    {
        state.Record(HitDirection.Left, 10f);
        state.Record(HitDirection.Right, 11.5f);

        Assert.That(state.LastHitDirection(13f), Is.EqualTo(HitDirection.Right));
        Assert.That(state.RecentlyDamaged(13f), Is.True, "the first hit alone would have lapsed by now");
    }

    [Test]
    public void Forget_WipesTheMemory()
    {
        state.Record(HitDirection.Front, 10f);
        state.Forget();

        Assert.That(state.RecentlyDamaged(10f), Is.False);
        Assert.That(state.LastHitDirection(10f), Is.EqualTo(HitDirection.None));
        Assert.That(state.TimeSinceDamaged(10f), Is.EqualTo(Mathf.Infinity));
    }

    [TestCase(0f, HitDirection.Front)]
    [TestCase(44f, HitDirection.Front)]
    [TestCase(-44f, HitDirection.Front)]
    [TestCase(45f, HitDirection.Front)]
    [TestCase(-45f, HitDirection.Front)]
    [TestCase(46f, HitDirection.Right)]
    [TestCase(90f, HitDirection.Right)]
    [TestCase(135f, HitDirection.Right)]
    [TestCase(136f, HitDirection.Back)]
    [TestCase(180f, HitDirection.Back)]
    [TestCase(-136f, HitDirection.Back)]
    [TestCase(-135f, HitDirection.Left)]
    [TestCase(-90f, HitDirection.Left)]
    [TestCase(-46f, HitDirection.Left)]
    public void Bucket_SplitsTheCircleIntoFourQuarters(float bearingDegrees, HitDirection expected)
    {
        Vector3 toSource = Quaternion.Euler(0f, bearingDegrees, 0f) * Vector3.forward;

        Assert.That(DamageState.Bucket(toSource, Vector3.forward), Is.EqualTo(expected));
    }

    [Test]
    public void Bucket_IsRelativeToOwnForward_NotWorldAxes()
    {
        // Facing +X, shot from +Z: world "north", but the victim's left.
        Assert.That(DamageState.Bucket(Vector3.forward, Vector3.right), Is.EqualTo(HitDirection.Left));
    }

    [Test]
    public void Bucket_IgnoresHeight()
    {
        Vector3 fromBehindAndAbove = new Vector3(0f, 20f, -5f);

        Assert.That(DamageState.Bucket(fromBehindAndAbove, Vector3.forward), Is.EqualTo(HitDirection.Back));
    }

    [Test]
    public void Bucket_WithNoHorizontalBearing_HasNoQuarter()
    {
        Assert.That(DamageState.Bucket(Vector3.up * 5f, Vector3.forward), Is.EqualTo(HitDirection.None),
            "a shooter straight overhead is in no quarter");
        Assert.That(DamageState.Bucket(Vector3.zero, Vector3.forward), Is.EqualTo(HitDirection.None));
        Assert.That(DamageState.Bucket(Vector3.forward, Vector3.up), Is.EqualTo(HitDirection.None),
            "a body with no flat facing has no quarters to sort into");
    }
}
