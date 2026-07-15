using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Match telemetry event bus (issue #12). Writes one JSON line per event to
/// a per-session file under <c>persistentDataPath/Telemetry/</c>, so every
/// evaluation phase of the thesis (training debug, headless eval, LLM A/B,
/// human study) reads the same log format.
///
/// Events: session_start, shot (shooter, hit, victim, damage), damage
/// (victim, amount, position), death (entity, position), episode_summary
/// (winner, duration, per-side damage dealt/taken, shots, hits, accuracy),
/// session_end. Future systems (NPC mode changes) call
/// <see cref="LogEvent"/> directly.
///
/// Pure file IO + event subscriptions — no UI, no rendering — so it works
/// unchanged in <c>-batchmode</c>. Auto-bootstraps at startup (the scene is
/// binary serialized) and survives scene reloads via DontDestroyOnLoad,
/// resubscribing to the fresh Health components on every scene load.
/// </summary>
public class TelemetryLogger : MonoBehaviour
{
    public static TelemetryLogger Instance { get; private set; }

    class SideStats
    {
        public float damageDealt;
        public float damageTaken;
        public int shots;
        public int hits;
    }

    StreamWriter writer;
    string logPath;
    float episodeStartTime;
    int episodeIndex;
    readonly Dictionary<string, SideStats> sides = new Dictionary<string, SideStats>();
    readonly List<Health> subscribed = new List<Health>();

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
        LogEvent("session_start",
            F("scene", SceneManager.GetActiveScene().name),
            F("unity", Application.unityVersion),
            F("batchmode", Application.isBatchMode));

        Weapon.ShotFired += HandleShot;
        GameManager.RoundEnded += HandleRoundEnded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        BeginEpisode();
        SubscribeHealths();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Weapon.ShotFired -= HandleShot;
        GameManager.RoundEnded -= HandleRoundEnded;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeHealths();
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
        string dir = Path.Combine(Application.persistentDataPath, "Telemetry");
        Directory.CreateDirectory(dir);
        string stamp = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        logPath = Path.Combine(dir, $"session_{stamp}_{System.Diagnostics.Process.GetCurrentProcess().Id}.jsonl");
        writer = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true }; // flush per line: crash-safe
        Debug.Log($"[Telemetry] Logging to {logPath}");
    }

    void CloseSessionFile()
    {
        if (writer == null) return;
        LogEvent("session_end");
        writer.Dispose();
        writer = null;
    }

    /// <summary>
    /// Append one event line: {"t":game-time,"wall":ISO-8601,"type":...,fields}.
    /// Public so future systems (NPC mode changes, LLM decisions) can emit
    /// their own events through the same bus. Build fields with the
    /// pre-serialized fragments from callers, e.g. via string interpolation
    /// with invariant culture.
    /// </summary>
    public void LogEvent(string type, params string[] jsonFields)
    {
        if (writer == null) return;
        var sb = new StringBuilder(128);
        sb.Append("{\"t\":").Append(Time.time.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append(",\"wall\":\"").Append(System.DateTime.UtcNow.ToString("o")).Append('"');
        sb.Append(",\"episode\":").Append(episodeIndex);
        sb.Append(",\"type\":\"").Append(Escape(type)).Append('"');
        foreach (string field in jsonFields)
        {
            sb.Append(',').Append(field);
        }
        sb.Append('}');
        writer.WriteLine(sb.ToString());
    }

    // JSON field helpers — key is trusted (compile-time literals), value is escaped.
    static string F(string key, string value) => $"\"{key}\":\"{Escape(value)}\"";
    static string F(string key, float value) => $"\"{key}\":{value.ToString("0.###", CultureInfo.InvariantCulture)}";
    static string F(string key, int value) => $"\"{key}\":{value}";
    static string F(string key, bool value) => $"\"{key}\":{(value ? "true" : "false")}";
    static string F(string key, Vector3 v) =>
        $"\"{key}\":[{v.x.ToString("0.##", CultureInfo.InvariantCulture)},{v.y.ToString("0.##", CultureInfo.InvariantCulture)},{v.z.ToString("0.##", CultureInfo.InvariantCulture)}]";

    static string Escape(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ------------------------------------------------------- subscriptions

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Scene reload = new round: fresh Health instances to listen to.
        UnsubscribeHealths();
        SubscribeHealths();
        BeginEpisode();
    }

    void SubscribeHealths()
    {
        foreach (Health health in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            Health h = health; // capture per instance
            h.OnDamaged += amount => HandleDamaged(h, amount);
            h.OnDied += () => HandleDied(h);
            subscribed.Add(h);
        }
    }

    void UnsubscribeHealths()
    {
        // Health exposes no per-listener removal for anonymous closures; the
        // instances themselves die with the scene, so dropping our refs is
        // enough — dead instances never fire again.
        subscribed.Clear();
    }

    // ------------------------------------------------------------- events

    void HandleShot(Weapon weapon, Health victim, float damage)
    {
        if (weapon == null) return;
        string shooter = weapon.transform.root.tag;
        bool hit = victim != null;
        LogEvent("shot",
            F("shooter", shooter),
            F("hit", hit),
            F("victim", hit ? victim.gameObject.tag : ""),
            F("damage", hit ? damage : 0f),
            F("pos", weapon.transform.position));

        SideStats stats = GetSide(shooter);
        stats.shots++;
        if (hit)
        {
            stats.hits++;
            stats.damageDealt += damage;
        }
    }

    void HandleDamaged(Health victim, float amount)
    {
        LogEvent("damage",
            F("victim", victim.gameObject.tag),
            F("amount", amount),
            F("remaining", victim.health),
            F("pos", victim.transform.position));
        GetSide(victim.gameObject.tag).damageTaken += amount;
    }

    void HandleDied(Health victim)
    {
        LogEvent("death",
            F("entity", victim.gameObject.tag),
            F("pos", victim.transform.position),
            F("survivalSeconds", Time.time - episodeStartTime));
    }

    void HandleRoundEnded(string winner)
    {
        float duration = Time.time - episodeStartTime;
        var sb = new StringBuilder(64);
        sb.Append("\"sides\":{");
        bool first = true;
        foreach (KeyValuePair<string, SideStats> kv in sides)
        {
            if (!first) sb.Append(',');
            first = false;
            SideStats s = kv.Value;
            float accuracy = s.shots > 0 ? (float)s.hits / s.shots : 0f;
            sb.Append('"').Append(Escape(kv.Key)).Append("\":{")
              .Append(F("damageDealt", s.damageDealt)).Append(',')
              .Append(F("damageTaken", s.damageTaken)).Append(',')
              .Append(F("shots", s.shots)).Append(',')
              .Append(F("hits", s.hits)).Append(',')
              .Append(F("accuracy", accuracy)).Append('}');
        }
        sb.Append('}');

        // durationSeconds doubles as time-to-kill: rounds end on the kill.
        LogEvent("episode_summary",
            F("winner", winner),
            F("durationSeconds", duration),
            sb.ToString());
        BeginEpisode();
    }

    SideStats GetSide(string tag)
    {
        if (!sides.TryGetValue(tag, out SideStats stats))
        {
            stats = new SideStats();
            sides[tag] = stats;
        }
        return stats;
    }

    void BeginEpisode()
    {
        // In training the next episode starts immediately after the summary
        // (no scene reload); in play mode this also runs on scene load.
        episodeIndex++;
        episodeStartTime = Time.time;
        sides.Clear();
    }
}
