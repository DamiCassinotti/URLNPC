using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// EnemyBehavior.IsHiddenFromTarget — the in-cover flag the HoldCover/Retreat
// reward columns are paid from (#44). Cover has to be geometry between the two
// bodies; neither body may count as its own cover.
public class CoverFlagTests : PlayModeTestBase
{
    const float EyeHeight = 0.5f;

    EnemyBehavior behavior;
    GameObject player;

    IEnumerator BuildPair()
    {
        player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.name = "TestPlayer";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, 10f);

        GameObject enemyGo = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        enemyGo.name = "TestEnemy";
        enemyGo.SetActive(false);
        // A low eye puts both ends of the probe inside their own capsule, which
        // is the case a plain raycast gets wrong: it stops on the enemy's own
        // body and calls the open floor cover. No NavMesh here, so keep it off.
        NavMeshAgent nav = enemyGo.AddComponent<NavMeshAgent>();
        nav.height = EyeHeight;
        nav.baseOffset = 0f;
        nav.enabled = false;
        behavior = enemyGo.AddComponent<EnemyBehavior>();
        behavior.enabled = false; // skip Start's random respawn: there is nowhere to spawn
        behavior.target = player.transform;
        enemyGo.SetActive(true);  // Awake caches the agent and the memories

        Physics.SyncTransforms();
        yield return null;
    }

    [UnityTest]
    public IEnumerator InTheOpen_NeitherBodyIsItsOwnCover()
    {
        yield return BuildPair();

        Vector3 threatEye = player.transform.position + Vector3.up * EyeHeight;
        Vector3 toSelf = (behavior.transform.position + Vector3.up * EyeHeight) - threatEye;
        Assert.That(Physics.Raycast(threatEye, toSelf.normalized, toSelf.magnitude - 0.1f), Is.True,
            "sanity: a plain raycast between the eyes does stop on the enemy's own capsule");

        Assert.That(behavior.IsHiddenFromTarget(), Is.False,
            "with clear ground between them the only colliders on the eye-line are the two bodies");
    }

    [UnityTest]
    public IEnumerator GeometryBetweenThem_IsCover()
    {
        yield return BuildPair();

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0f, 1.5f, 5f);
        wall.transform.localScale = new Vector3(10f, 3f, 1f);
        Physics.SyncTransforms();

        Assert.That(behavior.IsHiddenFromTarget(), Is.True);
    }

    [UnityTest]
    public IEnumerator NoTarget_IsNotCover()
    {
        yield return BuildPair();
        behavior.target = null;

        Assert.That(behavior.IsHiddenFromTarget(), Is.False,
            "nothing to hide from is not the same as being hidden");
    }
}
