using NUnit.Framework;
using UnityEngine;

// The frozen observation layout (#43), slot by slot. Every index is pinned here
// because reordering or re-meaning one invalidates every trained model, and a
// silent drift would only show up as a policy that mysteriously stops working.
public class NpcObservationsTests
{
    const float Tolerance = 1e-5f;

    // A combatant at the origin facing +Z that has never seen anything.
    static NpcObservationInput Blind() => new NpcObservationInput
    {
        canAttack = true,
        cooldownRemaining01 = 0f,
        targetVisible = false,
        hasEverSeen = false,
        lastSeenPosition = Vector3.zero,
        timeSinceSeen = Mathf.Infinity,
        selfPosition = Vector3.zero,
        selfForward = Vector3.forward,
        sightRange = 20f,
        normalizedHealth = 1f,
        normalizedSpeed = 0f,
        recentlyDamaged = false,
        hitDirection = HitDirection.None,
        mode = NpcMode.Hunt,
    };

    static float[] Fill(NpcObservationInput input)
    {
        var buffer = new float[NpcBrainSpec.ObservationSize];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = float.NaN;
        NpcObservations.Fill(buffer, input);
        return buffer;
    }

    [Test]
    public void EverySlot_IsWritten()
    {
        float[] obs = Fill(Blind());
        for (int i = 0; i < obs.Length; i++)
        {
            Assert.That(float.IsNaN(obs[i]), Is.False, $"observation {i} was left unwritten");
        }
    }

    [Test]
    public void WrongSizedBuffer_IsRejected()
    {
        // The agent sizes its buffer off the same constant the prefab declares;
        // a mismatch must not silently truncate the vector.
        Assert.Throws<System.ArgumentException>(
            () => NpcObservations.Fill(new float[NpcBrainSpec.ObservationSize - 1], Blind()));
        Assert.Throws<System.ArgumentException>(() => NpcObservations.Fill(null, Blind()));
    }

    [Test]
    public void ModeOneHot_FitsExactlyTheSlotsReservedForIt()
    {
        // A fifth NpcMode needs a wider vector, not an overflow into index 17.
        Assert.That(System.Enum.GetValues(typeof(NpcMode)).Length, Is.EqualTo(NpcObservations.ModeOneHotSlots));
        Assert.That(NpcObservations.ModeOneHotStart + NpcObservations.ModeOneHotSlots,
            Is.EqualTo(NpcBrainSpec.ObservationSize - 1), "index 17 is the speed slot");
    }

    [Test]
    public void NeverSeen_ReportsTheDocumentedDefaults()
    {
        float[] obs = Fill(Blind());

        Assert.That(obs[2], Is.EqualTo(0f), "not visible");
        Assert.That(obs[3], Is.EqualTo(1f), "unknown position reads as maximum distance");
        Assert.That(obs[4], Is.EqualTo(0f), "no bearing");
        Assert.That(obs[5], Is.EqualTo(0f), "no bearing");
        Assert.That(obs[6], Is.EqualTo(1f), "an infinite time-since-seen saturates rather than blowing up");
    }

    [Test]
    public void Cooldown_AndCanAttack_LandInTheFirstTwoSlots()
    {
        NpcObservationInput input = Blind();
        input.canAttack = false;
        input.cooldownRemaining01 = 0.4f;
        float[] obs = Fill(input);

        Assert.That(obs[0], Is.EqualTo(0f));
        Assert.That(obs[1], Is.EqualTo(0.4f).Within(Tolerance));
    }

    [Test]
    public void TargetStraightAhead_IsCosOneSinZero()
    {
        NpcObservationInput input = Blind();
        input.hasEverSeen = true;
        input.targetVisible = true;
        input.timeSinceSeen = 0f;
        input.lastSeenPosition = new Vector3(0f, 0f, 10f);
        float[] obs = Fill(input);

        Assert.That(obs[2], Is.EqualTo(1f));
        Assert.That(obs[3], Is.EqualTo(0.5f).Within(Tolerance), "10 m of a 20 m sight range");
        Assert.That(obs[4], Is.EqualTo(0f).Within(Tolerance), "sin");
        Assert.That(obs[5], Is.EqualTo(1f).Within(Tolerance), "cos");
        Assert.That(obs[6], Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void BearingSin_IsPositiveToTheRight()
    {
        NpcObservationInput input = Blind();
        input.hasEverSeen = true;

        input.lastSeenPosition = new Vector3(10f, 0f, 0f);
        float[] right = Fill(input);
        Assert.That(right[4], Is.EqualTo(1f).Within(Tolerance));
        Assert.That(right[5], Is.EqualTo(0f).Within(Tolerance));

        input.lastSeenPosition = new Vector3(-10f, 0f, 0f);
        float[] left = Fill(input);
        Assert.That(left[4], Is.EqualTo(-1f).Within(Tolerance));
        Assert.That(left[5], Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void Bearing_IsRelativeToOwnForward_AndIgnoresHeight()
    {
        NpcObservationInput input = Blind();
        input.hasEverSeen = true;
        input.selfForward = Vector3.right;                      // turned 90° right
        input.lastSeenPosition = new Vector3(0f, 25f, 10f);     // straight ahead of +Z, high up
        float[] obs = Fill(input);

        Assert.That(obs[4], Is.EqualTo(-1f).Within(Tolerance), "the target is now off the left shoulder");
        Assert.That(obs[5], Is.EqualTo(0f).Within(Tolerance));
        Assert.That(obs[3], Is.EqualTo(0.5f).Within(Tolerance), "distance is measured on the ground plane");
    }

    [Test]
    public void DistanceAndStaleness_SaturateAtOne()
    {
        NpcObservationInput input = Blind();
        input.hasEverSeen = true;
        input.lastSeenPosition = new Vector3(0f, 0f, 500f);
        input.timeSinceSeen = NpcObservations.SeenHorizonSeconds * 4f;
        float[] obs = Fill(input);

        Assert.That(obs[3], Is.EqualTo(1f));
        Assert.That(obs[6], Is.EqualTo(1f));
    }

    [Test]
    public void StandingOnTheLastSeenPosition_HasNoBearing()
    {
        NpcObservationInput input = Blind();
        input.hasEverSeen = true;
        input.lastSeenPosition = Vector3.zero; // same as selfPosition
        float[] obs = Fill(input);

        Assert.That(obs[3], Is.EqualTo(0f));
        Assert.That(obs[4], Is.EqualTo(0f));
        Assert.That(obs[5], Is.EqualTo(0f));
    }

    [Test]
    public void HealthAndSpeed_AreClampedIntoTheUnitRange()
    {
        NpcObservationInput input = Blind();
        input.normalizedHealth = 1.4f;
        input.normalizedSpeed = -0.3f;
        float[] obs = Fill(input);

        Assert.That(obs[7], Is.EqualTo(1f));
        Assert.That(obs[17], Is.EqualTo(0f));
    }

    [Test]
    public void NaNInputs_ReadAsZeroRatherThanPoisoningTheVector()
    {
        // ML-Agents' own NaN check fires deep inside the sensor, where the
        // culprit (a zero maxHealth, say) is no longer identifiable.
        NpcObservationInput input = Blind();
        input.normalizedHealth = float.NaN;
        Assert.That(Fill(input)[7], Is.EqualTo(0f));
    }

    [Test]
    [TestCase(HitDirection.Front, 9)]
    [TestCase(HitDirection.Back, 10)]
    [TestCase(HitDirection.Left, 11)]
    [TestCase(HitDirection.Right, 12)]
    public void HitDirection_SetsExactlyItsOwnSlot(HitDirection direction, int slot)
    {
        NpcObservationInput input = Blind();
        input.recentlyDamaged = true;
        input.hitDirection = direction;
        float[] obs = Fill(input);

        Assert.That(obs[8], Is.EqualTo(1f), "the recently-damaged flag");
        for (int i = 9; i <= 12; i++)
        {
            Assert.That(obs[i], Is.EqualTo(i == slot ? 1f : 0f), $"slot {i}");
        }
    }

    [Test]
    public void UnknownShooter_LeavesTheHitDirectionOneHotEmpty()
    {
        // DamageMemory reports None both for a lapsed memory and for damage with
        // no known source; the flag still distinguishes the two.
        NpcObservationInput input = Blind();
        input.recentlyDamaged = true;
        input.hitDirection = HitDirection.None;
        float[] obs = Fill(input);

        Assert.That(obs[8], Is.EqualTo(1f));
        for (int i = 9; i <= 12; i++) Assert.That(obs[i], Is.EqualTo(0f), $"slot {i}");
    }

    [Test]
    [TestCase(NpcMode.Hunt, 13)]
    [TestCase(NpcMode.HoldCover, 14)]
    [TestCase(NpcMode.Retreat, 15)]
    [TestCase(NpcMode.Patrol, 16)]
    public void CommandedMode_OneHotFollowsTheDeclarationOrder(NpcMode mode, int slot)
    {
        NpcObservationInput input = Blind();
        input.mode = mode;
        float[] obs = Fill(input);

        for (int i = 13; i <= 16; i++)
        {
            Assert.That(obs[i], Is.EqualTo(i == slot ? 1f : 0f), $"slot {i}");
        }
    }
}
