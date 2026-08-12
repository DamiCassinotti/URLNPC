using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Match telemetry (issue #12): one JSON line per event under
// persistentDataPath/Telemetry/, parseable without a Unity build. File IO
// only, so it works in -batchmode. Auto-bootstraps at startup (the scene is
// binary serialized) and survives scene reloads via DontDestroyOnLoad.
public class TelemetryLogger : MonoBehaviour
{
    public static TelemetryLogger Instance { get; private set; }

    TextWriter writer;
    string logPath;
    readonly EpisodeLog episodes = new EpisodeLog();
    readonly HashSet<Health> subscribed = new HashSet<Health>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrap()
    {
        if (Instance != null) return;
        new GameObject("TelemetryLogger").AddComponent<TelemetryLogger>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        OpenSessionFile();
        episodes.Begin(Time.time);
        LogEvent("session_start",
            JsonLine.Field("scene", SceneManager.GetActiveScene().name),
            JsonLine.Field("unity", Application.unityVersion),
            JsonLine.Field("batchmode", Application.isBatchMode));

        Weapon.ShotFired += HandleShot;
        GameManager.RoundEnded += HandleRoundEnded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeHealths();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Weapon.ShotFired -= HandleShot;
        GameManager.RoundEnded -= HandleRoundEnded;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        CloseSessionFile();
        Instance = null;
    }

    void OnApplicationQuit()
    {
        CloseSessionFile();
    }

    // ---------------------------------------------------------------- file

    void OpenSessionFile()
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "Telemetry");
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            logPath = Path.Combine(dir, $"session_{stamp}_{System.Diagnostics.Process.GetCurrentProcess().Id}.jsonl");
            writer = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true }; // flush per line: crash-safe
            Debug.Log($"[Telemetry] Logging to {logPath}");
        }
        catch (System.Exception e)
        {
            writer = null;
            Debug.LogWarning($"[Telemetry] Could not open the session file — telemetry disabled: {e.Message}");
        }
    }

    void CloseSessionFile()
    {
        if (writer == null) return;
        LogEvent("session_end");
        CloseWriter();
    }

    void CloseWriter()
    {
        TextWriter w = writer;
        writer = null;
        if (w == null) return;
        try { w.Dispose(); }
        catch (IOException) { } // already broken, nothing left to salvage
    }

    // Public so future systems (NPC mode changes, LLM decisions) emit through
    // the same bus; build fields with JsonLine.Field.
    public void LogEvent(string type, params string[] jsonFields)
    {
        if (writer == null) return;
        string line = JsonLine.Build(Time.time, System.DateTime.UtcNow.ToString("o"), episodes.Index, type, jsonFields);
        try
        {
            writer.WriteLine(line);
        }
        catch (System.Exception e)
        {
            // Must never escape: this runs inside Health.OnDied's multicast
            // chain, where isDead is already latched — throwing here would
            // skip ProcessDeath and wedge the round for good.
            Debug.LogWarning($"[Telemetry] Write failed — telemetry disabled for this session: {e.Message}");
            CloseWriter();
        }
    }

    // ------------------------------------------------------- subscriptions

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeHealths();
        episodes.SceneLoaded(Time.time);
    }

    // Instances that died with the old scene never fire again, so nothing
    // needs unsubscribing; the set only keeps a rescan from double-counting
    // the ones still alive.
    void SubscribeHealths()
    {
        subscribed.RemoveWhere(h => h == null);
        foreach (Health health in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            Health h = health; // capture per instance
            if (!subscribed.Add(h)) continue;
            h.OnDamaged += info => HandleDamaged(h, info);
            h.OnDied += () => HandleDied(h);
        }
    }

    // ------------------------------------------------------------- events

    void HandleShot(Weapon weapon, Health victim, float damage)
    {
        if (weapon == null) return;
        string shooter = weapon.transform.root.tag;
        bool hit = victim != null;
        LogEvent("shot",
            JsonLine.Field("shooter", shooter),
            JsonLine.Field("hit", hit),
            JsonLine.Field("victim", hit ? victim.gameObject.tag : ""),
            JsonLine.Field("damage", hit ? damage : 0f),
            JsonLine.Field("pos", weapon.transform.position));
        episodes.RecordShot(shooter, hit, damage);
    }

    void HandleDamaged(Health victim, DamageInfo info)
    {
        LogEvent("damage",
            JsonLine.Field("victim", victim.gameObject.tag),
            JsonLine.Field("amount", info.Amount),
            JsonLine.Field("remaining", victim.health),
            JsonLine.Field("pos", victim.transform.position));
        episodes.RecordDamage(victim.gameObject.tag, info.Amount);
    }

    void HandleDied(Health victim)
    {
        LogEvent("death",
            JsonLine.Field("entity", victim.gameObject.tag),
            JsonLine.Field("pos", victim.transform.position),
            JsonLine.Field("survivalSeconds", episodes.Elapsed(Time.time)));
    }

    void HandleRoundEnded(string winner)
    {
        // durationSeconds doubles as time-to-kill on decided rounds; on a
        // timeout (winner "Draw") it is just the round length.
        LogEvent("episode_summary",
            JsonLine.Field("winner", winner),
            JsonLine.Field("durationSeconds", episodes.Elapsed(Time.time)),
            episodes.SidesJson());
        episodes.EndRound(Time.time);
    }

    // ------------------------------------------------------------- seams

    internal string LogPath => logPath;
    internal int EpisodeIndex => episodes.Index;

    // Lets a test capture the stream (or force writes to fail) without
    // touching the session file.
    internal TextWriter SwapWriter(TextWriter replacement)
    {
        TextWriter previous = writer;
        writer = replacement;
        return previous;
    }

    // Health subscriptions are a scene-load-time scan; tests spawn their
    // combatants afterwards.
    internal void RescanHealths() => SubscribeHealths();
}
