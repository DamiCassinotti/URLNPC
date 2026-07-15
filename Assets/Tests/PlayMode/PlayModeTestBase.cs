using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared scaffolding for PlayMode integration tests. Guarantees per test:
/// - No auto-spawned arena: ArenaManager's bootstrap fires on every scene
///   load (including the test runner's init scene), so the seam suppresses it
///   and any manager/arena that already snuck in is swept.
/// - The real score tally survives: PlayerPrefs values are snapshotted in
///   SetUp and restored in TearDown.
/// - Engine state is restored: Time.timeScale (GameManager.FinishRound sets
///   it to 0), cursor state, NavMesh data, and RunRng's static streams.
/// Spawned objects registered via Track() are destroyed on teardown.
/// </summary>
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

    /// <summary>Register a spawned object for teardown destruction.</summary>
    protected GameObject Track(GameObject go)
    {
        tracked.Add(go);
        return go;
    }

    /// <summary>
    /// A minimal HUD: Canvas + TMP label wired into a Counter, enough for
    /// GameManager.Start to find a Counter and hang its runtime timer text on
    /// the same canvas.
    /// </summary>
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

    /// <summary>
    /// GameManager on a bare GameObject. Created inactive so the round
    /// duration is in place before Start arms the clock; the caller must
    /// yield a frame for Start to run.
    /// </summary>
    protected GameManager CreateGameManager(float roundDuration)
    {
        GameObject go = Track(new GameObject("TestGameManager"));
        go.SetActive(false);
        var gm = go.AddComponent<GameManager>();
        gm.roundDurationSeconds = roundDuration;
        go.SetActive(true);
        return gm;
    }

    /// <summary>
    /// Remove any ArenaManager (auto-bootstrapped before the first SetUp
    /// could suppress it, or left over from a test) plus its generated
    /// geometry and NavMesh.
    /// </summary>
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
