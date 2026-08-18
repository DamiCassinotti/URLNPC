using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// The mode timeline and the per-episode compliance tally as they reach the
// JSONL (#45): a real agent, a real ModeChannel, with the session file swapped
// for an in-memory writer. The POCO rules are pinned in
// ModeComplianceTrackerTests; this is the wiring and the episode it lands in.
public class ModeTelemetryTests : PlayModeTestBase
{
    TelemetryLogger logger;
    StringWriter captured;
    TextWriter sessionWriter;

    EnemyAgent agent;
    EnemyBehavior behavior;
    Health selfHealth;

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

    IEnumerator BuildAgentScene()
    {
        // The episode reset respawns the enemy with no NavMesh under it, which
        // logs placement complaints this fixture expects.
        LogAssert.ignoreFailingMessages = true;

        GameObject player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, 10f);
        player.AddComponent<Health>();

        GameObject enemyGo = Track(new GameObject("TestEnemyAgent"));
        enemyGo.tag = "NPC";
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>().enabled = false;
        agent = enemyGo.AddComponent<EnemyAgent>();
        behavior = enemyGo.GetComponent<EnemyBehavior>();
        behavior.enabled = false; // no NavMesh in this fixture: skip Start's spawn
        behavior.target = player.transform;
        selfHealth = enemyGo.GetComponent<Health>();
        enemyGo.SetActive(true);
        yield return null;
    }

    // One decision step's worth of actions. Move no-ops without a NavMesh, so
    // what this really drives is the bookkeeping around it.
    void Step(MovementAction movement)
    {
        var discrete = new ActionSegment<int>(new[] { (int)movement, NpcBrainSpec.DontFire });
        agent.OnActionReceived(new ActionBuffers(ActionSegment<float>.Empty, discrete));
    }

    string[] Lines()
    {
        return captured.ToString().Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }

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

    [UnityTest]
    public IEnumerator ModeChanges_AreLoggedAsTheyHappen()
    {
        yield return BuildAgentScene();
        ModeChannel channel = agent.GetComponent<ModeChannel>();

        channel.SetMode(NpcMode.Retreat);
        channel.SetMode(NpcMode.Retreat); // a re-command of the same mode is not a change

        string[] changes = Lines().Where(l => TypeOf(l) == "mode_change").ToArray();
        Assert.That(changes.Length, Is.EqualTo(1), "one event per mode interval");
        Assert.That(changes[0], Does.Contain("\"entity\":\"NPC\"")
            .And.Contain("\"from\":\"Hunt\"").And.Contain("\"to\":\"Retreat\""));
    }

    [UnityTest]
    public IEnumerator EpisodeEnd_ReportsComplianceForTheModesItRan()
    {
        yield return BuildAgentScene();
        agent.GetComponent<ModeChannel>().SetMode(NpcMode.Retreat);

        Step(MovementAction.Retreat);
        Step(MovementAction.Retreat);
        agent.OnRoundTimeout();

        string line = LineOfType("mode_compliance");
        Assert.That(line, Does.Contain("\"entity\":\"NPC\""));
        Assert.That(line, Does.Contain("\"compliance\":{\"Retreat\":{\"steps\":2"),
            "both decision steps were commanded Retreat");
        // Same line, same steps (#87): the compliance rate is only readable
        // against how often the mode had the target to act on.
        Assert.That(line, Does.Contain("\"visible\":{\"Retreat\":{\"steps\":2"));
        Assert.That(line, Does.Not.Contain("Hunt"), "a mode the episode never ran has no rate");
    }

    [UnityTest]
    public IEnumerator Compliance_IsReportedInTheEpisodeItDescribes()
    {
        CreateCounterHud();
        CreateGameManager(60f);
        yield return BuildAgentScene();
        logger.RescanHealths();

        Step(MovementAction.Advance);
        // Death runs the whole cascade: the agent's own handler, then
        // GameManager.ProcessDeath, then the summary that closes the episode.
        selfHealth.DecreaseHealth(selfHealth.maxHealth + 1f);

        string[] types = Lines().Select(TypeOf).ToArray();
        int complianceAt = System.Array.IndexOf(types, "mode_compliance");
        int summaryAt = System.Array.IndexOf(types, "episode_summary");
        Assert.That(complianceAt, Is.GreaterThanOrEqualTo(0), $"no mode_compliance line in:\n{captured}");
        Assert.That(summaryAt, Is.GreaterThan(complianceAt),
            "the summary is what moves the log on to the next episode");
        Assert.That(EpisodeOf(LineOfType("mode_compliance")),
            Is.EqualTo(EpisodeOf(LineOfType("episode_summary"))));
    }

    [UnityTest]
    public IEnumerator TheNextEpisode_StartsWithAnEmptyTally()
    {
        yield return BuildAgentScene();

        Step(MovementAction.Advance);
        agent.OnRoundTimeout();   // flushes and resets
        agent.OnRoundTimeout();   // nothing ran in the new episode

        Assert.That(Lines().Count(l => TypeOf(l) == "mode_compliance"), Is.EqualTo(1),
            "an episode with no decision steps has nothing to report");
    }
}
