using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The logger end to end: real weapons, real Health, real GameManager, with
// the session file swapped for an in-memory writer so the emitted JSONL can
// be read back. Covers what the POCO tests cannot — that the Unity events
// are wired up and land in the right episode.
public class TelemetryLoggerTests : PlayModeTestBase
{
    TelemetryLogger logger;
    StringWriter captured;
    TextWriter sessionWriter;

    [SetUp]
    public void CaptureTelemetry()
    {
        logger = TelemetryLogger.Instance;
        if (logger == null) Assert.Ignore("no TelemetryLogger bootstrapped in this run");
        captured = new StringWriter();
        sessionWriter = logger.SwapWriter(captured);
    }

    [TearDown]
    public void ReleaseTelemetry()
    {
        if (logger != null) logger.SwapWriter(sessionWriter);
        captured?.Dispose();
    }

    string[] Lines()
    {
        return captured.ToString().Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }

    string[] Types() => Lines().Select(TypeOf).ToArray();

    static string TypeOf(string line) => Extract(line, "\"type\":\"([^\"]+)\"");
    static string EpisodeOf(string line) => Extract(line, "\"episode\":(\\d+)");

    static string Extract(string line, string pattern)
    {
        Match match = Regex.Match(line, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    string LineOfType(string type)
    {
        string line = Lines().FirstOrDefault(l => TypeOf(l) == type);
        Assert.That(line, Is.Not.Null, $"no {type} line in:\n{captured}");
        return line;
    }

    [Test]
    public void LogEvent_WritesOneJsonLinePerCall()
    {
        logger.LogEvent("unit_test", JsonLine.Field("k", 1));
        logger.LogEvent("unit_test");

        string[] lines = Lines();
        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Does.Contain("\"type\":\"unit_test\"").And.Contain("\"k\":1"));
    }

    [UnityTest]
    public IEnumerator LethalShot_IsCountedInTheEpisodeItDecides()
    {
        CreateCounterHud();
        CreateGameManager(60f);
        Health target = CreateCombatant("NPC", new Vector3(0f, 0f, 10f), 50f); // one 50-damage shot kills
        Weapon weapon = CreateWeapon("Player", Vector3.zero);
        yield return null; // Health.Start finds the GameManager
        logger.RescanHealths();

        weapon.Shoot();

        Assert.That(Types(), Is.EqualTo(new[] { "shot", "damage", "death", "episode_summary" }));

        string[] lines = Lines();
        string episode = EpisodeOf(lines[0]);
        Assert.That(lines.Select(EpisodeOf), Is.All.EqualTo(episode),
            "the killing shot and the summary it triggers belong to the same episode");

        string summary = LineOfType("episode_summary");
        Assert.That(summary, Does.Contain("\"winner\":\"Player\""));
        Assert.That(summary, Does.Contain("\"Player\":{\"damageDealt\":50,\"damageTaken\":0,\"shots\":1,\"hits\":1,\"accuracy\":1}"),
            "the shot that ended the round must count in the round it ended");
        Assert.That(summary, Does.Contain("\"NPC\":{\"damageDealt\":0,\"damageTaken\":50,\"shots\":0,\"hits\":0,\"accuracy\":0}"));
        Assert.That(target.health, Is.LessThanOrEqualTo(0f));
    }

    [UnityTest]
    public IEnumerator NextEpisode_StartsCleanAfterARound()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(60f);
        yield return null;

        int before = logger.EpisodeIndex;
        gm.ProcessDeath("NPC");

        Assert.That(logger.EpisodeIndex, Is.EqualTo(before + 1));
        logger.LogEvent("unit_test");
        Assert.That(EpisodeOf(LineOfType("unit_test")), Is.EqualTo((before + 1).ToString()),
            "events after the summary belong to the next episode");
    }

    [UnityTest]
    public IEnumerator Timeout_WritesADrawSummary()
    {
        CreateCounterHud();
        CreateGameManager(0.3f);

        float safety = 5f;
        while (Time.timeScale > 0f && safety > 0f)
        {
            safety -= Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.That(Types(), Is.EqualTo(new[] { "episode_summary" }),
            "a timed-out round still gets exactly one summary");
        Assert.That(LineOfType("episode_summary"), Does.Contain($"\"winner\":\"{GameManager.DrawResult}\""));
    }

    [UnityTest]
    public IEnumerator FailingWrites_DisableTelemetryInsteadOfWedgingTheRound()
    {
        CreateCounterHud();
        CreateGameManager(60f);
        Health target = CreateCombatant("NPC", new Vector3(0f, 0f, 10f), 50f);
        yield return null;
        logger.RescanHealths();
        logger.SwapWriter(new ThrowingWriter());

        LogAssert.Expect(LogType.Warning, new Regex("Telemetry.*Write failed"));
        int userPointsBefore = CounterData.readUserPoints();

        // Health latches isDead before invoking OnDied, so an exception
        // escaping the logger here would lose the round for good.
        target.DecreaseHealth(100f);

        Assert.That(CounterData.readUserPoints(), Is.EqualTo(userPointsBefore + 1), "the death must still be processed");
        Assert.That(Time.timeScale, Is.Zero, "the round must still finish");
    }

    class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => throw new IOException("disk full");
    }
}
