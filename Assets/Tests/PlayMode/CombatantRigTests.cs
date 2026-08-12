using System.Collections;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using DriverKind = CombatantRig.DriverKind;

// CombatantRig composing the agent driver onto the player body (issue #10). The
// scene is force-binary serialized, so this stack can never be reviewed in the
// editor — it only exists as the product of EnableAgentDriver, which is why it
// is worth asserting piece by piece.
//
// No NavMesh in this fixture, so the NavMeshAgent the rig adds and
// EnemyBehavior.Start's respawn both complain. Those logs are expected.
public class CombatantRigTests : PlayModeTestBase
{
    GameObject body;

    [TearDown]
    public void ClearDriverOverride()
    {
        CombatantRig.DriverOverride = null;
        LogAssert.ignoreFailingMessages = false;
    }

    // A player body carrying what the rig expects to already be there. Built
    // inactive so the driver override is in place before Awake resolves.
    IEnumerator BuildBody(DriverKind driver)
    {
        // The rig reads the real command line first, so an editor launched with
        // -playerDriver would outrank the override these tests rely on.
        foreach (string arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == DriverSelector.CommandLineArg)
            {
                Assert.Ignore("Editor was launched with -playerDriver; driver-override tests do not apply.");
            }
        }

        LogAssert.ignoreFailingMessages = true; // no NavMesh in this fixture

        body = Track(new GameObject("TestPlayerBody"));
        body.SetActive(false);
        body.tag = "Player";
        body.AddComponent<CharacterController>();
        body.AddComponent<Health>();
        body.AddComponent<PlayerWeapon>();

        CombatantRig.DriverOverride = driver;
        body.AddComponent<CombatantRig>();
        body.SetActive(true); // Awake resolves the driver and composes

        yield return null; // let Start run on whatever the rig added
    }

    [UnityTest]
    public IEnumerator AgentDriver_LeavesMaxStepAtZeroSoTheRoundClockOwnsTimeout()
    {
        yield return BuildBody(DriverKind.Agent);

        // Regression guard: the rig used to set MaxStep to 5000 here,
        // reinstating on the player side the stale value the enemy had just
        // been fixed for. EnemyAgentTests only covers the Enemy prefab, which
        // never sees the runtime-composed player.
        var agent = body.GetComponent<PlayerAgent>();
        Assert.That(agent, Is.Not.Null, "the agent driver must install a PlayerAgent");
        Assert.That(agent.MaxStep, Is.Zero,
            "a nonzero MaxStep would end the player's episode before the round clock fires");
    }

    [UnityTest]
    public IEnumerator AgentDriver_SilencesEveryHumanInputPath()
    {
        yield return BuildBody(DriverKind.Agent);

        var cc = body.GetComponent<CharacterController>();
        Assert.That(cc.enabled, Is.False, "CharacterController fights NavMeshAgent for the transform");
        Assert.That(body.GetComponent<PlayerWeapon>().enabled, Is.False,
            "mouse fire must not survive alongside the agent's weapon");
    }

    [UnityTest]
    public IEnumerator AgentDriver_InstallsTheEnemyCombatStackAimedAtTheNpc()
    {
        yield return BuildBody(DriverKind.Agent);

        Assert.That(body.GetComponent<NavMeshAgent>(), Is.Not.Null, "agent locomotion is NavMesh-driven");
        Assert.That(body.GetComponent<EnemyWeapon>(), Is.Not.Null, "the agent fires through EnemyWeapon");

        var behavior = body.GetComponent<EnemyBehavior>();
        Assert.That(behavior, Is.Not.Null);
        Assert.That(behavior.targetTag, Is.EqualTo("NPC"),
            "the agent-driven player hunts the enemy, not itself");

        // EnemyBehavior.Awake auto-adds the sensory contract and the rest of the
        // brain's inputs; the agent-driven player must be bound by them exactly
        // like the enemy is, or the shared policy gets a different vector.
        Assert.That(behavior.Perception, Is.Not.Null, "PerceptionMemory must be present on the player side too");
        Assert.That(behavior.Damage, Is.Not.Null, "the hit-direction observations need a DamageMemory");
        Assert.That(behavior.Mode, Is.Not.Null, "the mode one-hot needs a ModeChannel");
    }

    [UnityTest]
    public IEnumerator AgentDriver_MatchesTheEnemyPolicyContract()
    {
        yield return BuildBody(DriverKind.Agent);

        var bp = body.GetComponent<BehaviorParameters>();
        Assert.That(bp, Is.Not.Null, "ML-Agents cannot drive the body without BehaviorParameters");
        Assert.That(bp.BehaviorName, Is.EqualTo("URLNPC"),
            "sharing the enemy's behavior name is what makes this self-play against one policy");
        Assert.That(bp.TeamId, Is.Not.EqualTo(0),
            "the player side needs a team id distinct from the enemy's 0 for adversarial self-play");

        // Same observation/action shape as the Enemy prefab, or the shared
        // policy cannot be applied to both sides.
        Assert.That(bp.BrainParameters.VectorObservationSize, Is.EqualTo(NpcBrainSpec.ObservationSize));
        Assert.That(bp.BrainParameters.NumStackedVectorObservations, Is.EqualTo(1));
        Assert.That(bp.BrainParameters.ActionSpec.BranchSizes,
            Is.EqualTo(new[] { NpcBrainSpec.MovementBranchSize, NpcBrainSpec.FireBranchSize }),
            "two discrete branches: movement x fire");

        var requester = body.GetComponent<DecisionRequester>();
        Assert.That(requester, Is.Not.Null,
            "without a DecisionRequester no decisions are requested and the body stands still");
        Assert.That(requester.DecisionPeriod, Is.EqualTo(5), "same cadence as the Enemy prefab");
    }

    [UnityTest]
    public IEnumerator HumanDriver_LeavesTheBodyExactlyAsAuthored()
    {
        yield return BuildBody(DriverKind.Human);

        Assert.That(body.GetComponent<CharacterController>().enabled, Is.True);
        Assert.That(body.GetComponent<PlayerWeapon>().enabled, Is.True);

        // Nothing from the agent stack may be composed in human mode.
        Assert.That(body.GetComponent<PlayerAgent>(), Is.Null);
        Assert.That(body.GetComponent<BehaviorParameters>(), Is.Null);
        Assert.That(body.GetComponent<DecisionRequester>(), Is.Null);
        Assert.That(body.GetComponent<NavMeshAgent>(), Is.Null);
        Assert.That(body.GetComponent<EnemyBehavior>(), Is.Null);
        Assert.That(body.GetComponent<EnemyWeapon>(), Is.Null);
    }

    [UnityTest]
    public IEnumerator ResolvedDriver_IsReportedOnTheRig()
    {
        yield return BuildBody(DriverKind.Agent);
        Assert.That(body.GetComponent<CombatantRig>().ActiveDriver, Is.EqualTo(DriverKind.Agent));
    }
}
