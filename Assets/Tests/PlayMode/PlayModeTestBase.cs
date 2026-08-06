using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

// Shared scaffolding for PlayMode integration tests. Guarantees per test:
// - No auto-spawned arena. ArenaManager's bootstrap fires on every scene load,
//   including the test runner's own init scene, so the seam suppresses it and
//   anything that already snuck in is swept.
// - The real score tally survives: PlayerPrefs are snapshotted and restored.
// - Engine state is restored: Time.timeScale (FinishRound zeroes it), cursor,
//   NavMesh data and RunRng's static streams.
public abstract class PlayModeTestBase
{
    // Must match the private keys in CounterData.cs.
    static readonly string[] PrefKeys = { "URLNPC.userPoints", "URLNPC.npcPoints", "URLNPC.draws" };
    readonly bool[] hadKey = new bool[PrefKeys.Length];
    readonly int[] savedValue = new int[PrefKeys.Length];

    readonly List<GameObject> tracked = new List<GameObject>();

    [SetUp]
    public void BaseSetUp()
    {
        ArenaManager.suppressAutoBootstrap = true;
        SweepArenas();

        for (int i = 0; i < PrefKeys.Length; i++)
        {
            hadKey[i] = PlayerPrefs.HasKey(PrefKeys[i]);
            savedValue[i] = PlayerPrefs.GetInt(PrefKeys[i], 0);
        }

        Time.timeScale = 1f;
        RunRng.ResetForNewRun();
    }

    [TearDown]
    public void BaseTearDown()
    {
        foreach (GameObject go in tracked)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        tracked.Clear();
        SweepArenas();

        for (int i = 0; i < PrefKeys.Length; i++)
        {
            if (hadKey[i]) PlayerPrefs.SetInt(PrefKeys[i], savedValue[i]);
            else PlayerPrefs.DeleteKey(PrefKeys[i]);
        }
        PlayerPrefs.Save();

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RunRng.ResetForNewRun();
        ArenaManager.suppressAutoBootstrap = false;
    }

    // Register a spawned object for teardown destruction.
    protected GameObject Track(GameObject go)
    {
        tracked.Add(go);
        return go;
    }

    // Enough for GameManager.Start to find a Counter and hang its runtime timer
    // text on the same canvas.
    protected Counter CreateCounterHud()
    {
        GameObject canvasGo = Track(new GameObject("TestHud", typeof(Canvas)));
        var labelGo = new GameObject("Score");
        labelGo.transform.SetParent(canvasGo.transform, false);
        TMP_Text label = labelGo.AddComponent<TextMeshProUGUI>();
        var counter = canvasGo.AddComponent<Counter>();
        counter.counter = label;
        return counter;
    }

    // Created inactive so the round duration is in place before Start arms the
    // clock; the caller must yield a frame for Start to run.
    protected GameManager CreateGameManager(float roundDuration)
    {
        GameObject go = Track(new GameObject("TestGameManager"));
        go.SetActive(false);
        var gm = go.AddComponent<GameManager>();
        gm.roundDurationSeconds = roundDuration;
        go.SetActive(true);
        return gm;
    }

    // A combatant stand-in: a cube with a collider (weapons raycast) and Health.
    protected Health CreateCombatant(string tag, Vector3 position, float startingHealth)
    {
        GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        go.name = $"Test{tag}";
        go.tag = tag;
        go.transform.position = position;
        // Physics.autoSyncTransforms is off: without this the collider stays
        // at the origin until the next FixedUpdate, so a weapon firing in the
        // same frame starts its ray inside the box and reports a miss.
        Physics.SyncTransforms();
        Health health = go.AddComponent<Health>();
        health.maxHealth = 100f;
        health.health = startingHealth;
        return health;
    }

    // Aimed along +Z. Weapon reports the shooter by root tag, so the owner tag
    // goes on the weapon's own GameObject.
    protected Weapon CreateWeapon(string ownerTag, Vector3 position)
    {
        GameObject go = Track(new GameObject($"Test{ownerTag}Weapon"));
        go.tag = ownerTag;
        go.transform.position = position;
        go.transform.forward = Vector3.forward;
        var weapon = go.AddComponent<TestWeapon>();
        weapon.muzzle = go.transform;
        return weapon;
    }

    class TestWeapon : Weapon
    {
        public Transform muzzle;
        protected override Vector3 GetPosition() => muzzle.position;
        protected override Vector3 GetForward() => muzzle.forward;
    }

    // Removes any ArenaManager — auto-bootstrapped before the first SetUp could
    // suppress it, or left over from a test — plus its geometry and NavMesh.
    protected static void SweepArenas()
    {
        foreach (ArenaManager manager in Object.FindObjectsByType<ArenaManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(manager.gameObject);
        }
        GameObject arena;
        while ((arena = GameObject.Find("Arena (Generated)")) != null)
        {
            Object.DestroyImmediate(arena);
        }
        NavMesh.RemoveAllNavMeshData();
    }
}
