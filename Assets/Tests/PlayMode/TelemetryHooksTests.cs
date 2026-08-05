using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The two static hooks TelemetryLogger listens on (issue #12): ordering and
// coverage, independent of what the logger does with them. A shot must be
// announced before the death it causes, and a timeout must be reported —
// otherwise the killing shot lands in the next episode and timed-out rounds
// get no summary at all.
public class TelemetryHooksTests : PlayModeTestBase
{
    readonly List<string> events = new List<string>();

    [TearDown]
    public void ClearEvents() => events.Clear();

    [UnityTest]
    public IEnumerator LethalShot_IsAnnouncedBeforeTheDeathItCauses()
    {
        Health target = CreateCombatant("NPC", new Vector3(0f, 0f, 10f), 10f);
        Weapon weapon = CreateWeapon("Player", Vector3.zero);
        yield return null;

        void OnShot(Weapon w, Health v, float d) => events.Add(v != null ? "shot:hit" : "shot:miss");
        void OnDied() => events.Add("died");
        Weapon.ShotFired += OnShot;
        target.OnDied += OnDied;
        try
        {
            weapon.Shoot();
        }
        finally
        {
            Weapon.ShotFired -= OnShot;
        }

        Assert.That(events, Is.EqualTo(new[] { "shot:hit", "died" }),
            "the killing shot must be reported before the death cascade closes the episode");
    }

    [UnityTest]
    public IEnumerator Miss_IsStillAnnounced()
    {
        Weapon weapon = CreateWeapon("Player", Vector3.zero);
        yield return null;

        void OnShot(Weapon w, Health v, float d) => events.Add(v != null ? "shot:hit" : "shot:miss");
        Weapon.ShotFired += OnShot;
        try
        {
            weapon.Shoot();
        }
        finally
        {
            Weapon.ShotFired -= OnShot;
        }

        Assert.That(events, Is.EqualTo(new[] { "shot:miss" }));
    }

    [UnityTest]
    public IEnumerator Timeout_ReportsRoundEndedAsDraw()
    {
        CreateCounterHud();
        CreateGameManager(0.3f);

        void OnRoundEnded(string winner) => events.Add(winner);
        GameManager.RoundEnded += OnRoundEnded;
        try
        {
            float safety = 5f;
            while (Time.timeScale > 0f && safety > 0f)
            {
                safety -= Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally
        {
            GameManager.RoundEnded -= OnRoundEnded;
        }

        Assert.That(events, Is.EqualTo(new[] { GameManager.DrawResult }),
            "a timed-out round must be reported exactly once, as a draw");
    }

    [UnityTest]
    public IEnumerator Death_ReportsRoundEndedWithTheWinner()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(60f);
        yield return null;

        void OnRoundEnded(string winner) => events.Add(winner);
        GameManager.RoundEnded += OnRoundEnded;
        try
        {
            gm.ProcessDeath("NPC");
        }
        finally
        {
            GameManager.RoundEnded -= OnRoundEnded;
        }

        Assert.That(events, Is.EqualTo(new[] { "Player" }));
    }
}
