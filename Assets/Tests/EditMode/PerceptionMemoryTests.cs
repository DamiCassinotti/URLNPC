using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Never-seen invariants of the sensory contract: a fresh (or wiped) memory
/// must report nothing about the player. The full sighting/freezing behavior
/// needs real geometry and runs in PlayMode (PerceptionContractTests).
/// (EditMode: Awake never runs, so the EnemyBehavior lookup stays null and
/// Refresh must degrade to "not visible" rather than throw.)
/// </summary>
public class PerceptionMemoryTests
{
    GameObject go;
    PerceptionMemory memory;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("PerceptionTest");
        memory = go.AddComponent<PerceptionMemory>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    [Test]
    public void FreshMemory_HasNeverSeenAnything()
    {
        Assert.That(memory.HasEverSeen, Is.False);
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.TimeSinceSeen, Is.EqualTo(Mathf.Infinity));
        Assert.That(memory.LastSeenPosition, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Refresh_WithoutBehavior_ReportsNotVisibleInsteadOfThrowing()
    {
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.HasEverSeen, Is.False);
    }

    [Test]
    public void Forget_ResetsToNeverSeenState()
    {
        memory.Forget();
        Assert.That(memory.HasEverSeen, Is.False);
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.TimeSinceSeen, Is.EqualTo(Mathf.Infinity));
        Assert.That(memory.LastSeenPosition, Is.EqualTo(Vector3.zero));
    }
}
