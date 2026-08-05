using NUnit.Framework;

/// <summary>
/// The round countdown as pure state: clamping, expiry semantics, re-arming,
/// and the "duration <= 0 disables the clock" contract that GameManager and
/// the training loop rely on.
/// </summary>
public class RoundClockTests
{
    [Test]
    public void Countdown_DecrementsAndClampsAtZero()
    {
        var clock = new RoundClock { Duration = 10f };
        clock.Reset();

        Assert.That(clock.Tick(3f), Is.False);
        Assert.That(clock.Remaining, Is.EqualTo(7f));

        Assert.That(clock.Tick(100f), Is.True, "overshooting the remaining time must expire the clock");
        Assert.That(clock.Remaining, Is.Zero, "remaining time never goes negative");
    }

    [Test]
    public void Expiry_FiresOnTheTickThatReachesZero()
    {
        var clock = new RoundClock { Duration = 1f };
        clock.Reset();

        Assert.That(clock.Tick(0.5f), Is.False);
        Assert.That(clock.Tick(0.5f), Is.True, "exactly reaching zero counts as expired");
    }

    [Test]
    public void Expired_StaysExpiredUntilReset()
    {
        var clock = new RoundClock { Duration = 1f };
        clock.Reset();
        clock.Tick(2f);

        // GameManager reacts every frame until the round ends or the clock is
        // re-armed (training), so expiry must keep reporting.
        Assert.That(clock.Tick(0.1f), Is.True);
        Assert.That(clock.Tick(0.1f), Is.True);

        clock.Reset();
        Assert.That(clock.Remaining, Is.EqualTo(1f));
        Assert.That(clock.Tick(0.1f), Is.False, "a re-armed clock runs a fresh round");
    }

    [Test]
    public void ZeroDuration_DisablesTheClock()
    {
        var clock = new RoundClock { Duration = 0f };
        clock.Reset();

        Assert.That(clock.Enabled, Is.False);
        Assert.That(clock.Tick(100f), Is.False, "a disabled clock never expires");
        Assert.That(clock.Remaining, Is.Zero);
    }

    [Test]
    public void NegativeDuration_DisablesTheClock()
    {
        var clock = new RoundClock { Duration = -5f };
        clock.Reset();

        Assert.That(clock.Enabled, Is.False);
        Assert.That(clock.Tick(100f), Is.False);
    }
}
